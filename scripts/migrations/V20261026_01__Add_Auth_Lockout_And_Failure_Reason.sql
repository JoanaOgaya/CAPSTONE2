-- Migration: V20261026_01__Add_Auth_Lockout_And_Failure_Reason.sql
-- Description: Adds account lockout tracking to Users and failure reason to Waybills.

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'failed_login_count')
BEGIN
    ALTER TABLE dbo.Users ADD failed_login_count INT NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'locked_at')
BEGIN
    ALTER TABLE dbo.Users ADD locked_at DATETIME2 NULL;
END;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'failure_reason')
BEGIN
    ALTER TABLE dbo.Waybills ADD failure_reason NVARCHAR(255) NULL;
END;
GO
