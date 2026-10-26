-- Rollback: V20261026_05_Rollback.sql
-- Description: Reverts V20261026_05 by dropping CK_Users_PasswordHash_Format

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Users_PasswordHash_Format')
BEGIN
    ALTER TABLE dbo.Users DROP CONSTRAINT CK_Users_PasswordHash_Format;
    PRINT 'Dropped constraint CK_Users_PasswordHash_Format successfully.';
END;
GO
