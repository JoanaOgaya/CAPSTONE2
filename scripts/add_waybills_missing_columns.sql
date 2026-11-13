USE FOMS_DB;
GO

-- Add missing columns to dbo.Waybills for EF Core mapping
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'recipient_name')
    ALTER TABLE dbo.Waybills ADD recipient_name NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'delivered_at')
    ALTER TABLE dbo.Waybills ADD delivered_at DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'pod_reference')
    ALTER TABLE dbo.Waybills ADD pod_reference NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'driver_name')
    ALTER TABLE dbo.Waybills ADD driver_name NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'is_manual_entry')
    ALTER TABLE dbo.Waybills ADD is_manual_entry BIT NOT NULL CONSTRAINT DF_Waybills_is_manual_entry DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'is_exception')
    ALTER TABLE dbo.Waybills ADD is_exception BIT NOT NULL CONSTRAINT DF_Waybills_is_exception DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'exception_reason')
    ALTER TABLE dbo.Waybills ADD exception_reason NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'is_duplicate_flagged')
    ALTER TABLE dbo.Waybills ADD is_duplicate_flagged BIT NOT NULL CONSTRAINT DF_Waybills_is_duplicate_flagged DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'client_signed')
    ALTER TABLE dbo.Waybills ADD client_signed BIT NOT NULL CONSTRAINT DF_Waybills_client_signed DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'accounting_copy_matched')
    ALTER TABLE dbo.Waybills ADD accounting_copy_matched BIT NOT NULL CONSTRAINT DF_Waybills_accounting_copy_matched DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'accounting_checked_at')
    ALTER TABLE dbo.Waybills ADD accounting_checked_at DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'accounting_checked_by')
    ALTER TABLE dbo.Waybills ADD accounting_checked_by NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'HasCtc')
    ALTER TABLE dbo.Waybills ADD HasCtc BIT NOT NULL CONSTRAINT DF_Waybills_HasCtc DEFAULT (0);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'ctc_document_url')
    ALTER TABLE dbo.Waybills ADD ctc_document_url NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'ctc_reason')
    ALTER TABLE dbo.Waybills ADD ctc_reason NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'ctc_approved_by')
    ALTER TABLE dbo.Waybills ADD ctc_approved_by NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'ctc_approved_at')
    ALTER TABLE dbo.Waybills ADD ctc_approved_at DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'hold_reason')
    ALTER TABLE dbo.Waybills ADD hold_reason NVARCHAR(1000) NULL;

GO

-- Set validated waybills to have both client_signed and accounting_copy_matched = 1
UPDATE dbo.Waybills
SET client_signed = 1,
    accounting_copy_matched = 1,
    delivered_at = ISNULL(delivered_at, created_at)
WHERE pod_status = 'Validated' OR pod_status = 'CTC' OR HasCtc = 1;

PRINT 'Successfully added missing columns to dbo.Waybills and synchronized validated flags.';
GO
