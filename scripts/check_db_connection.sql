SELECT DB_NAME() AS CurrentDatabase, @@SERVERNAME AS ServerInstance;

SELECT t.name AS TableName, SUM(st.row_count) AS [RowCount]
FROM sys.tables t
JOIN sys.dm_db_partition_stats st ON st.object_id = t.object_id AND st.index_id < 2
WHERE t.name IN ('Users', 'Clients', 'Waybills', 'Invoices', 'AccountsReceivable', 'Payments', 'AIReviewHistory', 'CashOutflowApprovalRequests', 'Roles')
GROUP BY t.name
ORDER BY t.name;
