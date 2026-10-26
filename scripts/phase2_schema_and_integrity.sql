USE FOMS_DB;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Departments Table (Placeholder seed per Condition #3 & #11)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Departments')
BEGIN
    CREATE TABLE Departments (
        department_id INT IDENTITY(1,1) PRIMARY KEY,
        department_code NVARCHAR(50) NOT NULL UNIQUE,
        department_name NVARCHAR(100) NOT NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- Placeholder seeds for Departments (referenced by ID only)
IF NOT EXISTS (SELECT 1 FROM Departments WHERE department_id = 1)
BEGIN
    SET IDENTITY_INSERT Departments ON;
    INSERT INTO Departments (department_id, department_code, department_name, is_active)
    VALUES 
        (1, 'DEP-FIN', 'Finance', 1),
        (2, 'DEP-OPS', 'Operations', 1),
        (3, 'DEP-FLT', 'Fleet & Logistics', 1),
        (4, 'DEP-HR',  'Human Resources', 1),
        (5, 'DEP-IT',  'Information Technology', 1),
        (6, 'DEP-EXEC','Executive Office', 1);
    SET IDENTITY_INSERT Departments OFF;
END
GO

-- 2. UserClientMappings Table (Condition #5 & #8)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserClientMappings')
BEGIN
    CREATE TABLE UserClientMappings (
        mapping_id INT IDENTITY(1,1) PRIMARY KEY,
        user_id NVARCHAR(100) NOT NULL,
        client_id NVARCHAR(100) NOT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_UserClientMappings UNIQUE (user_id, client_id)
    );
    CREATE INDEX IX_UserClientMappings_UserId ON UserClientMappings(user_id);
    CREATE INDEX IX_UserClientMappings_ClientId ON UserClientMappings(client_id);
END
GO

-- Seed sample Client mapping for test client account
IF NOT EXISTS (SELECT 1 FROM UserClientMappings WHERE user_id = 'USER-CLI-001')
BEGIN
    INSERT INTO UserClientMappings (user_id, client_id)
    VALUES ('USER-CLI-001', 'CLI-001');
END
GO

-- 3. RevokedTokens Table (Token revocation list per Condition #6)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RevokedTokens')
BEGIN
    CREATE TABLE RevokedTokens (
        token_id INT IDENTITY(1,1) PRIMARY KEY,
        token_hash NVARCHAR(256) NOT NULL UNIQUE,
        revoked_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        expires_at DATETIME2 NOT NULL
    );
    CREATE INDEX IX_RevokedTokens_Hash ON RevokedTokens(token_hash);
END
GO

-- 4. ExecutiveApprovalRecords Table (Condition #4)
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

    -- Unique filtered index: exactly one active record per transaction reference
    CREATE UNIQUE INDEX UQ_Active_ExecApproval_Ref 
    ON ExecutiveApprovalRecords(transaction_reference) 
    WHERE is_active = 1;

    CREATE INDEX IX_ExecApproval_TypeRef ON ExecutiveApprovalRecords(transaction_type, transaction_reference);
END
GO

-- 5. Archive Tables (Condition #2: dedicated append-only archive tables)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Archive_AuditLogs')
BEGIN
    CREATE TABLE Archive_AuditLogs (
        archive_id INT IDENTITY(1,1) PRIMARY KEY,
        audit_id NVARCHAR(100) NOT NULL,
        user_id NVARCHAR(100) NOT NULL,
        role NVARCHAR(50) NOT NULL,
        action NVARCHAR(100) NOT NULL,
        module NVARCHAR(100) NOT NULL,
        affected_record_type NVARCHAR(100) NOT NULL,
        affected_record_id NVARCHAR(100) NOT NULL,
        before_value NVARCHAR(MAX) NULL,
        after_value NVARCHAR(MAX) NULL,
        remarks NVARCHAR(MAX) NULL,
        original_created_at DATETIME2 NOT NULL,
        archived_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 6. Add retention columns to AuditLogs if not present
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLogs') AND name = 'internal_retention_until')
BEGIN
    ALTER TABLE dbo.AuditLogs ADD internal_retention_until DATETIME2 NOT NULL DEFAULT DATEADD(year, 5, SYSUTCDATETIME());
END
GO

-- 7. Triggers blocking UPDATE and DELETE on AuditLogs (Condition #2)
-- Revokes arbitrary tampering; controlled archiving runs under dedicated principal or admin
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventUpdate')
    DROP TRIGGER TR_AuditLogs_PreventUpdate;
GO

CREATE TRIGGER TR_AuditLogs_PreventUpdate
ON dbo.AuditLogs
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF SUSER_NAME() NOT IN ('foms_archive_principal', 'sa')
    BEGIN
        RAISERROR ('AUD-01 VIOLATION: Updates on AuditLogs are strictly prohibited. Audit trail is immutable and append-only.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventDelete')
    DROP TRIGGER TR_AuditLogs_PreventDelete;
GO

CREATE TRIGGER TR_AuditLogs_PreventDelete
ON dbo.AuditLogs
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF SUSER_NAME() NOT IN ('foms_archive_principal', 'sa')
    BEGIN
        RAISERROR ('AUD-01 VIOLATION: Deletions on AuditLogs are strictly prohibited. Audit trail is immutable and append-only.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

-- 8. Stored Procedure for Controlled Archiving (Condition #2)
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_ArchiveRetentionRecords')
    DROP PROCEDURE sp_ArchiveRetentionRecords;
GO

CREATE PROCEDURE sp_ArchiveRetentionRecords
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        -- Move AuditLogs older than 5-year retention period into Archive_AuditLogs
        INSERT INTO Archive_AuditLogs (
            audit_id, user_id, role, action, module, 
            affected_record_type, affected_record_id, before_value, after_value, remarks, original_created_at
        )
        SELECT 
            audit_id, user_id, role, action, module, 
            affected_record_type, affected_record_id, before_value, after_value, remarks, created_at
        FROM dbo.AuditLogs
        WHERE internal_retention_until < SYSUTCDATETIME();

        -- Note: DELETE is only permitted when running under dedicated archive principal or sa
        DELETE FROM dbo.AuditLogs
        WHERE internal_retention_until < SYSUTCDATETIME();

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- 9. Ensure exactly 5 canonical roles and ROLE-005 Coordinator as non-login (Condition #3 & #4)
IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 1)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-001', 'Financial Manager', 'Financial Manager', 1, 'Financial Manager', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 2)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-002', 'Head Accountant', 'Head Accountant', 2, 'Head Accountant', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 3)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-003', 'Accountant', 'Accountant', 3, 'Accountant', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 4)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-004', 'Assistant of Financial Manager', 'Assistant of Financial Manager', 4, 'Assistant of Financial Manager', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 5)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-005', 'Billing & Collection Coordinator', 'STARS Coordinator (Non-login)', 5, 'Coordinator', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_id = 6)
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-006', 'Client', 'External Client User', 6, 'Client', SYSUTCDATETIME());
GO

PRINT 'Phase 2 Database Schema, Integrity Constraints, Triggers and Archiving applied successfully.';
GO
