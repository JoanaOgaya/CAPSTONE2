-- Migration: V20261026_04__Add_Financial_Retention_And_FM_Delegates.sql
-- Description: Adds 2-year client-visible and 5-year internal retention columns to Invoices, Payments, CashAdvances, Liquidations (AUD-02)
--              and creates FinanceManagerDelegates table for delegated executive authorization (Q9).

-- 1. Retention Columns on Invoices
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Invoices') AND name = 'client_retention_until')
    ALTER TABLE dbo.Invoices ADD client_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Invoices') AND name = 'internal_retention_until')
    ALTER TABLE dbo.Invoices ADD internal_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Invoices') AND name = 'is_archived')
    ALTER TABLE dbo.Invoices ADD is_archived BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Invoices') AND name = 'archived_at')
    ALTER TABLE dbo.Invoices ADD archived_at DATETIME2 NULL;

-- 2. Retention Columns on Payments
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'client_retention_until')
    ALTER TABLE dbo.Payments ADD client_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'internal_retention_until')
    ALTER TABLE dbo.Payments ADD internal_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'is_archived')
    ALTER TABLE dbo.Payments ADD is_archived BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'archived_at')
    ALTER TABLE dbo.Payments ADD archived_at DATETIME2 NULL;

-- 3. Retention Columns on CashAdvances
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CashAdvances') AND name = 'client_retention_until')
    ALTER TABLE dbo.CashAdvances ADD client_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CashAdvances') AND name = 'internal_retention_until')
    ALTER TABLE dbo.CashAdvances ADD internal_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CashAdvances') AND name = 'is_archived')
    ALTER TABLE dbo.CashAdvances ADD is_archived BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CashAdvances') AND name = 'archived_at')
    ALTER TABLE dbo.CashAdvances ADD archived_at DATETIME2 NULL;

-- 4. Retention Columns on Liquidations
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Liquidations') AND name = 'client_retention_until')
    ALTER TABLE dbo.Liquidations ADD client_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Liquidations') AND name = 'internal_retention_until')
    ALTER TABLE dbo.Liquidations ADD internal_retention_until DATETIME2 NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Liquidations') AND name = 'is_archived')
    ALTER TABLE dbo.Liquidations ADD is_archived BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Liquidations') AND name = 'archived_at')
    ALTER TABLE dbo.Liquidations ADD archived_at DATETIME2 NULL;
GO

-- Backfill retention dates based on record creation/event timestamps
UPDATE dbo.Invoices 
SET client_retention_until = DATEADD(year, 2, ISNULL(created_at, SYSUTCDATETIME())),
    internal_retention_until = DATEADD(year, 5, ISNULL(created_at, SYSUTCDATETIME()))
WHERE client_retention_until IS NULL;

UPDATE dbo.Payments 
SET client_retention_until = DATEADD(year, 2, SYSUTCDATETIME()),
    internal_retention_until = DATEADD(year, 5, SYSUTCDATETIME())
WHERE client_retention_until IS NULL;

UPDATE dbo.CashAdvances 
SET client_retention_until = DATEADD(year, 2, ISNULL(CreatedAt, SYSUTCDATETIME())),
    internal_retention_until = DATEADD(year, 5, ISNULL(CreatedAt, SYSUTCDATETIME()))
WHERE client_retention_until IS NULL;

UPDATE dbo.Liquidations 
SET client_retention_until = DATEADD(year, 2, ISNULL(created_at, SYSUTCDATETIME())),
    internal_retention_until = DATEADD(year, 5, ISNULL(created_at, SYSUTCDATETIME()))
WHERE client_retention_until IS NULL;
GO

-- 5. FinanceManagerDelegates Table (Q9: Configurable Delegate for Financial Manager)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates')
BEGIN
    CREATE TABLE FinanceManagerDelegates (
        delegate_id INT IDENTITY(1,1) PRIMARY KEY,
        delegate_user_id NVARCHAR(100) NOT NULL,
        delegate_name NVARCHAR(150) NOT NULL,
        assigned_by NVARCHAR(100) NOT NULL,
        valid_from DATETIME2 NOT NULL,
        valid_until DATETIME2 NOT NULL,
        reason NVARCHAR(500) NOT NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FM_Delegates_UserActive ON FinanceManagerDelegates(delegate_user_id, is_active, valid_from, valid_until);
END;
GO

-- Seed initial delegation for Assistant FM (Arthur Assistant / STAFF-AFM)
IF NOT EXISTS (SELECT 1 FROM FinanceManagerDelegates WHERE delegate_user_id = 'STAFF-AFM')
BEGIN
    INSERT INTO FinanceManagerDelegates (
        delegate_user_id, delegate_name, assigned_by, valid_from, valid_until, reason, is_active
    ) VALUES (
        'STAFF-AFM', 'Arthur Assistant', 'STAFF-001 (Crystalyn Fajardo)',
        SYSUTCDATETIME(), DATEADD(year, 1, SYSUTCDATETIME()),
        'Authorized delegate for operational financial reviews and executive approval endorsements during leave.', 1
    );
END;
GO
