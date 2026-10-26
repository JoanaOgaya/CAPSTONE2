-- Rollback: V20261026_04_Rollback.sql
-- Description: Reverts V20261026_04

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates')
    DROP TABLE dbo.FinanceManagerDelegates;

-- Helper macro to drop default constraints before column drop
DECLARE @table NVARCHAR(100);
DECLARE @col NVARCHAR(100) = 'is_archived';
DECLARE @sql NVARCHAR(MAX);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR 
    SELECT 'Liquidations' UNION ALL SELECT 'CashAdvances' UNION ALL SELECT 'Payments' UNION ALL SELECT 'Invoices';
OPEN cur;
FETCH NEXT FROM cur INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = '';
    SELECT @sql += 'ALTER TABLE dbo.' + @table + ' DROP CONSTRAINT ' + name + ';'
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.' + @table) 
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.' + @table), @col, 'ColumnId');
    IF @sql <> '' EXEC sp_executesql @sql;

    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.' + @table) AND name = 'client_retention_until')
    BEGIN
        SET @sql = 'ALTER TABLE dbo.' + @table + ' DROP COLUMN client_retention_until, internal_retention_until, is_archived, archived_at;';
        EXEC sp_executesql @sql;
    END;

    FETCH NEXT FROM cur INTO @table;
END;
CLOSE cur;
DEALLOCATE cur;
GO
