-- Rollback: V20261026_03_Rollback.sql
-- Description: Reverts V20261026_03

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_ArchiveRetentionRecords')
    DROP PROCEDURE dbo.sp_ArchiveRetentionRecords;

IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventDelete')
    DROP TRIGGER dbo.TR_AuditLogs_PreventDelete;

IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventUpdate')
    DROP TRIGGER dbo.TR_AuditLogs_PreventUpdate;

IF EXISTS (SELECT * FROM sys.database_principals WHERE name = 'foms_archive_principal')
    DROP USER foms_archive_principal;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLogs') AND name = 'internal_retention_until')
    ALTER TABLE dbo.AuditLogs DROP COLUMN internal_retention_until;

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Archive_AuditLogs')
    DROP TABLE dbo.Archive_AuditLogs;

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ExecutiveApprovalRecords')
    DROP TABLE dbo.ExecutiveApprovalRecords;
GO
