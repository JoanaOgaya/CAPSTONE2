-- Rollback: V20261026_06_Rollback.sql
-- Description: Reverts V20261026_06 corrective migration

DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql += 'ALTER TABLE dbo.AccountsReceivable DROP CONSTRAINT ' + name + ';'
FROM sys.default_constraints
WHERE parent_object_id = OBJECT_ID('dbo.AccountsReceivable') 
  AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AccountsReceivable'), 'is_archived', 'ColumnId');
IF @sql <> '' EXEC sp_executesql @sql;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'client_retention_until')
    ALTER TABLE dbo.AccountsReceivable DROP COLUMN client_retention_until, internal_retention_until, is_archived, archived_at;

-- Re-enable initial demo delegate if needed
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates')
BEGIN
    UPDATE dbo.FinanceManagerDelegates 
    SET is_active = 1 
    WHERE delegate_user_id = 'STAFF-AFM';
END
GO
