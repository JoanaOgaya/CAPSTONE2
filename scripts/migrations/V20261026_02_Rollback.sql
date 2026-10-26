-- Rollback: V20261026_02_Rollback.sql
-- Description: Reverts V20261026_02

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'UserClientMappings')
BEGIN
    DROP TABLE dbo.UserClientMappings;
END;

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'RevokedTokens')
BEGIN
    DROP TABLE dbo.RevokedTokens;
END;
GO
