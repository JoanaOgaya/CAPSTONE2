/*
  Liquidation integrity upgrade for the EF SQL Server schema.
  Assumption: dbo.CashAdvances.AdvanceId is the canonical NVARCHAR identifier,
  as mapped by CashAdvance.AdvanceId in FomsMssqlEntities.cs. The legacy init.sql
  in this repository instead declares cash_advance_id; that bootstrap schema is
  incompatible with the EF model and must be reconciled before this migration.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Liquidations', 'cash_advance_reference') IS NULL
    ALTER TABLE dbo.Liquidations ADD cash_advance_reference NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.LiquidationLineItems', 'supporting_document_path') IS NULL
    ALTER TABLE dbo.LiquidationLineItems ADD supporting_document_path NVARCHAR(1000) NULL;

/* Backfill only unambiguous legacy numeric references. Unmatched rows stop deployment. */
UPDATE l
SET cash_advance_reference = ca.AdvanceId
FROM dbo.Liquidations AS l
JOIN dbo.CashAdvances AS ca
  ON ca.AdvanceId = CONVERT(NVARCHAR(100), l.cash_advance_id)
  OR ca.AdvanceId = CONCAT('CA-', CONVERT(NVARCHAR(100), l.cash_advance_id))
WHERE l.cash_advance_reference IS NULL;

IF EXISTS (SELECT 1 FROM dbo.Liquidations WHERE cash_advance_reference IS NULL)
    THROW 51000, 'Cannot enforce liquidation source FK: existing reports have no resolvable cash advance.', 1;

/* Existing DBs with multiple reports for one source need manual review before enforcing one-to-one. */
IF EXISTS (SELECT cash_advance_reference FROM dbo.Liquidations GROUP BY cash_advance_reference HAVING COUNT_BIG(*) > 1)
    THROW 51001, 'Cannot enforce one report per cash advance: duplicate legacy liquidations exist.', 1;

ALTER TABLE dbo.Liquidations ALTER COLUMN cash_advance_reference NVARCHAR(100) NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Liquidations_CashAdvances_Reference')
    ALTER TABLE dbo.Liquidations ADD CONSTRAINT FK_Liquidations_CashAdvances_Reference
        FOREIGN KEY (cash_advance_reference) REFERENCES dbo.CashAdvances(AdvanceId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Liquidations_CashAdvanceReference' AND object_id = OBJECT_ID('dbo.Liquidations'))
    CREATE UNIQUE INDEX UX_Liquidations_CashAdvanceReference ON dbo.Liquidations(cash_advance_reference);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Liquidations_Status_DueDate' AND object_id = OBJECT_ID('dbo.Liquidations'))
    CREATE INDEX IX_Liquidations_Status_DueDate ON dbo.Liquidations(status, DueDate) INCLUDE (submitted_by_employee_id, total_amount, leftover_cash, reimbursement_amount);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LiquidationLineItems_LiquidationId' AND object_id = OBJECT_ID('dbo.LiquidationLineItems'))
    CREATE INDEX IX_LiquidationLineItems_LiquidationId ON dbo.LiquidationLineItems(liquidation_id);

IF OBJECT_ID('dbo.LiquidationAuditEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LiquidationAuditEvents (
        audit_event_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        liquidation_id INT NOT NULL,
        action NVARCHAR(40) NOT NULL,
        user_id NVARCHAR(100) NOT NULL,
        occurred_at DATETIME2 NOT NULL CONSTRAINT DF_LiquidationAuditEvents_OccurredAt DEFAULT SYSUTCDATETIME(),
        old_value NVARCHAR(MAX) NULL,
        new_value NVARCHAR(MAX) NULL,
        CONSTRAINT FK_LiquidationAuditEvents_Liquidations FOREIGN KEY (liquidation_id) REFERENCES dbo.Liquidations(liquidation_id)
    );
    CREATE INDEX IX_LiquidationAuditEvents_LiquidationId_OccurredAt ON dbo.LiquidationAuditEvents(liquidation_id, occurred_at);
END;

COMMIT TRANSACTION;
