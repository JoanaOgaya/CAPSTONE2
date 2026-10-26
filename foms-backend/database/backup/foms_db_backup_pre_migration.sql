-- ============================================================================
-- FOMS (Finance Operations Management System) - MSSQL Official Schema
-- Database: FOMS_DB (Official Source of Truth)
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'FOMS_DB')
BEGIN
    CREATE DATABASE FOMS_DB;
END
GO

USE FOMS_DB;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Clients Table (Master source from DMS, read-only basic client info in FOMS)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Clients')
BEGIN
    CREATE TABLE Clients (
        client_id NVARCHAR(100) PRIMARY KEY,
        dms_client_id NVARCHAR(100) NOT NULL UNIQUE,
        client_code NVARCHAR(50) NOT NULL,
        company_name NVARCHAR(200) NOT NULL,
        address NVARCHAR(500) NOT NULL,
        contact_person NVARCHAR(150) NOT NULL,
        contact_number NVARCHAR(50) NOT NULL,
        email NVARCHAR(150) NOT NULL,
        dms_status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        is_active BIT NOT NULL DEFAULT 1,
        synced_from_dms BIT NOT NULL DEFAULT 1,
        last_synced_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Clients_ClientCode ON Clients(client_code);
    CREATE INDEX IX_Clients_IsActive ON Clients(is_active);
END
GO

-- 2. ClientFinanceSettings Table (FOMS-owned financial settings for clients)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientFinanceSettings')
BEGIN
    CREATE TABLE ClientFinanceSettings (
        finance_setting_id NVARCHAR(100) PRIMARY KEY,
        client_id NVARCHAR(100) NOT NULL UNIQUE,
        billing_terms NVARCHAR(100) NOT NULL DEFAULT '30 Days',
        payment_terms NVARCHAR(100) NOT NULL DEFAULT 'Net 30',
        credit_terms_days INT NOT NULL DEFAULT 30,
        tax_type NVARCHAR(50) NOT NULL DEFAULT 'VAT Standard (12%)',
        withholding_tax_applicable BIT NOT NULL DEFAULT 0,
        default_rate_group NVARCHAR(100) NOT NULL DEFAULT 'STANDARD',
        oda_rate_applicable BIT NOT NULL DEFAULT 1,
        finance_status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        created_by NVARCHAR(100) NOT NULL DEFAULT 'SYSTEM',
        updated_by NVARCHAR(100) NOT NULL DEFAULT 'SYSTEM',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ClientFinanceSettings_Clients FOREIGN KEY (client_id) REFERENCES Clients(client_id) ON DELETE CASCADE
    );
END
GO

-- 3. ClientRates Table (Delivery rates, minimum rates, ODA rates, surcharges)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientRates')
BEGIN
    CREATE TABLE ClientRates (
        rate_id NVARCHAR(100) PRIMARY KEY,
        client_id NVARCHAR(100) NOT NULL,
        service_type NVARCHAR(100) NOT NULL,
        delivery_area NVARCHAR(100) NOT NULL,
        delivery_type NVARCHAR(100) NOT NULL DEFAULT 'Standard Delivery',
        minimum_kg DECIMAL(18,2) NOT NULL DEFAULT 5.00,
        minimum_rate DECIMAL(18,2) NOT NULL DEFAULT 100.00,
        excess_rate_per_kg DECIMAL(18,2) NOT NULL DEFAULT 25.00,
        oda_rate DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        fuel_surcharge_rate DECIMAL(18,4) NOT NULL DEFAULT 0.1500,
        valuation_rate DECIMAL(18,4) NOT NULL DEFAULT 0.0100,
        effective_from DATE NOT NULL,
        effective_to DATE NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ClientRates_Clients FOREIGN KEY (client_id) REFERENCES Clients(client_id)
    );
    CREATE INDEX IX_ClientRates_Area ON ClientRates(client_id, delivery_area, service_type, is_active);
END
GO

-- 4. Invoices Table (Official billing master records)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Invoices')
BEGIN
    CREATE TABLE Invoices (
        invoice_id NVARCHAR(100) PRIMARY KEY,
        invoice_no NVARCHAR(100) NOT NULL UNIQUE,
        client_id NVARCHAR(100) NOT NULL,
        client_name NVARCHAR(200) NOT NULL,
        billing_date DATE NOT NULL,
        due_date DATE NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        amount_paid DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        balance DECIMAL(18,2) NOT NULL,
        payment_status NVARCHAR(50) NOT NULL DEFAULT 'Unpaid',
        ctc_status NVARCHAR(50) NOT NULL DEFAULT 'Pending CTC',
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        notes NVARCHAR(MAX) NULL,
        created_by NVARCHAR(100) NOT NULL DEFAULT 'SYSTEM',
        approved_by NVARCHAR(100) NULL,
        approved_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Invoices_Clients FOREIGN KEY (client_id) REFERENCES Clients(client_id)
    );
    CREATE INDEX IX_Invoices_Client ON Invoices(client_id);
    CREATE INDEX IX_Invoices_Status ON Invoices(payment_status, validation_status);
END
GO

-- 5. InvoiceComputations Table (Complete auditable breakdown of freight calculation)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'InvoiceComputations')
BEGIN
    CREATE TABLE InvoiceComputations (
        computation_id NVARCHAR(100) PRIMARY KEY,
        invoice_id NVARCHAR(100) NOT NULL,
        waybill_id NVARCHAR(100) NOT NULL,
        client_id NVARCHAR(100) NOT NULL,
        rate_id NVARCHAR(100) NOT NULL,
        area NVARCHAR(100) NOT NULL,
        actual_weight DECIMAL(18,2) NOT NULL,
        volume_weight DECIMAL(18,2) NOT NULL,
        chargeable_weight DECIMAL(18,2) NOT NULL,
        minimum_kg DECIMAL(18,2) NOT NULL,
        minimum_rate DECIMAL(18,2) NOT NULL,
        excess_rate_per_kg DECIMAL(18,2) NOT NULL,
        excess_weight DECIMAL(18,2) NOT NULL,
        freight_cost DECIMAL(18,2) NOT NULL,
        declared_value DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        valuation_rate DECIMAL(18,4) NOT NULL DEFAULT 0.00,
        valuation_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        is_oda BIT NOT NULL DEFAULT 0,
        oda_rate DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        oda_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        subtotal DECIMAL(18,2) NOT NULL,
        vat_rate DECIMAL(18,4) NOT NULL DEFAULT 0.1200,
        vat_amount DECIMAL(18,2) NOT NULL,
        fuel_surcharge_rate DECIMAL(18,4) NOT NULL DEFAULT 0.1500,
        fuel_surcharge_amount DECIMAL(18,2) NOT NULL,
        grand_total DECIMAL(18,2) NOT NULL,
        computation_json NVARCHAR(MAX) NOT NULL,
        computed_by NVARCHAR(100) NOT NULL,
        computed_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_InvoiceComputations_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id)
    );
    CREATE INDEX IX_InvoiceComputations_Invoice ON InvoiceComputations(invoice_id);
END
GO

-- 6. BillingDocuments Table (Separate Validated Status vs CTC Status)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BillingDocuments')
BEGIN
    CREATE TABLE BillingDocuments (
        billing_document_id NVARCHAR(100) PRIMARY KEY,
        invoice_id NVARCHAR(100) NOT NULL,
        waybill_id NVARCHAR(100) NOT NULL,
        pod_status NVARCHAR(50) NOT NULL DEFAULT 'Delivered',
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        ctc_status NVARCHAR(50) NOT NULL DEFAULT 'Not Required',
        ctc_required BIT NOT NULL DEFAULT 0,
        validated_by NVARCHAR(100) NULL,
        validated_at DATETIME2 NULL,
        ctc_verified_by NVARCHAR(100) NULL,
        ctc_verified_at DATETIME2 NULL,
        remarks NVARCHAR(500) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BillingDocuments_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id)
    );
    CREATE INDEX IX_BillingDocs_Validation ON BillingDocuments(validation_status, ctc_status);
END
GO

-- 7. AccountsReceivable Table (Strict AR flow, zero and negative handling)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AccountsReceivable')
BEGIN
    CREATE TABLE AccountsReceivable (
        ar_id NVARCHAR(100) PRIMARY KEY,
        invoice_id NVARCHAR(100) NOT NULL UNIQUE,
        client_id NVARCHAR(100) NOT NULL,
        invoice_amount DECIMAL(18,2) NOT NULL,
        paid_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        outstanding_balance DECIMAL(18,2) NOT NULL,
        overpayment_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        ar_status NVARCHAR(50) NOT NULL DEFAULT 'Outstanding',
        due_date DATE NOT NULL,
        aging_bucket NVARCHAR(50) NOT NULL DEFAULT 'Current',
        last_payment_date DATE NULL,
        refund_status NVARCHAR(50) NOT NULL DEFAULT 'Not Required',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AccountsReceivable_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id),
        CONSTRAINT FK_AccountsReceivable_Clients FOREIGN KEY (client_id) REFERENCES Clients(client_id)
    );
    CREATE INDEX IX_AR_Status ON AccountsReceivable(ar_status, aging_bucket);
    CREATE INDEX IX_AR_Client ON AccountsReceivable(client_id);
END
GO

-- 8. Payments Table (Official Payment Records)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Payments')
BEGIN
    CREATE TABLE Payments (
        payment_id NVARCHAR(100) PRIMARY KEY,
        or_number NVARCHAR(100) NOT NULL UNIQUE,
        invoice_id NVARCHAR(100) NOT NULL,
        invoice_no NVARCHAR(100) NOT NULL,
        client_id NVARCHAR(100) NOT NULL,
        client_name NVARCHAR(200) NOT NULL,
        payment_date DATE NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        payment_method NVARCHAR(100) NOT NULL,
        reference_number NVARCHAR(150) NOT NULL,
        proof_image_url NVARCHAR(500) NULL,
        remarks NVARCHAR(500) NULL,
        payment_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Finance Validation',
        recorded_by NVARCHAR(100) NOT NULL,
        date_recorded DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
        validated_by NVARCHAR(100) NULL,
        validated_at DATETIME2 NULL,
        submitted_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Payments_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id)
    );
    CREATE INDEX IX_Payments_Ref ON Payments(reference_number);
    CREATE INDEX IX_Payments_Status ON Payments(payment_status);
END
GO

-- 9. PaymentCustomerInfo Table (PayMongo Customer Validation: separated first & last name)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PaymentCustomerInfo')
BEGIN
    CREATE TABLE PaymentCustomerInfo (
        customer_info_id NVARCHAR(100) PRIMARY KEY,
        payment_id NVARCHAR(100) NOT NULL,
        first_name NVARCHAR(100) NOT NULL,
        last_name NVARCHAR(100) NOT NULL,
        email NVARCHAR(150) NOT NULL,
        phone NVARCHAR(50) NULL,
        full_name_generated AS (first_name + ' ' + last_name) PERSISTED,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PaymentCustomerInfo_Payments FOREIGN KEY (payment_id) REFERENCES Payments(payment_id) ON DELETE CASCADE
    );
END
GO

-- 10. RefundRequests Table (Overpayments, DMS integration cases, finance approvals)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RefundRequests')
BEGIN
    CREATE TABLE RefundRequests (
        refund_id NVARCHAR(100) PRIMARY KEY,
        dms_case_id NVARCHAR(100) NULL,
        invoice_id NVARCHAR(100) NOT NULL,
        payment_id NVARCHAR(100) NOT NULL,
        ar_id NVARCHAR(100) NOT NULL,
        client_id NVARCHAR(100) NOT NULL,
        overpayment_amount DECIMAL(18,2) NOT NULL,
        refund_amount DECIMAL(18,2) NOT NULL,
        refund_reason NVARCHAR(500) NOT NULL,
        refund_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Finance Approval',
        requested_source NVARCHAR(100) NOT NULL DEFAULT 'System Overpayment Detection',
        requested_by NVARCHAR(100) NOT NULL,
        dms_validated_by NVARCHAR(100) NULL,
        finance_reviewed_by NVARCHAR(100) NULL,
        approved_by NVARCHAR(100) NULL,
        processed_by NVARCHAR(100) NULL,
        requested_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        dms_validated_at DATETIME2 NULL,
        finance_reviewed_at DATETIME2 NULL,
        approved_at DATETIME2 NULL,
        processed_at DATETIME2 NULL,
        remarks NVARCHAR(MAX) NULL,
        CONSTRAINT FK_RefundRequests_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id),
        CONSTRAINT FK_RefundRequests_Payments FOREIGN KEY (payment_id) REFERENCES Payments(payment_id),
        CONSTRAINT FK_RefundRequests_AR FOREIGN KEY (ar_id) REFERENCES AccountsReceivable(ar_id)
    );
    CREATE INDEX IX_RefundRequests_Status ON RefundRequests(refund_status);
END
GO

-- 11. CashFlowAccounts Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashFlowAccounts')
BEGIN
    CREATE TABLE CashFlowAccounts (
        account_id NVARCHAR(100) PRIMARY KEY,
        account_name NVARCHAR(150) NOT NULL,
        account_type NVARCHAR(50) NOT NULL, -- Operating, Petty Cash, Payroll, Bank
        maintaining_balance DECIMAL(18,2) NOT NULL DEFAULT 500.00,
        current_balance DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 12. CashInflows Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashInflows')
BEGIN
    CREATE TABLE CashInflows (
        inflow_id NVARCHAR(100) PRIMARY KEY,
        account_id NVARCHAR(100) NOT NULL,
        source_type NVARCHAR(100) NOT NULL, -- Client Payment, Cash Advance Return, Bank Deposit
        source_reference_id NVARCHAR(100) NULL,
        description NVARCHAR(255) NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        inflow_date DATE NOT NULL,
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        validated_by NVARCHAR(100) NULL,
        validated_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CashInflows_Accounts FOREIGN KEY (account_id) REFERENCES CashFlowAccounts(account_id)
    );
    CREATE INDEX IX_CashInflows_AccountDate ON CashInflows(account_id, inflow_date);
END
GO

-- 13. CashOutflows Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashOutflows')
BEGIN
    CREATE TABLE CashOutflows (
        outflow_id NVARCHAR(100) PRIMARY KEY,
        account_id NVARCHAR(100) NOT NULL,
        source_type NVARCHAR(100) NOT NULL, -- Expense, Liquidation, Cash Advance Release
        source_reference_id NVARCHAR(100) NULL,
        expense_category NVARCHAR(100) NOT NULL, -- Travel, Fuel, Communication, Toll, Office Supplies, etc.
        description NVARCHAR(255) NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        outflow_date DATE NOT NULL,
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        validated_by NVARCHAR(100) NULL,
        validated_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CashOutflows_Accounts FOREIGN KEY (account_id) REFERENCES CashFlowAccounts(account_id)
    );
    CREATE INDEX IX_CashOutflows_AccountDate ON CashOutflows(account_id, outflow_date);
END
GO

-- 14. CashFlowSnapshots Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashFlowSnapshots')
BEGIN
    CREATE TABLE CashFlowSnapshots (
        snapshot_id NVARCHAR(100) PRIMARY KEY,
        account_id NVARCHAR(100) NOT NULL,
        beginning_balance DECIMAL(18,2) NOT NULL,
        total_inflows DECIMAL(18,2) NOT NULL,
        total_outflows DECIMAL(18,2) NOT NULL,
        net_cash_balance DECIMAL(18,2) NOT NULL,
        maintaining_balance DECIMAL(18,2) NOT NULL,
        variance DECIMAL(18,2) NOT NULL,
        snapshot_date DATE NOT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CashFlowSnapshots_Accounts FOREIGN KEY (account_id) REFERENCES CashFlowAccounts(account_id)
    );
END
GO

-- 15. CashAdvances Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashAdvances')
BEGIN
    CREATE TABLE CashAdvances (
        cash_advance_id NVARCHAR(100) PRIMARY KEY,
        employee_id NVARCHAR(100) NOT NULL,
        request_date DATE NOT NULL,
        purpose NVARCHAR(500) NOT NULL,
        approved_amount DECIMAL(18,2) NOT NULL,
        released_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        approved_by NVARCHAR(100) NULL,
        released_by NVARCHAR(100) NULL,
        released_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 16. CashAdvanceBreakdown Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashAdvanceBreakdown')
BEGIN
    CREATE TABLE CashAdvanceBreakdown (
        breakdown_id NVARCHAR(100) PRIMARY KEY,
        cash_advance_id NVARCHAR(100) NOT NULL,
        category NVARCHAR(100) NOT NULL,
        estimated_amount DECIMAL(18,2) NOT NULL,
        remarks NVARCHAR(255) NULL,
        CONSTRAINT FK_CashAdvanceBreakdown_CA FOREIGN KEY (cash_advance_id) REFERENCES CashAdvances(cash_advance_id) ON DELETE CASCADE
    );
END
GO

-- 17. Liquidations Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Liquidations')
BEGIN
    CREATE TABLE Liquidations (
        liquidation_id NVARCHAR(100) PRIMARY KEY,
        cash_advance_id NVARCHAR(100) NOT NULL,
        employee_id NVARCHAR(100) NOT NULL,
        submission_date DATE NOT NULL,
        total_liquidated_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        returned_cash_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        reimbursement_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        variance_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Review',
        reviewed_by NVARCHAR(100) NULL,
        approved_by NVARCHAR(100) NULL,
        remarks NVARCHAR(500) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Liquidations_CA FOREIGN KEY (cash_advance_id) REFERENCES CashAdvances(cash_advance_id)
    );
END
GO

-- 18. LiquidationLineItems Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LiquidationLineItems')
BEGIN
    CREATE TABLE LiquidationLineItems (
        line_item_id NVARCHAR(100) PRIMARY KEY,
        liquidation_id NVARCHAR(100) NOT NULL,
        expense_category NVARCHAR(100) NOT NULL,
        description NVARCHAR(255) NOT NULL,
        receipt_number NVARCHAR(100) NOT NULL,
        receipt_date DATE NOT NULL,
        vendor_name NVARCHAR(150) NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        tax_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        deductible_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        supporting_document_status NVARCHAR(50) NOT NULL DEFAULT 'Verified',
        rejection_reason NVARCHAR(255) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_LiquidationLineItems_Liq FOREIGN KEY (liquidation_id) REFERENCES Liquidations(liquidation_id) ON DELETE CASCADE
    );
END
GO

-- 19. LiquidationDocuments Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LiquidationDocuments')
BEGIN
    CREATE TABLE LiquidationDocuments (
        document_id NVARCHAR(100) PRIMARY KEY,
        liquidation_id NVARCHAR(100) NOT NULL,
        document_type NVARCHAR(100) NOT NULL, -- Official Receipt, Voucher, Invoice, Check Copy
        file_name NVARCHAR(255) NOT NULL,
        file_path NVARCHAR(500) NOT NULL,
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        remarks NVARCHAR(255) NULL,
        uploaded_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_LiquidationDocuments_Liq FOREIGN KEY (liquidation_id) REFERENCES Liquidations(liquidation_id) ON DELETE CASCADE
    );
END
GO

-- 20. UnidentifiedPayments Table (Hold / For Verification bank credits)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UnidentifiedPayments')
BEGIN
    CREATE TABLE UnidentifiedPayments (
        unidentified_payment_id NVARCHAR(100) PRIMARY KEY,
        amount DECIMAL(18,2) NOT NULL,
        received_date DATE NOT NULL,
        bank_reference NVARCHAR(150) NOT NULL,
        remarks NVARCHAR(500) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Hold / For Verification',
        claimed_by_client_id NVARCHAR(100) NULL,
        matched_invoice_id NVARCHAR(100) NULL,
        verified_by NVARCHAR(100) NULL,
        verified_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_UnidentifiedPayments_Clients FOREIGN KEY (claimed_by_client_id) REFERENCES Clients(client_id),
        CONSTRAINT FK_UnidentifiedPayments_Invoices FOREIGN KEY (matched_invoice_id) REFERENCES Invoices(invoice_id)
    );
END
GO

-- 21. BillingAdjustments Table (Discounts, Credit Memos, Cargo Damage Claims)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BillingAdjustments')
BEGIN
    CREATE TABLE BillingAdjustments (
        adjustment_id NVARCHAR(100) PRIMARY KEY,
        invoice_id NVARCHAR(100) NOT NULL,
        adjustment_type NVARCHAR(50) NOT NULL, -- Discount, Credit Memo, Damage Claim, Lost Cargo Credit
        amount DECIMAL(18,2) NOT NULL,
        percentage DECIMAL(5,2) NULL,
        reason NVARCHAR(500) NOT NULL,
        approval_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        approved_by NVARCHAR(100) NULL,
        approved_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BillingAdjustments_Invoices FOREIGN KEY (invoice_id) REFERENCES Invoices(invoice_id)
    );
END
GO

-- 22. Notifications Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
BEGIN
    CREATE TABLE Notifications (
        notification_id NVARCHAR(100) PRIMARY KEY,
        recipient_user_id NVARCHAR(100) NULL,
        recipient_role NVARCHAR(50) NOT NULL,
        type NVARCHAR(50) NOT NULL,
        title NVARCHAR(200) NOT NULL,
        message NVARCHAR(1000) NOT NULL,
        related_record_type NVARCHAR(50) NOT NULL,
        related_record_id NVARCHAR(100) NOT NULL,
        is_read BIT NOT NULL DEFAULT 0,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Notifications_Recipient ON Notifications(recipient_role, is_read, created_at);
END
GO

-- 23. AuditLogs Table (Comprehensive immutable audit trail)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
BEGIN
    CREATE TABLE AuditLogs (
        audit_id NVARCHAR(100) PRIMARY KEY,
        user_id NVARCHAR(100) NOT NULL,
        role NVARCHAR(50) NOT NULL,
        action NVARCHAR(100) NOT NULL,
        module NVARCHAR(100) NOT NULL,
        affected_record_type NVARCHAR(100) NOT NULL,
        affected_record_id NVARCHAR(100) NOT NULL,
        before_value NVARCHAR(MAX) NULL,
        after_value NVARCHAR(MAX) NULL,
        remarks NVARCHAR(MAX) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_AuditLogs_ModuleRecord ON AuditLogs(module, affected_record_type, affected_record_id);
    CREATE INDEX IX_AuditLogs_CreatedAt ON AuditLogs(created_at DESC);
END
GO

-- 24. Employees Table (FOMS Staff accounts)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Employees')
BEGIN
    CREATE TABLE Employees (
        employee_id NVARCHAR(100) PRIMARY KEY,
        name NVARCHAR(150) NOT NULL,
        role NVARCHAR(50) NOT NULL,
        email NVARCHAR(150) NOT NULL UNIQUE,
        system_access NVARCHAR(100) NOT NULL DEFAULT 'Finance Operation Service',
        status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        is_active BIT NOT NULL DEFAULT 1,
        username NVARCHAR(100) NOT NULL UNIQUE,
        password_hash NVARCHAR(255) NOT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

PRINT 'MSSQL FOMS_DB tables and indexes created successfully.';
GO
-- ============================================================================
-- FOMS Database Migration - Add Missing Tables & Align with FOMS_DB
-- Safe migrations only: Does NOT drop existing tables or delete existing data
-- ============================================================================

USE FOMS_DB;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Roles table alignment
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Roles' AND COLUMN_NAME = 'role_id')
BEGIN
    ALTER TABLE Roles ADD role_id INT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Roles' AND COLUMN_NAME = 'role_name')
BEGIN
    ALTER TABLE Roles ADD role_name NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Roles' AND COLUMN_NAME = 'created_at')
BEGIN
    ALTER TABLE Roles ADD created_at DATETIME2 NULL DEFAULT SYSUTCDATETIME();
END
GO

-- Seed Roles if missing
IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_name = 'Financial Manager' OR Name = 'Financial Manager')
BEGIN
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-001', 'Financial Manager', 'Financial Operations Manager', 1, 'Financial Manager', SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE Roles SET role_id = 1, role_name = 'Financial Manager' WHERE Name = 'Financial Manager' OR role_name = 'Financial Manager';
END
GO

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_name = 'Head Accountant' OR Name = 'Head Accountant')
BEGIN
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-002', 'Head Accountant', 'Head Accountant', 2, 'Head Accountant', SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE Roles SET role_id = 2, role_name = 'Head Accountant' WHERE Name = 'Head Accountant' OR role_name = 'Head Accountant';
END
GO

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_name = 'Accountant' OR Name = 'Accountant')
BEGIN
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-003', 'Accountant', 'Accountant', 3, 'Accountant', SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE Roles SET role_id = 3, role_name = 'Accountant' WHERE Name = 'Accountant' OR role_name = 'Accountant';
END
GO

IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_name = 'Assistant Financial Manager' OR Name = 'Assistant Financial Manager')
BEGIN
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-004', 'Assistant Financial Manager', 'Assistant Financial Manager', 4, 'Assistant Financial Manager', SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE Roles SET role_id = 4, role_name = 'Assistant Financial Manager' WHERE Name = 'Assistant Financial Manager' OR role_name = 'Assistant Financial Manager';
END
GO

-- 2. Users table (Required for EMP-001 login and user management)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (
        user_id INT IDENTITY(1,1) PRIMARY KEY,
        employee_number NVARCHAR(50) NOT NULL UNIQUE,
        full_name NVARCHAR(200) NOT NULL,
        username NVARCHAR(100) NOT NULL UNIQUE,
        password_hash NVARCHAR(500) NOT NULL,
        role_id INT NOT NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Users_EmployeeNumber ON Users(employee_number);
    CREATE INDEX IX_Users_Username ON Users(username);
END
GO

-- 3. Clients table alignment (ensure client_id and company_name columns exist)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'client_id')
BEGIN
    ALTER TABLE Clients ADD client_id INT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'company_name')
BEGIN
    ALTER TABLE Clients ADD company_name NVARCHAR(200) NULL;
END
GO

UPDATE Clients SET client_id = 1, company_name = Name WHERE Id = 'CA-001';
UPDATE Clients SET client_id = 2, company_name = Name WHERE Id = 'CA-002';
GO

-- 4. ClientFinanceSettings table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientFinanceSettings')
BEGIN
    CREATE TABLE ClientFinanceSettings (
        finance_setting_id INT IDENTITY(1,1) PRIMARY KEY,
        client_id INT NOT NULL,
        billing_terms NVARCHAR(100) NOT NULL DEFAULT '30 Days',
        payment_terms NVARCHAR(100) NOT NULL DEFAULT 'Net 30',
        credit_terms_days INT NOT NULL DEFAULT 30,
        tax_type NVARCHAR(50) NOT NULL DEFAULT 'VATable',
        oda_rate_applicable BIT NOT NULL DEFAULT 1,
        finance_status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ClientFinanceSettings_ClientId ON ClientFinanceSettings(client_id);
END
GO

-- Seed ClientFinanceSettings for existing clients CA-001 & CA-002 if empty
IF NOT EXISTS (SELECT 1 FROM ClientFinanceSettings WHERE client_id = 1)
BEGIN
    INSERT INTO ClientFinanceSettings (client_id, billing_terms, payment_terms, credit_terms_days, tax_type, oda_rate_applicable, finance_status)
    VALUES (1, '30 Days', 'Net 30', 30, 'VATable', 1, 'Active');
END
IF NOT EXISTS (SELECT 1 FROM ClientFinanceSettings WHERE client_id = 2)
BEGIN
    INSERT INTO ClientFinanceSettings (client_id, billing_terms, payment_terms, credit_terms_days, tax_type, oda_rate_applicable, finance_status)
    VALUES (2, '15 Days', 'Net 15', 15, 'VATable', 1, 'Active');
END
GO

-- 5. ClientRates table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientRates')
BEGIN
    CREATE TABLE ClientRates (
        rate_id INT IDENTITY(1,1) PRIMARY KEY,
        client_id INT NOT NULL,
        service_type NVARCHAR(100) NOT NULL DEFAULT 'Standard Delivery',
        delivery_area NVARCHAR(100) NOT NULL,
        minimum_kg DECIMAL(18,2) NOT NULL DEFAULT 5.00,
        minimum_rate DECIMAL(18,2) NOT NULL DEFAULT 100.00,
        excess_rate_per_kg DECIMAL(18,2) NOT NULL DEFAULT 25.00,
        oda_rate DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        fuel_surcharge_rate DECIMAL(18,4) NOT NULL DEFAULT 0.1500,
        valuation_rate DECIMAL(18,4) NOT NULL DEFAULT 0.0100,
        effective_from DATE NULL,
        effective_to DATE NULL,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ClientRates_Client ON ClientRates(client_id, delivery_area);
END
GO

-- Seed ClientRates for client 1 (Lazada: NCR, Luzon)
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 1)
BEGIN
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, oda_rate, fuel_surcharge_rate, valuation_rate, is_active)
    VALUES 
    (1, 'Standard Delivery', 'NCR', 5.00, 100.00, 25.00, 0.00, 0.1500, 0.0100, 1),
    (1, 'Standard Delivery', 'Luzon', 5.00, 150.00, 30.00, 50.00, 0.1500, 0.0100, 1);
END
GO

-- 6. Invoices table alignment
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'invoice_id')
BEGIN
    ALTER TABLE Invoices ADD invoice_id INT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'invoice_number')
BEGIN
    ALTER TABLE Invoices ADD invoice_number NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'billing_period')
BEGIN
    ALTER TABLE Invoices ADD billing_period NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'invoice_status')
BEGIN
    ALTER TABLE Invoices ADD invoice_status NVARCHAR(50) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'validation_status')
BEGIN
    ALTER TABLE Invoices ADD validation_status NVARCHAR(50) NULL;
END
GO

UPDATE Invoices SET invoice_id = 1, invoice_number = InvoiceNo, invoice_status = 'Pending Validation', validation_status = 'Pending Validation', billing_period = 'October 2026' WHERE Id = 'INV-001';
UPDATE Invoices SET invoice_id = 2, invoice_number = InvoiceNo, invoice_status = 'Approved', validation_status = 'Validated', billing_period = 'October 2026' WHERE Id = 'INV-002';
GO

-- 7. AccountsReceivable table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AccountsReceivable')
BEGIN
    CREATE TABLE AccountsReceivable (
        ar_id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NOT NULL,
        client_id INT NOT NULL,
        invoice_amount DECIMAL(18,2) NOT NULL,
        paid_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        outstanding_balance DECIMAL(18,2) NOT NULL,
        overpayment_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        ar_status NVARCHAR(50) NOT NULL DEFAULT 'Outstanding',
        due_date DATE NULL,
        aging_bucket NVARCHAR(50) NOT NULL DEFAULT '0-30 days',
        collection_priority NVARCHAR(50) NOT NULL DEFAULT 'Normal',
        refund_status NVARCHAR(50) NOT NULL DEFAULT 'Not Required',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_AR_Invoice ON AccountsReceivable(invoice_id);
    CREATE INDEX IX_AR_Client ON AccountsReceivable(client_id);
END
GO

-- Seed AccountsReceivable from existing unpaid invoices if empty
IF NOT EXISTS (SELECT 1 FROM AccountsReceivable WHERE invoice_id = 1)
BEGIN
    INSERT INTO AccountsReceivable (invoice_id, client_id, invoice_amount, paid_amount, outstanding_balance, overpayment_amount, ar_status, due_date, aging_bucket, collection_priority)
    VALUES (1, 1, 50000.00, 0.00, 50000.00, 0.00, 'Outstanding', CAST(DATEADD(day, 15, GETDATE()) AS DATE), '0-30 days', 'Normal');
END
IF NOT EXISTS (SELECT 1 FROM AccountsReceivable WHERE invoice_id = 2)
BEGIN
    INSERT INTO AccountsReceivable (invoice_id, client_id, invoice_amount, paid_amount, outstanding_balance, overpayment_amount, ar_status, due_date, aging_bucket, collection_priority)
    VALUES (2, 1, 30000.00, 0.00, 30000.00, 0.00, 'Outstanding', CAST(DATEADD(day, 20, GETDATE()) AS DATE), '0-30 days', 'Normal');
END
GO

-- 8. Payments table alignment
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Payments' AND COLUMN_NAME = 'payment_id')
BEGIN
    ALTER TABLE Payments ADD payment_id INT NULL;
END
GO
UPDATE Payments SET payment_id = 1 WHERE Id = 'PAY-001';
GO

-- 9. PaymentHistory table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PaymentHistory')
BEGIN
    CREATE TABLE PaymentHistory (
        history_id INT IDENTITY(1,1) PRIMARY KEY,
        payment_id INT NULL,
        invoice_id INT NULL,
        status NVARCHAR(50) NULL,
        action NVARCHAR(100) NULL,
        remarks NVARCHAR(500) NULL,
        performed_by INT NULL,
        performed_role NVARCHAR(100) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 10. Waybills table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Waybills')
BEGIN
    CREATE TABLE Waybills (
        waybill_id INT IDENTITY(1,1) PRIMARY KEY,
        waybill_number NVARCHAR(100) NOT NULL UNIQUE,
        client_id INT NOT NULL,
        origin NVARCHAR(200) NULL,
        destination NVARCHAR(200) NULL,
        delivery_area NVARCHAR(100) NULL,
        actual_weight DECIMAL(18,2) NULL,
        length_cm DECIMAL(18,2) NULL,
        width_cm DECIMAL(18,2) NULL,
        height_cm DECIMAL(18,2) NULL,
        number_of_boxes INT NULL,
        declared_value DECIMAL(18,2) NULL,
        delivery_status NVARCHAR(50) NULL DEFAULT 'Delivered',
        pod_status NVARCHAR(50) NULL DEFAULT 'Validated',
        is_billed BIT NOT NULL DEFAULT 0,
        invoice_id INT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Waybills_Number ON Waybills(waybill_number);
    CREATE INDEX IX_Waybills_Client ON Waybills(client_id);
END
GO

-- 11. BillingDocuments table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BillingDocuments')
BEGIN
    CREATE TABLE BillingDocuments (
        billing_doc_id INT IDENTITY(1,1) PRIMARY KEY,
        waybill_id INT NOT NULL,
        client_id INT NOT NULL,
        computed_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        validation_status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        ctc_status NVARCHAR(50) NOT NULL DEFAULT 'Not Required',
        validated_by INT NULL,
        validated_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 12. InvoiceLineItems table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'InvoiceLineItems')
BEGIN
    CREATE TABLE InvoiceLineItems (
        line_item_id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NOT NULL,
        waybill_id INT NULL,
        description NVARCHAR(500) NULL,
        quantity INT NOT NULL DEFAULT 1,
        unit_price DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        line_total DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_InvoiceLineItems_Invoice ON InvoiceLineItems(invoice_id);
END
GO

-- 13. InvoiceComputations table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'InvoiceComputations')
BEGIN
    CREATE TABLE InvoiceComputations (
        computation_id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NOT NULL,
        subtotal DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        vat_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        discount_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        total_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        computed_by INT NULL,
        computed_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_InvoiceComputations_Invoice ON InvoiceComputations(invoice_id);
END
GO

-- 14. OfficialReceipts alignment
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'or_id')
BEGIN
    ALTER TABLE OfficialReceipts ADD or_id INT NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'invoice_id')
BEGIN
    ALTER TABLE OfficialReceipts ADD invoice_id INT NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'payment_id')
BEGIN
    ALTER TABLE OfficialReceipts ADD payment_id INT NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'client_id')
BEGIN
    ALTER TABLE OfficialReceipts ADD client_id INT NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'or_number')
BEGIN
    ALTER TABLE OfficialReceipts ADD or_number NVARCHAR(100) NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'issued_amount')
BEGIN
    ALTER TABLE OfficialReceipts ADD issued_amount DECIMAL(18,2) NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'issued_by')
BEGIN
    ALTER TABLE OfficialReceipts ADD issued_by NVARCHAR(100) NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'issued_at')
BEGIN
    ALTER TABLE OfficialReceipts ADD issued_at DATETIME2 NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'pdf_path')
BEGIN
    ALTER TABLE OfficialReceipts ADD pdf_path NVARCHAR(500) NULL;
END
GO
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfficialReceipts' AND COLUMN_NAME = 'status')
BEGIN
    ALTER TABLE OfficialReceipts ADD status NVARCHAR(50) NULL;
END
GO

-- 15. CashInflows table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashInflows')
BEGIN
    CREATE TABLE CashInflows (
        inflow_id INT IDENTITY(1,1) PRIMARY KEY,
        payment_id INT NULL,
        client_id INT NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        source NVARCHAR(100) NULL DEFAULT 'Client Payment',
        reference_number NVARCHAR(100) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Validated',
        transaction_date DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 16. CashOutflows table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashOutflows')
BEGIN
    CREATE TABLE CashOutflows (
        outflow_id INT IDENTITY(1,1) PRIMARY KEY,
        category NVARCHAR(100) NULL,
        description NVARCHAR(255) NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        approved_by INT NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Approved',
        transaction_date DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 17. CashAdvances table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CashAdvances')
BEGIN
    CREATE TABLE CashAdvances (
        cash_advance_id INT IDENTITY(1,1) PRIMARY KEY,
        employee_id INT NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        purpose NVARCHAR(500) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        approved_by INT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 18. Liquidations table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Liquidations')
BEGIN
    CREATE TABLE Liquidations (
        liquidation_id INT IDENTITY(1,1) PRIMARY KEY,
        reference_number NVARCHAR(100) NULL,
        cash_advance_id INT NULL,
        submitted_by INT NULL,
        submitted_by_name NVARCHAR(150) NULL,
        total_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        leftover_cash DECIMAL(18,2) NULL DEFAULT 0.00,
        reimbursement_amount DECIMAL(18,2) NULL DEFAULT 0.00,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        validated_by INT NULL,
        validated_at DATETIME2 NULL,
        date_submitted DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 19. LiquidationLineItems table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LiquidationLineItems')
BEGIN
    CREATE TABLE LiquidationLineItems (
        line_item_id INT IDENTITY(1,1) PRIMARY KEY,
        liquidation_id INT NOT NULL,
        description NVARCHAR(255) NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        receipt_attached BIT NOT NULL DEFAULT 0,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 20. LiquidationDocuments table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LiquidationDocuments')
BEGIN
    CREATE TABLE LiquidationDocuments (
        document_id INT IDENTITY(1,1) PRIMARY KEY,
        liquidation_id INT NOT NULL,
        document_type NVARCHAR(100) NULL,
        file_path NVARCHAR(500) NULL,
        uploaded_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 21. FinancialAdjustments table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FinancialAdjustments')
BEGIN
    CREATE TABLE FinancialAdjustments (
        adjustment_id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NULL,
        client_id INT NULL,
        adjustment_type NVARCHAR(50) NULL DEFAULT 'Adjustment',
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        reason NVARCHAR(500) NULL,
        requested_by INT NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        approved_by INT NULL,
        approved_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 22. CreditMemos table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CreditMemos')
BEGIN
    CREATE TABLE CreditMemos (
        credit_memo_id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NULL,
        client_id INT NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        reason NVARCHAR(500) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        approved_by INT NULL,
        approved_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 23. RefundRequests table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RefundRequests')
BEGIN
    CREATE TABLE RefundRequests (
        refund_id INT IDENTITY(1,1) PRIMARY KEY,
        payment_id INT NULL,
        client_id INT NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        reason NVARCHAR(500) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Approval',
        processed_by INT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 24. Settlements table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Settlements')
BEGIN
    CREATE TABLE Settlements (
        settlement_id INT IDENTITY(1,1) PRIMARY KEY,
        record_type NVARCHAR(100) NULL DEFAULT 'Settlement',
        courier_trip NVARCHAR(150) NULL,
        amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        submitted_by INT NULL,
        submitted_at DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
        status NVARCHAR(50) NOT NULL DEFAULT 'Pending Validation',
        validated_by INT NULL,
        validated_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 25. AIReviewHistory table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AIReviewHistory')
BEGIN
    CREATE TABLE AIReviewHistory (
        review_id INT IDENTITY(1,1) PRIMARY KEY,
        document_number NVARCHAR(100) NULL,
        final_decision NVARCHAR(100) NULL,
        reviewer NVARCHAR(100) NULL,
        decision_reason NVARCHAR(500) NULL,
        reviewed_date DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 26. AIOutputHistory table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AIOutputHistory')
BEGIN
    CREATE TABLE AIOutputHistory (
        output_id INT IDENTITY(1,1) PRIMARY KEY,
        output_type NVARCHAR(100) NULL,
        source_reference NVARCHAR(100) NULL,
        processing_status NVARCHAR(100) NULL,
        model_name NVARCHAR(100) NULL,
        version NVARCHAR(50) NULL,
        message NVARCHAR(MAX) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 27. Reports table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Reports')
BEGIN
    CREATE TABLE Reports (
        report_id INT IDENTITY(1,1) PRIMARY KEY,
        report_code NVARCHAR(50) NOT NULL UNIQUE,
        report_name NVARCHAR(200) NOT NULL,
        report_type NVARCHAR(100) NOT NULL,
        category NVARCHAR(100) NULL DEFAULT 'Financial',
        date_from DATETIME2 NULL,
        date_to DATETIME2 NULL,
        generated_by NVARCHAR(100) NOT NULL,
        generated_by_role NVARCHAR(100) NULL,
        record_count INT NOT NULL DEFAULT 0,
        total_amount DECIMAL(18,2) NULL DEFAULT 0.00,
        file_format NVARCHAR(20) NULL DEFAULT 'PDF',
        download_url NVARCHAR(500) NULL,
        summary_notes NVARCHAR(MAX) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Generated',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        is_deleted BIT NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_Reports_Type ON Reports(report_type);
    CREATE INDEX IX_Reports_CreatedAt ON Reports(created_at);
END
GO

IF NOT EXISTS (SELECT 1 FROM Reports WHERE report_code = 'REP-2026-0001')
BEGIN
    INSERT INTO Reports (report_code, report_name, report_type, category, date_from, date_to, generated_by, generated_by_role, record_count, total_amount, file_format, summary_notes, status, created_at)
    VALUES 
    ('REP-2026-0001', 'Accounts Receivable Aging Report', 'aging', 'Financial', '2026-01-01', '2026-10-16', 'Crystalyn Joyce C. Fajardo', 'Financial Manager', 6, 82957.20, 'PDF', 'Comprehensive aged receivables report covering 1-30, 31-60, 61-90, and 90+ day overdue buckets.', 'Generated', DATEADD(HOUR, -2, SYSUTCDATETIME())),
    ('REP-2026-0002', 'Daily Collection Summary Report', 'collections', 'Financial', '2026-10-01', '2026-10-16', 'Crystalyn Joyce C. Fajardo', 'Financial Manager', 12, 145800.00, 'PDF', 'Consolidated collection summary from SpeedPay and direct bank settlements.', 'Generated', DATEADD(HOUR, -8, SYSUTCDATETIME())),
    ('REP-2026-0003', 'Official Invoices & Billing Summary', 'invoices', 'Financial', '2026-09-01', '2026-10-16', 'Head Accountant', 'Head Accountant', 7, 237957.20, 'Excel', 'Verified client billing statement across Shopee, Lazada, and corporate clients.', 'Generated', DATEADD(DAY, -1, SYSUTCDATETIME())),
    ('REP-2026-0004', 'AI Duplicate Alert & OCR Audit Summary', 'duplicate', 'Duplicate Detection', '2026-10-01', '2026-10-16', 'Assistant Financial Manager', 'Assistant FM', 16, 0.00, 'PDF', 'AI-assisted OCR duplicate detection and perceptual hash comparison audit.', 'Generated', DATEADD(DAY, -2, SYSUTCDATETIME())),
    ('REP-2026-0005', 'Courier Liquidation Validation Report', 'liquidations', 'Liquidation', '2026-10-01', '2026-10-16', 'Assistant Financial Manager', 'Assistant FM', 4, 18450.00, 'PDF', 'Disbursement liquidation validation covering fuel, toll, and operational advances.', 'Generated', DATEADD(DAY, -3, SYSUTCDATETIME())),
    ('REP-2026-0006', 'Executive Cash Flow & Treasury Ledger', 'cash-flow', 'Financial', '2026-01-01', '2026-10-16', 'Crystalyn Joyce C. Fajardo', 'Financial Manager', 28, 520000.00, 'PDF', 'Net operational cash inflow vs outflow reconciliation for Q3-Q4 2026.', 'Generated', DATEADD(DAY, -5, SYSUTCDATETIME()));
END
GO

PRINT 'All FOMS_DB tables and columns checked/created successfully.';
GO
-- ============================================================================
-- STARS Collection Tasks & DMS Integration Database Schema Alignment
-- ============================================================================

USE FOMS_DB;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Ensure Coordinator Role exists
IF NOT EXISTS (SELECT 1 FROM Roles WHERE role_name = 'Billing & Collection Coordinator' OR role_name = 'Coordinator')
BEGIN
    INSERT INTO Roles (Id, Name, Description, role_id, role_name, created_at)
    VALUES ('ROLE-005', 'Billing & Collection Coordinator', 'STARS Billing & Collection Coordinator', 5, 'Billing & Collection Coordinator', SYSUTCDATETIME());
END
GO

-- 2. Ensure Coordinators exist in Users table for Graph Routing Engine
IF NOT EXISTS (SELECT 1 FROM Users WHERE employee_number = 'EMP-006')
BEGIN
    -- Password hash for 'password123'
    INSERT INTO Users (employee_number, full_name, username, password_hash, role_id, is_active, created_at, updated_at)
    VALUES ('EMP-006', 'Joana Marie Chan Ogaya', 'coordinator.ogaya', 'ef92b778bafe771e89245b89ecbc08a44a4e166c06659911881f383d4473e94f', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM Users WHERE employee_number = 'EMP-007')
BEGIN
    INSERT INTO Users (employee_number, full_name, username, password_hash, role_id, is_active, created_at, updated_at)
    VALUES ('EMP-007', 'Mark Anthony Santos', 'coordinator.santos', 'ef92b778bafe771e89245b89ecbc08a44a4e166c06659911881f383d4473e94f', 5, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END
GO

-- 3. Create StarsCollectionTasks table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StarsCollectionTasks')
BEGIN
    CREATE TABLE StarsCollectionTasks (
        task_id INT IDENTITY(1,1) PRIMARY KEY,
        task_number NVARCHAR(100) NOT NULL UNIQUE,
        task_type NVARCHAR(100) NOT NULL DEFAULT 'Collection Follow-up',
        invoice_number NVARCHAR(100) NOT NULL,
        client_name NVARCHAR(200) NOT NULL,
        amount_due DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        days_overdue INT NOT NULL DEFAULT 0,
        assigned_coordinator_id NVARCHAR(100) NULL,
        assigned_coordinator_name NVARCHAR(200) NOT NULL,
        routing_score DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        routing_reason NVARCHAR(500) NULL,
        status NVARCHAR(50) NOT NULL DEFAULT 'Assigned',
        action_taken NVARCHAR(500) NULL,
        outcome NVARCHAR(100) NULL,
        promised_payment_date DATETIME2 NULL,
        execution_notes NVARCHAR(MAX) NULL,
        executed_at DATETIME2 NULL,
        executed_by NVARCHAR(200) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_StarsCollectionTasks_InvoiceNumber ON StarsCollectionTasks(invoice_number);
    CREATE INDEX IX_StarsCollectionTasks_Status ON StarsCollectionTasks(status);
    CREATE INDEX IX_StarsCollectionTasks_Coordinator ON StarsCollectionTasks(assigned_coordinator_id);
END
GO

-- 4. Ensure CashAdvances has approval and tracking columns
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashAdvances' AND COLUMN_NAME = 'ApprovedBy')
BEGIN
    ALTER TABLE CashAdvances ADD ApprovedBy NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashAdvances' AND COLUMN_NAME = 'ApprovedAt')
BEGIN
    ALTER TABLE CashAdvances ADD ApprovedAt DATETIME2 NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashAdvances' AND COLUMN_NAME = 'DisbursedAt')
BEGIN
    ALTER TABLE CashAdvances ADD DisbursedAt DATETIME2 NULL;
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashAdvances' AND COLUMN_NAME = 'DisbursedBy')
BEGIN
    ALTER TABLE CashAdvances ADD DisbursedBy NVARCHAR(100) NULL;
END
GO

-- 5. Seed initial Cash Advances for testing and UI display
IF NOT EXISTS (SELECT 1 FROM CashAdvances WHERE AdvanceId = 'CA-2026-0001')
BEGIN
    INSERT INTO CashAdvances (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy, Status, Notes, CreatedAt)
    VALUES ('CA-2026-0001', 'Juan Dela Cruz', 'TRIP-NCR-2026-001', 5000.00, SYSUTCDATETIME(), 'Ops Dispatcher', 'Approved', 'Fuel, toll, and route maintenance for NCR Run', SYSUTCDATETIME());

    INSERT INTO CashAdvances (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy, Status, Notes, CreatedAt)
    VALUES ('CA-2026-0002', 'Pedro Santos', 'TRIP-LUZ-2026-002', 3500.00, SYSUTCDATETIME(), 'Ops Dispatcher', 'Pending Finance Review', 'Pampanga and Clark delivery run cash advance', SYSUTCDATETIME());

    INSERT INTO CashAdvances (AdvanceId, CourierName, TripReference, AmountGiven, ReleaseDate, ReleasedBy, Status, Notes, CreatedAt)
    VALUES ('CA-2026-0003', 'Carlos Reyes', 'TRIP-VIS-2026-003', 7000.00, SYSUTCDATETIME(), 'Finance Manager', 'Disbursed', 'Visayas logistics corridor operational advance', SYSUTCDATETIME());
END
GO

PRINT 'STARS Collection Tasks and DMS Integration tables verified successfully.';
GO
-- Link existing waybills to existing invoices
UPDATE Waybills SET invoice_no = 'INV-2026-007', is_billed = 1, pod_status = 'Billed' WHERE waybill_number IN ('WB-NCR-88491', 'WB-NCR-88492');
UPDATE Invoices SET WaybillIds = 'WB-NCR-88491,WB-NCR-88492' WHERE InvoiceNo = 'INV-2026-007';

UPDATE Waybills SET invoice_no = 'INV-2026-009', is_billed = 1, pod_status = 'Billed' WHERE waybill_number = 'WB-NCR-88493';
UPDATE Invoices SET WaybillIds = 'WB-NCR-88493' WHERE InvoiceNo = 'INV-2026-009';

-- Insert additional waybills if not exist
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-NCR-88496')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-NCR-88496', 1, 'Manila Hub', 'Pasig City', 'NCR', 5.0, 1500.00, 'Delivered', 'Billed', 1, 'INV-2026-010', DATEADD(day, -4, SYSUTCDATETIME()), SYSUTCDATETIME());
UPDATE Waybills SET invoice_no = 'INV-2026-010', is_billed = 1, pod_status = 'Billed' WHERE waybill_number = 'WB-NCR-88496';
UPDATE Invoices SET WaybillIds = 'WB-NCR-88496' WHERE InvoiceNo = 'INV-2026-010';

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-NCR-88497')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-NCR-88497', 1, 'Manila Hub', 'Taguig City', 'NCR', 5.5, 2000.00, 'Delivered', 'Billed', 1, 'INV-2026-004', DATEADD(day, -6, SYSUTCDATETIME()), SYSUTCDATETIME());
UPDATE Waybills SET invoice_no = 'INV-2026-004', is_billed = 1, pod_status = 'Billed' WHERE waybill_number = 'WB-NCR-88497';
UPDATE Invoices SET WaybillIds = 'WB-NCR-88497' WHERE InvoiceNo = 'INV-2026-004';

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-NCR-88498')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-NCR-88498', 1, 'Manila Hub', 'Makati City', 'NCR', 10.0, 3500.00, 'Delivered', 'Billed', 1, 'INV-2026-008', DATEADD(day, -5, SYSUTCDATETIME()), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-NCR-88499')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-NCR-88499', 1, 'Manila Hub', 'Caloocan City', 'NCR', 9.5, 3000.00, 'Delivered', 'Billed', 1, 'INV-2026-008', DATEADD(day, -5, SYSUTCDATETIME()), SYSUTCDATETIME());
UPDATE Waybills SET invoice_no = 'INV-2026-008', is_billed = 1, pod_status = 'Billed' WHERE waybill_number IN ('WB-NCR-88498', 'WB-NCR-88499');
UPDATE Invoices SET WaybillIds = 'WB-NCR-88498,WB-NCR-88499' WHERE InvoiceNo = 'INV-2026-008';

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-LZD-00101')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-LZD-00101', 1, 'Manila Hub', 'Cebu Central', 'Visayas', 45.0, 25000.00, 'Delivered', 'Billed', 1, 'LZD-2026-0001', DATEADD(day, -12, SYSUTCDATETIME()), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-LZD-00102')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-LZD-00102', 1, 'Manila Hub', 'Davao Central', 'Mindanao', 38.0, 25000.00, 'Delivered', 'Billed', 1, 'LZD-2026-0001', DATEADD(day, -12, SYSUTCDATETIME()), SYSUTCDATETIME());
UPDATE Waybills SET invoice_no = 'LZD-2026-0001', is_billed = 1, pod_status = 'Billed' WHERE waybill_number IN ('WB-LZD-00101', 'WB-LZD-00102');
UPDATE Invoices SET WaybillIds = 'WB-LZD-00101,WB-LZD-00102' WHERE InvoiceNo = 'LZD-2026-0001';

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-LZD-00103')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-LZD-00103', 1, 'Manila Hub', 'Iloilo City', 'Visayas', 28.0, 15000.00, 'Delivered', 'Billed', 1, 'BI-2026-0001', DATEADD(day, -8, SYSUTCDATETIME()), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-LZD-00104')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-LZD-00104', 1, 'Manila Hub', 'Cagayan de Oro', 'Mindanao', 22.5, 15000.00, 'Delivered', 'Billed', 1, 'BI-2026-0001', DATEADD(day, -8, SYSUTCDATETIME()), SYSUTCDATETIME());
UPDATE Waybills SET invoice_no = 'BI-2026-0001', is_billed = 1, pod_status = 'Billed' WHERE waybill_number IN ('WB-LZD-00103', 'WB-LZD-00104');
UPDATE Invoices SET WaybillIds = 'WB-LZD-00103,WB-LZD-00104' WHERE InvoiceNo = 'BI-2026-0001';

UPDATE Waybills SET invoice_no = 'SHP-2026-0001', is_billed = 1, pod_status = 'Billed' WHERE waybill_number IN ('WB-NCR-88494', 'WB-NCR-88495');
UPDATE Invoices SET WaybillIds = 'WB-NCR-88494,WB-NCR-88495' WHERE InvoiceNo = 'SHP-2026-0001';

-- Seed fresh unbilled validated waybills so user can create invoices smoothly
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90001')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES 
    ('WB-VAL-90001', 1, 'Manila Hub', 'Quezon City', 'NCR', 8.5, 2500.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90002', 1, 'Manila Hub', 'Mandaluyong City', 'NCR', 11.0, 3200.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90003', 1, 'Cebu Hub', 'Lapu-Lapu City', 'Visayas', 14.5, 4500.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90004', 2, 'Manila Hub', 'Pasig City', 'NCR', 7.2, 1800.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90005', 2, 'Clark Hub', 'Angeles City', 'Luzon', 18.0, 6000.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90006', 3, 'Manila Hub', 'Taguig BGC', 'NCR', 5.0, 2000.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME()),
    ('WB-VAL-90007', 4, 'Manila Hub', 'Makati CBD', 'NCR', 4.5, 3500.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME());
-- Seed comprehensive billing rates and unbilled validated waybills for Accountant Create Invoice testing
USE FOMS_DB;
GO

-- 1. SEED BILLING RATES FOR ALL CLIENTS ACROSS ALL REGIONS
-- Client 1: Lazada Philippines
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 1 AND delivery_area = 'NCR')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (1, 'Standard Delivery', 'NCR', 5.0, 120.00, 20.00, 0.01, 0.05, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 1 AND delivery_area = 'Luzon')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (1, 'Standard Delivery', 'Luzon', 5.0, 180.00, 30.00, 0.01, 0.08, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 1 AND delivery_area = 'Visayas')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (1, 'Standard Delivery', 'Visayas', 5.0, 220.00, 40.00, 0.015, 0.10, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 1 AND delivery_area = 'Mindanao')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (1, 'Standard Delivery', 'Mindanao', 5.0, 260.00, 50.00, 0.015, 0.12, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

-- Client 2: Shopee Philippines
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 2 AND delivery_area = 'NCR')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (2, 'Standard Delivery', 'NCR', 5.0, 110.00, 18.00, 0.01, 0.05, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 2 AND delivery_area = 'Luzon')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (2, 'Standard Delivery', 'Luzon', 5.0, 170.00, 28.00, 0.01, 0.08, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 2 AND delivery_area = 'Visayas')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (2, 'Standard Delivery', 'Visayas', 5.0, 210.00, 38.00, 0.015, 0.10, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 2 AND delivery_area = 'Mindanao')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (2, 'Standard Delivery', 'Mindanao', 5.0, 250.00, 48.00, 0.015, 0.12, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

-- Client 3: TikTok Shop Philippines
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 3 AND delivery_area = 'NCR')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (3, 'Standard Delivery', 'NCR', 5.0, 105.00, 20.00, 0.01, 0.05, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 3 AND delivery_area = 'Luzon')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (3, 'Standard Delivery', 'Luzon', 5.0, 165.00, 30.00, 0.01, 0.08, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 3 AND delivery_area = 'Visayas')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (3, 'Standard Delivery', 'Visayas', 5.0, 205.00, 38.00, 0.015, 0.10, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 3 AND delivery_area = 'Mindanao')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (3, 'Standard Delivery', 'Mindanao', 5.0, 245.00, 48.00, 0.015, 0.12, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

-- Client 4: Zalora Philippines
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 4 AND delivery_area = 'NCR')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (4, 'Standard Delivery', 'NCR', 5.0, 115.00, 22.00, 0.01, 0.05, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 4 AND delivery_area = 'Luzon')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (4, 'Standard Delivery', 'Luzon', 5.0, 175.00, 32.00, 0.01, 0.08, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 4 AND delivery_area = 'Visayas')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (4, 'Standard Delivery', 'Visayas', 5.0, 215.00, 42.00, 0.015, 0.10, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 4 AND delivery_area = 'Mindanao')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (4, 'Standard Delivery', 'Mindanao', 5.0, 255.00, 50.00, 0.015, 0.12, DATEADD(month, -3, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

-- Client 5: Test Client Company
IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 5 AND delivery_area = 'Luzon')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (5, 'Standard Delivery', 'Luzon', 5.0, 150.00, 30.00, 0.01, 0.08, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 5 AND delivery_area = 'Visayas')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (5, 'Standard Delivery', 'Visayas', 5.0, 200.00, 40.00, 0.015, 0.10, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ClientRates WHERE client_id = 5 AND delivery_area = 'Mindanao')
    INSERT INTO ClientRates (client_id, service_type, delivery_area, minimum_kg, minimum_rate, excess_rate_per_kg, valuation_rate, fuel_surcharge_rate, effective_from, is_active, created_at, updated_at)
    VALUES (5, 'Standard Delivery', 'Mindanao', 5.0, 250.00, 50.00, 0.015, 0.12, DATEADD(month, -2, CAST(GETUTCDATE() AS date)), 1, SYSUTCDATETIME(), SYSUTCDATETIME());


-- 2. SEED UNBILLED VALIDATED WAYBILLS FOR TESTING CREATE INVOICE

-- For Zalora Philippines (client_id = 4)
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90008')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90008', 4, 'Manila Hub', 'Bonifacio Global City, Taguig', 'NCR', 6.2, 4200.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90009')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90009', 4, 'Clark Hub', 'San Fernando, Pampanga', 'Luzon', 12.0, 7800.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -3, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90010')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90010', 4, 'Cebu Hub', 'Cebu Central, Cebu City', 'Visayas', 8.5, 5500.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -3, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90011')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90011', 4, 'Davao Hub', 'Davao Central, Davao City', 'Mindanao', 15.0, 11000.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -4, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90012')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90012', 4, 'Manila Hub', 'Ortigas Center, Pasig City', 'NCR', 3.8, 2900.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90013')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90013', 4, 'Laguna Hub', 'Lipa City, Batangas', 'Luzon', 7.5, 3600.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME());

-- For Shopee Philippines (client_id = 2)
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90014')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90014', 2, 'Cebu Hub', 'Bacolod City, Negros Occidental', 'Visayas', 9.0, 3200.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90015')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90015', 2, 'Davao Hub', 'Cagayan de Oro City', 'Mindanao', 14.2, 5800.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -3, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90016')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90016', 2, 'Manila Hub', 'Quezon City', 'NCR', 5.5, 2400.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME());

-- For TikTok Shop Philippines (client_id = 3)
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90017')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90017', 3, 'Clark Hub', 'Baguio City, Benguet', 'Luzon', 8.4, 3600.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90018')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90018', 3, 'Cebu Hub', 'Iloilo City', 'Visayas', 11.0, 4900.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -3, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90019')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90019', 3, 'Manila Hub', 'Mandaluyong City', 'NCR', 4.2, 1950.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -1, SYSUTCDATETIME()), SYSUTCDATETIME());

-- For Lazada Philippines (client_id = 1)
IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90020')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90020', 1, 'Laguna Hub', 'Calamba City, Laguna', 'Luzon', 9.8, 4100.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -2, SYSUTCDATETIME()), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Waybills WHERE waybill_number = 'WB-VAL-90021')
    INSERT INTO Waybills (waybill_number, client_id, origin, destination, delivery_area, actual_weight, declared_value, delivery_status, pod_status, is_billed, invoice_no, created_at, updated_at)
    VALUES ('WB-VAL-90021', 1, 'Davao Hub', 'General Santos City', 'Mindanao', 16.5, 8700.00, 'Delivered', 'Validated', 0, NULL, DATEADD(day, -3, SYSUTCDATETIME()), SYSUTCDATETIME());

PRINT 'Successfully seeded billing rates and unbilled validated waybills!';
GO
-- Seed sample approval requests and history into Azure SQL Database
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-1D347C')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-1D347C', 'CO-20261021-1D347C', 'CO-TEST-2851', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Pending Assistant Review', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-49BEB2')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-49BEB2', 'CO-20261021-49BEB2', 'CO-TEST-8383', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Approved by Assistant', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-35C176')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-35C176', 'CO-20261021-35C176', 'CO-HIGH-4014', 'Dell Systems Philippines', 'Equipment Purchase', 'Server rack replacement unit', 25000.00, 'PHP', 'Level 2', 'Financial Manager', 'Approved by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-C4B425')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-C4B425', 'CO-20261021-C4B425', 'CO-TEST-2980', 'National Book Store', 'Office Supplies', 'Quarterly printer paper and toner cartridges', 4500.00, 'PHP', 'Level 1', 'Assistant Financial Manager', 'Approved by Assistant', 0, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-3427A9')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-3427A9', 'CO-20261021-3427A9', 'CO-HIGH-1921', 'Dell Systems Philippines', 'Equipment Purchase', 'Server rack replacement unit', 25000.00, 'PHP', 'Level 2', 'Financial Manager', 'Approved by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-6FEAE9')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-6FEAE9', 'CO-20261021-6FEAE9', 'CO-REJ-9445', 'Unverified Vendor', 'Equipment Purchase', 'Unbudgeted item', 18000.00, 'PHP', 'Level 2', 'Financial Manager', 'Rejected by Financial Manager', 1, 10000.00, 'EMP-004', 'EMP-004', 'Budget not approved for this fiscal quarter.', NULL, 0);

IF NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-BDF390')
INSERT INTO dbo.CashOutflowApprovalRequests (request_guid, cash_outflow_id, transaction_reference, payee, category, purpose, amount, currency, approval_level, assigned_role, status, is_escalated, escalation_threshold, submitted_by, submitted_by_id, rejection_reason, remarks, is_posted)
VALUES ('CO-20261021-BDF390', 'CO-20261021-BDF390', 'CO-RET-6515', 'Airline Booking', 'Travel Expense', 'Conference attendance', 15000.00, 'PHP', 'Level 2', 'Financial Manager', 'Returned for Correction', 1, 10000.00, 'EMP-004', 'EMP-004', NULL, 'Please attach official receipt and travel authorization slip.', 0);

-- Also add sample history
DECLARE @req3 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-35C176');
IF @req3 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req3)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req3, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 25,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req3, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req3, 'EMP-001', 'EMP-001', 'Financial Manager', 'Approved', 'Pending Financial Manager Approval', 'Approved by Financial Manager', 'Approved by Financial Manager. Cash outflow posted.');
END;

DECLARE @req5 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-3427A9');
IF @req5 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req5)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req5, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 25,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req5, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req5, 'EMP-001', 'EMP-001', 'Financial Manager', 'Approved', 'Pending Financial Manager Approval', 'Approved by Financial Manager', 'Approved by Financial Manager. Cash outflow posted.');
END;

DECLARE @req6 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-6FEAE9');
IF @req6 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req6)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req6, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 18,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req6, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req6, 'EMP-001', 'EMP-001', 'Financial Manager', 'Rejected', 'Pending Financial Manager Approval', 'Rejected by Financial Manager', 'Budget not approved for this fiscal quarter.');
END;

DECLARE @req7 INT = (SELECT approval_request_id FROM dbo.CashOutflowApprovalRequests WHERE request_guid = 'CO-20261021-BDF390');
IF @req7 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CashOutflowApprovalHistory WHERE approval_request_id = @req7)
BEGIN
    INSERT INTO dbo.CashOutflowApprovalHistory (approval_request_id, actor_user_id, actor_name, actor_role, action, previous_status, new_status, remarks)
    VALUES 
    (@req7, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Submitted', 'None', 'Pending Assistant Review', 'Submitted. Amount 15,000.00 exceeds Assistant threshold 10,000.00 (Flagged for Level 2 review).'),
    (@req7, 'EMP-004', 'EMP-004', 'Assistant of Financial Manager', 'Escalated', 'Pending Assistant Review', 'Pending Financial Manager Approval', 'Exceeds Assistant authorization limit. Endorsed for Financial Manager approval.'),
    (@req7, 'EMP-001', 'EMP-001', 'Financial Manager', 'Returned', 'Pending Financial Manager Approval', 'Returned for Correction', 'Please attach official receipt and travel authorization slip.');
END;
