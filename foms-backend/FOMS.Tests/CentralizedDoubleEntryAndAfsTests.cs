using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class CentralizedDoubleEntryAndAfsTests
{
    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task OpeningBalances_ValidBalancedBatch_SavedSuccessfully()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        var batch = new OpeningBalanceBatchDto
        {
            AsOfDate = new DateTime(2026, 1, 1),
            Balances = new List<OpeningBalanceInputDto>
            {
                new() { AccountCode = "101", AccountName = "Cash in Bank", Debit = 500000m, Credit = 0m, DepartmentCode = "DEP-FIN", Category = "Cash" },
                new() { AccountCode = "102", AccountName = "Accounts Receivable", Debit = 200000m, Credit = 0m, DepartmentCode = "DEP-FIN", Category = "Receivables" },
                new() { AccountCode = "201", AccountName = "Accounts Payable", Debit = 0m, Credit = 150000m, DepartmentCode = "DEP-FIN", Category = "Payables" },
                new() { AccountCode = "301", AccountName = "Owner's Equity", Debit = 0m, Credit = 550000m, DepartmentCode = "DEP-FIN", Category = "Equity" }
            }
        };

        var result = await service.SetOpeningBalancesAsync(batch, "EMP-001", "Finance Manager");

        Assert.Equal(4, result.Count);
        var inDb = await context.GoLiveOpeningBalances.ToListAsync();
        Assert.Equal(4, inDb.Count);
        Assert.Equal(700000m, inDb.Sum(b => b.Debit));
        Assert.Equal(700000m, inDb.Sum(b => b.Credit));

        // Verify audit log
        var log = await context.AuditLogs.FirstOrDefaultAsync(l => l.Action == "GO_LIVE_OPENING_BALANCES");
        Assert.NotNull(log);
        Assert.Contains("balanced", log.Details);
    }

    [Fact]
    public async Task OpeningBalances_UnbalancedBatch_ThrowsInvalidOperationException()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        var batch = new OpeningBalanceBatchDto
        {
            AsOfDate = new DateTime(2026, 1, 1),
            Balances = new List<OpeningBalanceInputDto>
            {
                new() { AccountCode = "101", AccountName = "Cash in Bank", Debit = 500000m, Credit = 0m },
                new() { AccountCode = "301", AccountName = "Owner's Equity", Debit = 0m, Credit = 400000m } // Debit != Credit
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetOpeningBalancesAsync(batch, "EMP-001", "Finance Manager"));

        Assert.Contains("Double entries must balance", ex.Message);
    }

    [Fact]
    public async Task OpeningBalances_AttemptToEnterTwice_Blocked()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        var batch = new OpeningBalanceBatchDto
        {
            AsOfDate = new DateTime(2026, 1, 1),
            Balances = new List<OpeningBalanceInputDto>
            {
                new() { AccountCode = "101", AccountName = "Cash", Debit = 10000m, Credit = 0m },
                new() { AccountCode = "301", AccountName = "Equity", Debit = 0m, Credit = 10000m }
            }
        };

        await service.SetOpeningBalancesAsync(batch, "EMP-001", "Finance Manager");

        // Second attempt
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetOpeningBalancesAsync(batch, "EMP-001", "Finance Manager"));

        Assert.Contains("already been entered once at go-live", ex.Message);
    }

    [Fact]
    public async Task OpeningBalances_NonFinanceManager_ThrowsUnauthorizedAccessException()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        var batch = new OpeningBalanceBatchDto
        {
            AsOfDate = new DateTime(2026, 1, 1),
            Balances = new List<OpeningBalanceInputDto>
            {
                new() { AccountCode = "101", AccountName = "Cash", Debit = 10000m, Credit = 0m },
                new() { AccountCode = "301", AccountName = "Equity", Debit = 0m, Credit = 10000m }
            }
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.SetOpeningBalancesAsync(batch, "EMP-002", "Head Accountant"));

        Assert.Contains("Only Finance Manager can establish go-live opening balances", ex.Message);
    }

    [Fact]
    public async Task RevenueRecognition_AccrualAtSent_CashAtValidatedPayment()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Seed opening balance
        context.GoLiveOpeningBalances.AddRange(
            new GoLiveOpeningBalance { AccountCode = "101", AccountName = "Cash", Debit = 100000m, Credit = 0m },
            new GoLiveOpeningBalance { AccountCode = "301", AccountName = "Equity", Debit = 0m, Credit = 100000m }
        );

        // Invoice 1: Sent (Accrual recognized = 50,000)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            InvoiceNo = "INV-2026-001",
            ClientId = "CLI-001",
            ClientName = "Alpha Logistics",
            DocumentStatus = "Sent",
            BillingDate = "2026-05-10",
            TotalAmount = 50000m,
            Balance = 50000m
        });

        // Invoice 2: Draft (NOT recognized)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            InvoiceNo = "INV-2026-002",
            ClientId = "CLI-002",
            ClientName = "Beta Corp",
            DocumentStatus = "Draft",
            BillingDate = "2026-05-11",
            TotalAmount = 30000m,
            Balance = 30000m
        });

        // Invoice 3: Paid with Validated payment of 20,000 (Accrual & Cash recognized)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            InvoiceNo = "INV-2026-003",
            ClientId = "CLI-003",
            ClientName = "Gamma Retail",
            DocumentStatus = "Paid",
            PaymentStatus = "Paid",
            BillingDate = "2026-05-12",
            TotalAmount = 20000m,
            Balance = 0m
        });

        // Validated payment for INV-2026-003
        context.Payments.Add(new FOMS.Domain.Entities.Payment
        {
            OrNumber = "OR-2026-001",
            InvoiceId = "INV-2026-003",
            InvoiceNo = "INV-2026-003",
            Amount = 20000m,
            PaymentDate = "2026-05-15",
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 5, 15)
        });

        // Pending Validation payment (NOT recognized in cash revenue)
        context.Payments.Add(new FOMS.Domain.Entities.Payment
        {
            OrNumber = "OR-2026-002",
            InvoiceId = "INV-2026-001",
            InvoiceNo = "INV-2026-001",
            Amount = 15000m,
            PaymentDate = "2026-05-18",
            PaymentStatus = "Pending Validation"
        });

        await context.SaveChangesAsync();

        var isDto = await service.GenerateIncomeStatementAsync(2026, "Year", 1);

        // Accrual Revenue = INV-001 (50,000) + INV-003 (20,000) = 70,000
        Assert.Equal(70000m, isDto.TotalAccrualRevenue);

        // Cash Revenue = PAY-001 validated (20,000)
        Assert.Equal(20000m, isDto.TotalCashRevenue);
    }

    [Fact]
    public async Task ExpenseRecognition_LiquidationsDisbursementsReimbursements()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Validated Liquidation = 12,000 Fuel
        context.Liquidations.Add(new Liquidation
        {
            CashAdvanceId = 1,
            TotalAmount = 12000m,
            LeftoverCash = 3000m,
            Status = "Validated",
            DepartmentCode = "DEP-OPS",
            Category = "Fuel",
            FiscalYear = 2026,
            ValidatedAt = new DateTime(2026, 3, 10),
            CreatedAt = new DateTime(2026, 3, 5)
        });

        // Pending Liquidation = 5,000 (NOT recognized as accrual expense)
        context.Liquidations.Add(new Liquidation
        {
            CashAdvanceId = 2,
            TotalAmount = 5000m,
            LeftoverCash = 0m,
            Status = "Pending",
            DepartmentCode = "DEP-OPS",
            Category = "Toll",
            FiscalYear = 2026,
            CreatedAt = new DateTime(2026, 3, 15)
        });

        // Direct disbursement Released = 25,000 Repairs and Maintenance
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DISB-2026-001",
            DepartmentCode = "DEP-OPS",
            Category = "Repairs and Maintenance",
            FiscalYear = 2026,
            Amount = 25000m,
            Status = "Released",
            ReleasedAt = new DateTime(2026, 3, 20),
            CreatedAt = new DateTime(2026, 3, 18)
        });

        // Paid Reimbursement = 8,000 Food allowance
        context.Reimbursements.Add(new Reimbursement
        {
            ReferenceNumber = "REIMB-2026-001",
            DepartmentCode = "DEP-ADM",
            Category = "Food allowance",
            Amount = 8000m,
            Status = "Paid",
            PaidAt = new DateTime(2026, 3, 25),
            CreatedAt = new DateTime(2026, 3, 22)
        });

        await context.SaveChangesAsync();

        var isDto = await service.GenerateIncomeStatementAsync(2026, "Year", 1);

        // Accrual Expenses = 12,000 + 25,000 + 8,000 = 45,000
        Assert.Equal(45000m, isDto.TotalAccrualExpenses);

        var fuelLine = isDto.Expenses.FirstOrDefault(e => e.LineItem == "Fuel");
        Assert.NotNull(fuelLine);
        Assert.Equal(12000m, fuelLine.AccrualAmount);

        var repairLine = isDto.Expenses.FirstOrDefault(e => e.LineItem == "Repairs and Maintenance");
        Assert.NotNull(repairLine);
        Assert.Equal(25000m, repairLine.AccrualAmount);

        var foodLine = isDto.Expenses.FirstOrDefault(e => e.LineItem == "Food allowance");
        Assert.NotNull(foodLine);
        Assert.Equal(8000m, foodLine.AccrualAmount);
    }

    [Fact]
    public async Task PayablesRecognition_ApprovedUnpaidItems()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Approved direct disbursement (unpaid) = 15,000
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DISB-002",
            DepartmentCode = "DEP-OPS",
            Category = "Fuel",
            FiscalYear = 2026,
            Amount = 15000m,
            Status = "Approved",
            CreatedAt = new DateTime(2026, 4, 1)
        });

        // Approved reimbursement (unpaid) = 7,500
        context.Reimbursements.Add(new Reimbursement
        {
            ReferenceNumber = "REIMB-002",
            DepartmentCode = "DEP-ADM",
            Category = "Toll",
            Amount = 7500m,
            Status = "Approved",
            CreatedAt = new DateTime(2026, 4, 2)
        });

        // Released disbursement (already paid - NOT payable)
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DISB-003",
            DepartmentCode = "DEP-OPS",
            Category = "Fuel",
            FiscalYear = 2026,
            Amount = 5000m,
            Status = "Released",
            CreatedAt = new DateTime(2026, 4, 3)
        });

        await context.SaveChangesAsync();

        var bsDto = await service.GenerateBalanceSheetAsync(2026, "Year", 1);

        // Accounts payable = 15,000 + 7,500 = 22,500
        Assert.Equal(22500m, bsDto.AccountsPayable);
        Assert.Equal(22500m, bsDto.TotalLiabilities);
    }

    [Fact]
    public async Task NetCashFormula_DirectMethod_MatchesExpectedInflowsAndOutflows()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Beginning cash = 100,000
        context.GoLiveOpeningBalances.AddRange(
            new GoLiveOpeningBalance { AccountCode = "101", AccountName = "Cash", Debit = 100000m, Credit = 0m },
            new GoLiveOpeningBalance { AccountCode = "301", AccountName = "Equity", Debit = 0m, Credit = 100000m }
        );

        // Inflow 1: Validated Client Payment = 80,000
        context.Payments.Add(new FOMS.Domain.Entities.Payment
        {
            OrNumber = "OR-010",
            InvoiceId = "INV-010",
            InvoiceNo = "INV-010",
            Amount = 80000m,
            PaymentDate = "2026-06-01",
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 6, 1)
        });

        // Inflow 2: Returned Advance Cash = 5,000
        context.CashInflows.Add(new CashInflow
        {
            ReferenceNumber = "RET-001",
            Source = "Returned Advance Cash Return",
            Amount = 5000m,
            Status = "Completed",
            TransactionDate = new DateTime(2026, 6, 2)
        });

        // Outflow 1: Released Cash Advance = 20,000
        context.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "ADV-001",
            AmountGiven = 20000m,
            Status = "Released",
            FiscalYear = 2026,
            ReleaseDate = new DateTime(2026, 6, 1),
            CreatedAt = new DateTime(2026, 6, 1)
        });

        // Outflow 2: Released Direct Disbursement = 15,000
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DISB-010",
            Amount = 15000m,
            Status = "Released",
            FiscalYear = 2026,
            ReleasedAt = new DateTime(2026, 6, 3),
            CreatedAt = new DateTime(2026, 6, 3)
        });

        // Outflow 3: Paid Reimbursement = 10,000
        context.Reimbursements.Add(new Reimbursement
        {
            ReferenceNumber = "REIMB-010",
            Amount = 10000m,
            Status = "Paid",
            PaidAt = new DateTime(2026, 6, 4),
            CreatedAt = new DateTime(2026, 6, 4)
        });

        await context.SaveChangesAsync();

        var cf = await service.GenerateCashFlowStatementAsync(2026, "Year", 1);

        // Inflows = 80,000 + 5,000 = 85,000
        Assert.Equal(80000m, cf.InflowsFromClientPayments);
        Assert.Equal(5000m, cf.InflowsFromReturnedAdvances);
        Assert.Equal(85000m, cf.TotalOperatingInflows);

        // Outflows = 20,000 + 15,000 + 10,000 = 45,000
        Assert.Equal(20000m, cf.OutflowsCashAdvancesReleased);
        Assert.Equal(15000m, cf.OutflowsDirectDisbursements);
        Assert.Equal(10000m, cf.OutflowsPaidReimbursements);
        Assert.Equal(45000m, cf.TotalOperatingOutflows);

        // Net Operating = 85,000 - 45,000 = 40,000
        Assert.Equal(40000m, cf.NetCashFromOperating);
        Assert.Equal(100000m, cf.BeginningCash);
        Assert.Equal(140000m, cf.EndingNetCash);
    }

    [Fact]
    public async Task BalanceSheet_AccountingEquation_AssetsEqualLiabilitiesPlusEquity()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Opening Balances: Cash 200,000, Equity 200,000
        context.GoLiveOpeningBalances.AddRange(
            new GoLiveOpeningBalance { AccountCode = "101", AccountName = "Cash", Debit = 200000m, Credit = 0m },
            new GoLiveOpeningBalance { AccountCode = "301", AccountName = "Equity", Debit = 0m, Credit = 200000m }
        );

        // Invoice Sent: Total 100,000 (Receivable 100,000, Accrual Revenue 100,000)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            InvoiceNo = "INV-100",
            ClientId = "CLI-100",
            ClientName = "Delta Trans",
            DocumentStatus = "Sent",
            BillingDate = "2026-07-01",
            TotalAmount = 100000m,
            Balance = 60000m // After 40k payment, 60k remaining receivable
        });

        // Validated Payment for 40,000 (Reduces AR to 60,000, Increases Cash by 40,000)
        context.Payments.Add(new FOMS.Domain.Entities.Payment
        {
            OrNumber = "OR-100",
            InvoiceId = "INV-100",
            InvoiceNo = "INV-100",
            Amount = 40000m,
            PaymentDate = "2026-07-05",
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 7, 5)
        });

        // Released Direct Disbursement of 30,000 (Outflow: Cash -30,000, Expense: 30,000)
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DISB-100",
            Category = "Fuel",
            FiscalYear = 2026,
            Amount = 30000m,
            Status = "Released",
            ReleasedAt = new DateTime(2026, 7, 10),
            CreatedAt = new DateTime(2026, 7, 10)
        });

        // Approved Reimbursement of 10,000 (Accounts Payable +10,000, Accrual Expense +10,000)
        context.Reimbursements.Add(new Reimbursement
        {
            ReferenceNumber = "REIMB-100",
            Category = "Toll",
            Amount = 10000m,
            Status = "Approved",
            CreatedAt = new DateTime(2026, 7, 12)
        });

        await context.SaveChangesAsync();

        var bs = await service.GenerateBalanceSheetAsync(2026, "Year", 1);

        // Assets = Cash (200k + 40k - 30k = 210,000) + AR (60,000) + Advances (0) = 270,000
        // Liabilities = Accounts Payable (10,000)
        // Net Income (Accrual) = Revenue (100k) - Expenses (30k + 10k = 40k) = 60,000
        // Ending Equity = 200,000 + 60,000 = 260,000
        // Total Liabilities & Equity = 10,000 + 260,000 = 270,000
        Assert.Equal(270000m, bs.TotalAssets);
        Assert.Equal(10000m, bs.TotalLiabilities);
        Assert.Equal(260000m, bs.EndingEquity);
        Assert.Equal(270000m, bs.TotalLiabilitiesAndEquity);
        Assert.True(bs.IsBalanced);
    }

    [Fact]
    public async Task NotesToFinancialStatements_ClientAndCategoryBreakdownsCalculated()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Invoices for 2 clients
        context.Invoices.AddRange(
            new FOMS.Domain.Entities.Invoice
            {
                InvoiceNo = "INV-N1",
                ClientId = "CLI-01",
                ClientName = "Client One",
                DocumentStatus = "Sent",
                BillingDate = "2026-08-01",
                TotalAmount = 60000m,
                Balance = 60000m
            },
            new FOMS.Domain.Entities.Invoice
            {
                InvoiceNo = "INV-N2",
                ClientId = "CLI-02",
                ClientName = "Client Two",
                DocumentStatus = "Sent",
                BillingDate = "2026-08-02",
                TotalAmount = 40000m,
                Balance = 40000m
            }
        );

        // Validated liquidations across categories
        context.Liquidations.AddRange(
            new Liquidation
            {
                CashAdvanceId = 1,
                Category = "Fuel",
                DepartmentCode = "DEP-OPS",
                TotalAmount = 30000m,
                Status = "Validated",
                FiscalYear = 2026,
                ValidatedAt = new DateTime(2026, 8, 5),
                CreatedAt = new DateTime(2026, 8, 5)
            },
            new Liquidation
            {
                CashAdvanceId = 2,
                Category = "Toll",
                DepartmentCode = "DEP-OPS",
                TotalAmount = 10000m,
                Status = "Validated",
                FiscalYear = 2026,
                ValidatedAt = new DateTime(2026, 8, 6),
                CreatedAt = new DateTime(2026, 8, 6)
            }
        );

        await context.SaveChangesAsync();

        var notes = await service.GenerateNotesAsync(2026, "Year", 1);

        // Revenue Note: Client One (60k = 60%), Client Two (40k = 40%)
        Assert.Equal(2, notes.RevenuePerClient.Count);
        var c1 = notes.RevenuePerClient.First(c => c.ClientId == "CLI-01");
        Assert.Equal(60000m, c1.AccrualAmount);
        Assert.Equal(60.0m, c1.PercentShare);

        // Expense Note: Fuel (30k = 75%), Toll (10k = 25%)
        Assert.Equal(2, notes.ExpensePerCategory.Count);
        var fuel = notes.ExpensePerCategory.First(c => c.Category == "Fuel");
        Assert.Equal(30000m, fuel.Amount);
        Assert.Equal(75.0m, fuel.PercentShare);
    }

    [Fact]
    public async Task AfsPackageWorkflow_FMReview_And_CeoApproval_Succeeds()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // 1. FM reviews package
        var pkgReviewed = await service.ReviewAfsPackageAsync(2026, "Year", 1, "EMP-001", "Finance Manager");
        Assert.Equal("Reviewed", pkgReviewed.Status);
        Assert.Equal("EMP-001", pkgReviewed.ReviewedBy);
        Assert.NotNull(pkgReviewed.ReviewedAt);

        // 2. Non-CEO attempt to approve annual package fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ApproveAfsPackageAsync(2026, "Year", 1, "EMP-001", "Finance Manager"));

        // 3. CEO approves annual package
        var pkgApproved = await service.ApproveAfsPackageAsync(2026, "Year", 1, "EMP-099", "CEO");
        Assert.Equal("Approved", pkgApproved.Status);
        Assert.Equal("EMP-099", pkgApproved.ApprovedBy);
        Assert.NotNull(pkgApproved.ApprovedAt);

        // 4. Attach Audited AFS as file only
        var attachDto = new AttachAuditedAfsDto
        {
            FiscalYear = 2026,
            PeriodType = "Year",
            FileUrl = "https://storage.azure.com/foms-docs/AFS_2026_Audited_Signed.pdf",
            FileName = "AFS_2026_Audited_Signed.pdf"
        };
        var pkgWithAudited = await service.AttachAuditedAfsAsync(attachDto, "EMP-001", "Finance Manager");
        Assert.Equal("https://storage.azure.com/foms-docs/AFS_2026_Audited_Signed.pdf", pkgWithAudited.AuditedAfsFileUrl);
        Assert.Equal("AFS_2026_Audited_Signed.pdf", pkgWithAudited.AuditedAfsFileName);
    }

    [Fact]
    public async Task AfsPackageExport_ExcelAndPdf_LogsToAudit()
    {
        using var context = CreateContext();
        var service = new FinancialStatementService(context);

        // Excel Export
        var excelBytes = await service.ExportAfsPackageToExcelAsync(2026, "Year", 1, "EMP-001");
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        // PDF Export
        var pdfBytes = await service.ExportAfsPackageToPdfAsync(2026, "Year", 1, "EMP-001");
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 0);

        // Verify audit logs
        var excelLog = await context.AuditLogs.FirstOrDefaultAsync(l => l.Action == "AFS_EXPORT_EXCEL");
        Assert.NotNull(excelLog);
        Assert.Contains("2026", excelLog.Details);

        var pdfLog = await context.AuditLogs.FirstOrDefaultAsync(l => l.Action == "AFS_EXPORT_PDF");
        Assert.NotNull(pdfLog);
        Assert.Contains("2026", pdfLog.Details);
    }
}
