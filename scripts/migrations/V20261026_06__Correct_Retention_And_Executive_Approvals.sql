-- Migration: V20261026_06__Correct_Retention_And_Executive_Approvals.sql
-- Description: Corrects retention periods to strict AUD-02 specifications:
--              2 years client-visible records (Invoices, Payments, AccountsReceivable),
--              5 years internal finance records (Cash advances, Liquidations, Audit logs).
--              Clears default active FM delegate so FM explicitly configures delegations.
--              Fixes filtered index UQ_Active_ExecApproval_Ref and sp_ArchiveRetentionRecords from V03.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. AccountsReceivable Retention Columns (AUD-02)
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'AccountsReceivable')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'client_retention_until')
        ALTER TABLE dbo.AccountsReceivable ADD client_retention_until DATETIME2 NULL;

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'internal_retention_until')
        ALTER TABLE dbo.AccountsReceivable ADD internal_retention_until DATETIME2 NULL;

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'is_archived')
        ALTER TABLE dbo.AccountsReceivable ADD is_archived BIT NOT NULL DEFAULT 0;

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'archived_at')
        ALTER TABLE dbo.AccountsReceivable ADD archived_at DATETIME2 NULL;
END
GO

-- 2. Correct retention timestamps per AUD-02
-- Client-visible records: 2 years client visibility, 5 years internal retention
UPDATE dbo.Invoices 
SET client_retention_until = DATEADD(year, 2, CAST(COALESCE(created_at, TRY_CAST(BillingDate AS DATETIME2), TRY_CAST(DateEncoded AS DATETIME2), SYSUTCDATETIME()) AS DATETIME2)),
    internal_retention_until = DATEADD(year, 5, CAST(COALESCE(created_at, TRY_CAST(BillingDate AS DATETIME2), TRY_CAST(DateEncoded AS DATETIME2), SYSUTCDATETIME()) AS DATETIME2));

UPDATE dbo.Payments 
SET client_retention_until = DATEADD(year, 2, CAST(COALESCE(SubmittedAt, TRY_CAST(DateRecorded AS DATETIME2), TRY_CAST(PaymentDate AS DATETIME2), SYSUTCDATETIME()) AS DATETIME2)),
    internal_retention_until = DATEADD(year, 5, CAST(COALESCE(SubmittedAt, TRY_CAST(DateRecorded AS DATETIME2), TRY_CAST(PaymentDate AS DATETIME2), SYSUTCDATETIME()) AS DATETIME2));

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'AccountsReceivable')
BEGIN
    UPDATE dbo.AccountsReceivable 
    SET client_retention_until = DATEADD(year, 2, CAST(COALESCE(created_at, SYSUTCDATETIME()) AS DATETIME2)),
        internal_retention_until = DATEADD(year, 5, CAST(COALESCE(created_at, SYSUTCDATETIME()) AS DATETIME2));
END

-- Internal finance records: 5 years internal retention; client retention is NULL (not accessible to clients)
UPDATE dbo.CashAdvances 
SET client_retention_until = NULL,
    internal_retention_until = DATEADD(year, 5, CAST(COALESCE(CreatedAt, ReleaseDate, ApprovedAt, SYSUTCDATETIME()) AS DATETIME2));

UPDATE dbo.Liquidations 
SET client_retention_until = NULL,
    internal_retention_until = DATEADD(year, 5, CAST(COALESCE(created_at, date_submitted, SYSUTCDATETIME()) AS DATETIME2));
GO

-- 3. Deactivate default seeded FM delegate so FM must explicitly configure any delegation
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates')
BEGIN
    UPDATE dbo.FinanceManagerDelegates 
    SET is_active = 0 
    WHERE delegate_user_id = 'STAFF-AFM';
END
GO

-- 4. Create UQ_Active_ExecApproval_Ref Filtered Unique Index with correct QUOTED_IDENTIFIER
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UQ_Active_ExecApproval_Ref' AND object_id = OBJECT_ID('dbo.ExecutiveApprovalRecords'))
BEGIN
    CREATE UNIQUE INDEX UQ_Active_ExecApproval_Ref 
    ON dbo.ExecutiveApprovalRecords(transaction_reference) 
    WHERE is_active = 1;
END
GO

-- 5. Fix sp_ArchiveRetentionRecords with correct AuditLogs schema columns and CAST to string
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
        details, created_at, archived_at
    )
    SELECT 
        CAST(audit_id AS NVARCHAR(100)),
        ISNULL(CAST(user_id AS NVARCHAR(100)), 'SYSTEM'),
        ISNULL(role, 'SYSTEM'),
        action,
        module,
        ISNULL(CAST(affected_record_id AS NVARCHAR(100)), 'N/A'),
        ISNULL(affected_record_type, module),
        remarks,
        ISNULL(created_at, SYSUTCDATETIME()),
        SYSUTCDATETIME()
    FROM dbo.AuditLogs
    WHERE (internal_retention_until IS NOT NULL AND internal_retention_until < SYSUTCDATETIME())
       OR (internal_retention_until IS NULL AND created_at < @CutoffDate);

    SET @RowsArchived = @@ROWCOUNT;

    DELETE FROM dbo.AuditLogs
    WHERE (internal_retention_until IS NOT NULL AND internal_retention_until < SYSUTCDATETIME())
       OR (internal_retention_until IS NULL AND created_at < @CutoffDate);

    COMMIT TRANSACTION;

    RETURN 0;
END;
GO

GRANT SELECT, INSERT ON dbo.Archive_AuditLogs TO foms_archive_principal;
GRANT SELECT, DELETE ON dbo.AuditLogs TO foms_archive_principal;
GO
