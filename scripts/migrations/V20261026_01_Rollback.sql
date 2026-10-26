-- Rollback: V20261026_01_Rollback.sql
-- Description: Reverts V20261026_01

DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql += 'ALTER TABLE dbo.Users DROP CONSTRAINT ' + name + ';'
FROM sys.default_constraints
WHERE parent_object_id = OBJECT_ID('dbo.Users') 
  AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Users'), 'failed_login_count', 'ColumnId');
IF @sql <> '' EXEC sp_executesql @sql;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'failed_login_count')
BEGIN
    ALTER TABLE dbo.Users DROP COLUMN failed_login_count;
END;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'locked_at')
BEGIN
    ALTER TABLE dbo.Users DROP COLUMN locked_at;
END;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'failure_reason')
BEGIN
    ALTER TABLE dbo.Waybills DROP COLUMN failure_reason;
END;
GO
