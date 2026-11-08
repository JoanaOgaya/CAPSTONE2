-- ==============================================================================
-- FOMS Pre-Deployment AR Accrual & Document Status Reconciliation Check (Read-Only)
-- Description:
--   Verifies canonical DocumentStatus and PaymentStatus for all Approved/Received
--   invoices in FOMS_DB, compares them with pre-migration backup table values,
--   and counts associated accrual journal entries to flag discrepancies.
-- ==============================================================================

SET NOCOUNT ON;

DECLARE @BackupTable NVARCHAR(128);

SELECT TOP 1 @BackupTable = name
FROM sys.tables
WHERE name LIKE 'Invoices_Backup_%'
ORDER BY create_date DESC;

IF @BackupTable IS NULL
BEGIN
    PRINT 'No pre-migration Invoices_Backup table found. Running current status audit only.';
    SELECT 
        i.Id AS [InvoiceId],
        i.InvoiceNo,
        i.DocumentStatus,
        i.PaymentStatus,
        i.TotalAmount,
        i.Balance,
        i.AccrualRevenueRecognized,
        i.AccrualJournalEntryId,
        (SELECT COUNT(*) FROM dbo.JournalEntries je WHERE je.EntryNumber = 'JE-ACCRUAL-' + i.InvoiceNo OR je.Id = i.AccrualJournalEntryId) AS [JournalEntryCount]
    FROM dbo.Invoices i
    WHERE i.DocumentStatus IN ('Approved', 'Received')
    ORDER BY i.InvoiceNo;
END
ELSE
BEGIN
    DECLARE @Sql NVARCHAR(MAX) = N'
    SELECT 
        i.Id AS [InvoiceId],
        i.InvoiceNo,
        i.DocumentStatus AS [CurrentDocStatus],
        i.PaymentStatus AS [CurrentPaymentStatus],
        b.invoice_status AS [PreMigration_InvoiceStatus],
        b.PaymentValidationStatus AS [PreMigration_ValidationStatus],
        b.PaymentStatus AS [PreMigration_PaymentStatus],
        i.TotalAmount,
        i.Balance,
        i.AccrualRevenueRecognized,
        i.AccrualJournalEntryId,
        (SELECT COUNT(*) FROM dbo.JournalEntries je WHERE je.EntryNumber = ''JE-ACCRUAL-'' + i.InvoiceNo OR je.Id = i.AccrualJournalEntryId) AS [JournalEntryCount]
    FROM dbo.Invoices i
    LEFT JOIN dbo.' + QUOTENAME(@BackupTable) + N' b ON i.Id = b.Id
    WHERE i.DocumentStatus IN (''Approved'', ''Received'')
    ORDER BY i.InvoiceNo;';

    EXEC sp_executesql @Sql;
END;
