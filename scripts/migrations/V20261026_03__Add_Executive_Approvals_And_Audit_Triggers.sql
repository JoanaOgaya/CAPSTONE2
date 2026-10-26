-- Migration: V20261026_03__Add_Executive_Approvals_And_Audit_Triggers.sql
-- Description: Adds ExecutiveApprovalRecords, Archive_AuditLogs, triggers TR_AuditLogs_PreventUpdate/Delete, dedicated user foms_archive_principal, and sp_ArchiveRetentionRecords.

-- 1. ExecutiveApprovalRecords Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExecutiveApprovalRecords')
BEGIN
    CREATE TABLE ExecutiveApprovalRecords (
        approval_id INT IDENTITY(1,1) PRIMARY KEY,
        approver_title NVARCHAR(50) NOT NULL,
        approver_name NVARCHAR(150) NOT NULL,
        approval_date DATE NOT NULL,
        decision NVARCHAR(50) NOT NULL,
        approved_amount DECIMAL(18,2) NOT NULL,
        transaction_type NVARCHAR(100) NOT NULL,
        transaction_reference NVARCHAR(100) NOT NULL,
        proof_attachment_url NVARCHAR(500) NOT NULL,
        recorded_by NVARCHAR(100) NOT NULL,
        is_delegated BIT NOT NULL DEFAULT 0,
        delegate_justification NVARCHAR(500) NULL,
        supersedes_approval_id INT NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        
        -- Check Constraints
        CONSTRAINT CK_ExecApproval_Title CHECK (approver_title IN ('CEO', 'President')),
        CONSTRAINT CK_ExecApproval_Decision CHECK (decision IN ('Approved', 'Rejected')),
        CONSTRAINT CK_ExecApproval_Amount CHECK (approved_amount >= 0),
        CONSTRAINT CK_ExecApproval_Type CHECK (transaction_type IN ('Discount', 'LargeCashOutflow', 'BudgetOverride', 'BudgetTransfer', 'BudgetCarryOver')),
        CONSTRAINT CK_ExecApproval_Delegate CHECK (is_delegated = 0 OR (is_delegated = 1 AND delegate_justification IS NOT NULL AND LEN(LTRIM(RTRIM(delegate_justification))) > 0))
    );

    CREATE UNIQUE INDEX UQ_Active_ExecApproval_Ref 
    ON ExecutiveApprovalRecords(transaction_reference) 
    WHERE is_active = 1;

    CREATE INDEX IX_ExecApproval_TypeRef ON ExecutiveApprovalRecords(transaction_type, transaction_reference);
END;
GO

-- 2. Archive_AuditLogs Table (Append-only)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Archive_AuditLogs')
BEGIN
    CREATE TABLE Archive_AuditLogs (
        archive_id INT IDENTITY(1,1) PRIMARY KEY,
        audit_id NVARCHAR(100) NOT NULL,
        user_id NVARCHAR(100) NOT NULL,
        role NVARCHAR(50) NOT NULL,
        action NVARCHAR(100) NOT NULL,
        module NVARCHAR(100) NOT NULL,
        affected_record_id NVARCHAR(100) NOT NULL,
        affected_record_type NVARCHAR(100) NOT NULL,
        before_state NVARCHAR(MAX) NULL,
        after_state NVARCHAR(MAX) NULL,
        details NVARCHAR(MAX) NULL,
        ip_address NVARCHAR(50) NULL,
        created_at DATETIME2 NOT NULL,
        archived_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Archive_AuditLogs_Record ON Archive_AuditLogs(affected_record_type, affected_record_id);
    CREATE INDEX IX_Archive_AuditLogs_ArchivedAt ON Archive_AuditLogs(archived_at);
END;
GO

-- 3. AuditLogs Internal Retention Column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLogs') AND name = 'internal_retention_until')
BEGIN
    ALTER TABLE dbo.AuditLogs ADD internal_retention_until DATETIME2 NULL;
END;
GO

-- Backfill 5-year internal retention on AuditLogs
UPDATE dbo.AuditLogs 
SET internal_retention_until = DATEADD(year, 5, ISNULL(created_at, SYSUTCDATETIME()))
WHERE internal_retention_until IS NULL;
GO

-- 4. Dedicated Archive Principal (WITHOUT LOGIN)
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'foms_archive_principal')
BEGIN
    CREATE USER foms_archive_principal WITHOUT LOGIN;
END;
GO

GRANT SELECT, INSERT ON dbo.Archive_AuditLogs TO foms_archive_principal;
GRANT SELECT, DELETE ON dbo.AuditLogs TO foms_archive_principal;
GO

-- 5. Anti-Tamper Triggers on AuditLogs (Prevents UPDATE and DELETE by Application Principal)
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventUpdate')
    DROP TRIGGER dbo.TR_AuditLogs_PreventUpdate;
GO
CREATE TRIGGER dbo.TR_AuditLogs_PreventUpdate
ON dbo.AuditLogs
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF USER_NAME() <> 'foms_archive_principal'
    BEGIN
        RAISERROR ('AUD-01 VIOLATION: Updates to dbo.AuditLogs are strictly prohibited. Audit logs are immutable and append-only.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;
END;
GO

IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventDelete')
    DROP TRIGGER dbo.TR_AuditLogs_PreventDelete;
GO
CREATE TRIGGER dbo.TR_AuditLogs_PreventDelete
ON dbo.AuditLogs
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF USER_NAME() <> 'foms_archive_principal'
    BEGIN
        RAISERROR ('AUD-01 VIOLATION: Deletions from dbo.AuditLogs by application users are strictly prohibited. Purging is permitted solely via the dedicated archive procedure running under foms_archive_principal.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;
END;
GO

-- 6. Stored Procedure: sp_ArchiveRetentionRecords (Runs with EXECUTE AS 'foms_archive_principal')
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_ArchiveRetentionRecords')
    DROP PROCEDURE dbo.sp_ArchiveRetentionRecords;
GO
CREATE PROCEDURE dbo.sp_ArchiveRetentionRecords
    @RetentionYears INT = 5,
    @RowsArchived INT = 0 OUTPUT
WITH EXECUTE AS 'foms_archive_principal'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CutoffDate DATETIME2 = DATEADD(year, -@RetentionYears, SYSUTCDATETIME());

    BEGIN TRANSACTION;

    INSERT INTO dbo.Archive_AuditLogs (
        audit_id, user_id, role, action, module, affected_record_id, affected_record_type,
        before_state, after_state, details, ip_address, created_at, archived_at
    )
    SELECT 
        CAST(Id AS NVARCHAR(100)),
        ISNULL(UserId, 'SYSTEM'),
        ISNULL(Role, 'SYSTEM'),
        Action,
        Module,
        ISNULL(RecordId, 'N/A'),
        Module,
        OldValues,
        NewValues,
        Details,
        IpAddress,
        Timestamp,
        SYSUTCDATETIME()
    FROM dbo.AuditLogs
    WHERE (internal_retention_until IS NOT NULL AND internal_retention_until < SYSUTCDATETIME())
       OR (internal_retention_until IS NULL AND Timestamp < @CutoffDate);

    SET @RowsArchived = @@ROWCOUNT;

    DELETE FROM dbo.AuditLogs
    WHERE (internal_retention_until IS NOT NULL AND internal_retention_until < SYSUTCDATETIME())
       OR (internal_retention_until IS NULL AND Timestamp < @CutoffDate);

    COMMIT TRANSACTION;

    RETURN 0;
END;
GO
