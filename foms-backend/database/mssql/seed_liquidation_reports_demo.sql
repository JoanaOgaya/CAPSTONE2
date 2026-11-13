/*
  Idempotent demo seed for the Liquidation Reports screen.
  Run after the SQL Server EF schema and liquidation integrity migration are applied.
  All records use DEMO identifiers and placeholder document paths; they are not
  financial transactions or official receipts.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Liquidations', 'cash_advance_reference') IS NULL
    THROW 51100, 'Apply the liquidation integrity migration before running this seed.', 1;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM dbo.CashAdvances WHERE AdvanceId = 'CA-DEMO-LIQ-001')
    INSERT dbo.CashAdvances
        (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy,
         SubmittedByEmployeeId, Status, Notes, ApprovedBy, ApprovedAt, DisbursedBy,
         DisbursedAt, CreatedAt, DepartmentCode, Category, FiscalYear, Month, LocationType,
         DueDate, RequiresCeoApproval)
    VALUES
        ('CA-DEMO-LIQ-001', 'Demo Courier - Ana Santos', 'DEMO-TRIP-001', 1000.00,
         DATEADD(DAY, -3, @Now), 'DEMO-FINANCE', 'EMP-DEMO-101', 'Released', 'DEMO SEED',
         'DEMO-FINANCE', DATEADD(DAY, -4, @Now), 'DEMO-CASHIER', DATEADD(DAY, -3, @Now),
         DATEADD(DAY, -4, @Now), 'DEP-OPS', 'Fuel', YEAR(@Now), MONTH(@Now), 'NCR',
         DATEADD(DAY, 4, @Now), 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashAdvances WHERE AdvanceId = 'CA-DEMO-LIQ-002')
    INSERT dbo.CashAdvances
        (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy,
         SubmittedByEmployeeId, Status, Notes, ApprovedBy, ApprovedAt, DisbursedBy,
         DisbursedAt, CreatedAt, DepartmentCode, Category, FiscalYear, Month, LocationType,
         DueDate, RequiresCeoApproval)
    VALUES
        ('CA-DEMO-LIQ-002', 'Demo Courier - Marco Reyes', 'DEMO-TRIP-002', 1200.00,
         DATEADD(DAY, -7, @Now), 'DEMO-FINANCE', 'EMP-DEMO-102', 'Released', 'DEMO SEED',
         'DEMO-FINANCE', DATEADD(DAY, -8, @Now), 'DEMO-CASHIER', DATEADD(DAY, -7, @Now),
         DATEADD(DAY, -8, @Now), 'DEP-OPS', 'Tolls', YEAR(@Now), MONTH(@Now), 'NCR',
         DATEADD(DAY, 1, @Now), 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashAdvances WHERE AdvanceId = 'CA-DEMO-LIQ-003')
    INSERT dbo.CashAdvances
        (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy,
         SubmittedByEmployeeId, Status, Notes, ApprovedBy, ApprovedAt, DisbursedBy,
         DisbursedAt, CreatedAt, DepartmentCode, Category, FiscalYear, Month, LocationType,
         DueDate, RequiresCeoApproval)
    VALUES
        ('CA-DEMO-LIQ-003', 'Demo Courier - Lea Cruz', 'DEMO-TRIP-003', 1250.00,
         DATEADD(DAY, -12, @Now), 'DEMO-FINANCE', 'EMP-DEMO-103', 'Released', 'DEMO SEED',
         'DEMO-FINANCE', DATEADD(DAY, -13, @Now), 'DEMO-CASHIER', DATEADD(DAY, -12, @Now),
         DATEADD(DAY, -13, @Now), 'DEP-OPS', 'Repairs and Maintenance', YEAR(@Now), MONTH(@Now), 'NCR',
         DATEADD(DAY, -5, @Now), 0);

DECLARE @Seed TABLE (ReferenceNumber NVARCHAR(100), AdvanceId NVARCHAR(100), EmployeeId NVARCHAR(100),
                     EmployeeName NVARCHAR(200), Total DECIMAL(18,2), Returned DECIMAL(18,2),
                     Reimbursement DECIMAL(18,2), Status NVARCHAR(30), ExpenseDate DATETIME2,
                     Category NVARCHAR(100), Description NVARCHAR(255), ReceiptNumber NVARCHAR(100),
                     DocumentPath NVARCHAR(1000));

INSERT @Seed VALUES
('LIQ-DEMO-001', 'CA-DEMO-LIQ-001', 'EMP-DEMO-101', 'Demo Courier - Ana Santos', 850.00, 150.00, 0.00, 'Submitted', DATEADD(DAY,-2,@Now), 'Fuel', 'DEMO fuel expense', 'OR-DEMO-001', '/demo-receipts/OR-DEMO-001.pdf'),
('LIQ-DEMO-002', 'CA-DEMO-LIQ-002', 'EMP-DEMO-102', 'Demo Courier - Marco Reyes', 1200.00, 0.00, 0.00, 'Under Review', DATEADD(DAY,-5,@Now), 'Tolls', 'DEMO toll expense', 'OR-DEMO-002', '/demo-receipts/OR-DEMO-002.pdf'),
('LIQ-DEMO-003', 'CA-DEMO-LIQ-003', 'EMP-DEMO-103', 'Demo Courier - Lea Cruz', 1300.00, 0.00, 50.00, 'Draft', DATEADD(DAY,-9,@Now), 'Repairs and Maintenance', 'DEMO vehicle repair expense', 'OR-DEMO-003', '/demo-receipts/OR-DEMO-003.pdf');

DECLARE @Reference NVARCHAR(100), @Advance NVARCHAR(100), @Employee NVARCHAR(100), @Name NVARCHAR(200),
        @Total DECIMAL(18,2), @Returned DECIMAL(18,2), @Reimbursement DECIMAL(18,2), @Status NVARCHAR(30),
        @ExpenseDate DATETIME2, @Category NVARCHAR(100), @Description NVARCHAR(255), @Receipt NVARCHAR(100), @Path NVARCHAR(1000),
        @LiqId INT;

DECLARE seed_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ReferenceNumber, AdvanceId, EmployeeId, EmployeeName, Total, Returned, Reimbursement,
           Status, ExpenseDate, Category, Description, ReceiptNumber, DocumentPath FROM @Seed;
OPEN seed_cursor;
FETCH NEXT FROM seed_cursor INTO @Reference, @Advance, @Employee, @Name, @Total, @Returned, @Reimbursement,
    @Status, @ExpenseDate, @Category, @Description, @Receipt, @Path;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Liquidations WHERE reference_number = @Reference)
    BEGIN
        INSERT dbo.Liquidations
            (reference_number, cash_advance_reference, submitted_by_employee_id, submitted_by_name,
             total_amount, leftover_cash, reimbursement_amount, status, date_submitted, DueDate,
             is_deleted, created_at, DepartmentCode, Category, FiscalYear, Month, Remarks)
        SELECT @Reference, @Advance, @Employee, @Name, @Total, @Returned, @Reimbursement, @Status,
               @Now, ca.DueDate, 0, @Now, ca.DepartmentCode, @Category, ca.FiscalYear, ca.Month, 'DEMO SEED DATA'
        FROM dbo.CashAdvances ca WHERE ca.AdvanceId = @Advance;

        SET @LiqId = CONVERT(INT, SCOPE_IDENTITY());
        INSERT dbo.LiquidationLineItems
            (liquidation_id, category, description, amount, receipt_attached, ReceiptDate,
             ReceiptNumber, supporting_document_path, created_at)
        VALUES (@LiqId, @Category, @Description, @Total, 1, @ExpenseDate, @Receipt, @Path, @Now);

        IF OBJECT_ID('dbo.LiquidationDocuments', 'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.LiquidationDocuments WHERE liquidation_id = @LiqId AND file_path = @Path)
            INSERT dbo.LiquidationDocuments (liquidation_id, document_type, file_path, uploaded_at)
            VALUES (@LiqId, 'Demo receipt placeholder', @Path, @Now);
    END;

    FETCH NEXT FROM seed_cursor INTO @Reference, @Advance, @Employee, @Name, @Total, @Returned, @Reimbursement,
        @Status, @ExpenseDate, @Category, @Description, @Receipt, @Path;
END;
CLOSE seed_cursor;
DEALLOCATE seed_cursor;

COMMIT TRANSACTION;
