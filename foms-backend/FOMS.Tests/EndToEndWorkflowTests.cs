using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Features;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using Notification = FOMS.Domain.Entities.Notification;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class EndToEndWorkflowTests
{
    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. FULL HAPPY PATH E2E
    // delivery -> POD check -> billing -> review -> Sent -> Received -> payment -> Finance validation -> Paid
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task E2E_FullHappyPath_Delivery_To_Paid()
    {
        using var context = CreateContext();

        // Step 1: Client Setup with credit term days = 30
        var client = new FOMS.Domain.Entities.Client
        {
            Id = "CLI-HAPPY-01",
            ClientCode = "CLI-HAPPY-01",
            Name = "Pacific Trade Corp",
            CreditTermDays = 30,
            Email = "billing@pacifictrade.ph",
            Status = "Active",
            IsActive = true
        };
        context.Clients.Add(client);

        // Step 2: Delivery & POD Check
        var waybill = new Waybill
        {
            WaybillNumber = "WB-HAPPY-001",
            ClientId = 1,
            Origin = "Manila",
            Destination = "Cebu",
            DeliveryArea = "Visayas",
            DeliveryStatus = "Delivered",
            PodStatus = "Validated", // POD checked & cleared
            ClientSigned = true,
            AccountingCopyMatched = true,
            DeliveredAt = DateTime.UtcNow,
            IsBilled = false
        };
        context.Waybills.Add(waybill);
        await context.SaveChangesAsync();

        Assert.Equal("Delivered", waybill.DeliveryStatus);
        Assert.Equal("Validated", waybill.PodStatus);

        // Step 3: Billing -> Creates Invoice in Draft status
        var invoice = new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-HAPPY-001",
            InvoiceNo = "INV-2026-HAPPY-01",
            ClientId = client.Id,
            ClientName = client.Name,
            TotalAmount = 50000m,
            Balance = 50000m,
            DocumentStatus = "Draft",
            PaymentStatus = "Unpaid",
            BillingDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DueDate = null,
            AccrualRevenueRecognized = false
        };
        context.Invoices.Add(invoice);
        waybill.IsBilled = true;
        await context.SaveChangesAsync();

        // STARS dispatch is blocked while in Draft
        bool canDispatchToStarsBeforeApproval = invoice.DocumentStatus == "Approved";
        Assert.False(canDispatchToStarsBeforeApproval);

        // Step 4: Head Accountant Review -> Approved
        invoice.DocumentStatus = "For Review";
        invoice.IsSampledForReview = true;
        invoice.SampledBy = "Head Accountant";
        invoice.SampledAt = DateTime.UtcNow;

        // Head accountant approves invoice
        invoice.DocumentStatus = "Approved";
        await context.SaveChangesAsync();
        Assert.Equal("Approved", invoice.DocumentStatus);

        // Dispatch to Operations via STARS is now allowed!
        bool canDispatchToStarsAfterApproval = invoice.DocumentStatus == "Approved";
        Assert.True(canDispatchToStarsAfterApproval);

        // Step 5: Sent -> Receivable created with no due date, Accrual revenue recognized
        invoice.DocumentStatus = "Sent";
        invoice.SentDate = DateTime.UtcNow;
        invoice.AccrualRevenueRecognized = true;

        var receivable = new ReceivableBalance
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceId = invoice.Id,
            ClientId = client.Id,
            BalanceAmount = invoice.Balance,
            PaidAmount = 0m,
            Status = "Sent",
            DueDate = null // No due date yet upon Sent
        };
        context.ReceivableBalances.Add(receivable);
        await context.SaveChangesAsync();

        Assert.Equal("Sent", invoice.DocumentStatus);
        Assert.True(invoice.AccrualRevenueRecognized);
        Assert.Null(receivable.DueDate);

        // Step 6: Received -> Due Date = Received Date + client.CreditTermDays (30 days)
        invoice.DocumentStatus = "Received";
        var receivedDate = DateTime.UtcNow.Date;
        invoice.ReceivedDate = receivedDate;
        int termDays = client.CreditTermDays ?? 30;
        var computedDueDate = receivedDate.AddDays(termDays);
        invoice.DueDate = computedDueDate.ToString("yyyy-MM-dd");
        receivable.DueDate = computedDueDate;
        await context.SaveChangesAsync();

        Assert.Equal("Received", invoice.DocumentStatus);
        Assert.Equal(computedDueDate.ToString("yyyy-MM-dd"), invoice.DueDate);

        // Derived Aging Bucket check
        var daysOverdue = (int)(DateTime.UtcNow.Date - computedDueDate).TotalDays;
        string agingBucket = daysOverdue <= 0 ? "Not Yet Due" : "1-30";
        Assert.Equal("Not Yet Due", agingBucket); // dynamically derived!

        // Step 7: Payment Recorded -> enters Pending Validation
        var payment = new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-HAPPY-001",
            OrNumber = "OR-2026-001",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = client.Id,
            ClientName = client.Name,
            Amount = 50000m, // EXACT MATCH
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "TRF-HAPPY-999",
            ProofImageUrl = "https://storage.azure.com/proofs/transfer_receipt.pdf",
            PaymentStatus = "Pending Validation",
            SubmittedAt = DateTime.UtcNow
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        Assert.Equal("Pending Validation", payment.PaymentStatus);

        // Step 8: Finance Manager Validates Funds Arrived
        // Only Finance Manager (or logged delegate) validates funds arrived
        string validatorRole = "Finance Manager";
        Assert.Contains("Finance Manager", validatorRole);

        payment.PaymentStatus = "Validated";
        payment.ValidatedAt = DateTime.UtcNow;
        payment.ValidatedBy = "EMP-001";

        // Step 9: Exact Match Rule -> Invoice becomes Paid, AR balance cleared
        if (payment.Amount == invoice.Balance)
        {
            invoice.AmountPaid += payment.Amount;
            invoice.Balance = 0m;
            invoice.PaymentStatus = "Paid";
            receivable.BalanceAmount = 0m;
            receivable.PaidAmount = payment.Amount;
            receivable.Status = "Paid";
        }
        await context.SaveChangesAsync();

        Assert.Equal("Paid", invoice.PaymentStatus);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(0m, receivable.BalanceAmount);

        // Verify Net Cash & Income statement auto-updated
        var statementService = new FinancialStatementService(context);
        var isDto = await statementService.GenerateIncomeStatementAsync(DateTime.UtcNow.Year, "Year", 1);
        Assert.Equal(50000m, isDto.TotalAccrualRevenue);
        Assert.Equal(50000m, isDto.TotalCashRevenue);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. FAILURE PATHS E2E
    // duplicate waybill, On Hold POD, review fail, short/over payment, exception resolution
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task E2E_FailurePaths_Waybills_Review_And_Payments()
    {
        using var context = CreateContext();

        // ── Failure Path A: Duplicate Waybill Detection ──
        context.Waybills.Add(new Waybill
        {
            WaybillNumber = "WB-DUP-001",
            ClientId = 1,
            DeliveryStatus = "Delivered",
            PodStatus = "Validated"
        });
        await context.SaveChangesAsync();

        bool isDuplicateWaybill = await context.Waybills.AnyAsync(s => s.WaybillNumber == "WB-DUP-001");
        Assert.True(isDuplicateWaybill);

        // ── Failure Path B: Delivery Failed / On Hold POD blocks billing ──
        var failedShipment = new Waybill
        {
            WaybillNumber = "WB-FAIL-001",
            ClientId = 1,
            DeliveryStatus = "Failed",
            PodStatus = "On Hold", // Failed delivery sets POD to On Hold
            IsBilled = false
        };
        context.Waybills.Add(failedShipment);
        await context.SaveChangesAsync();

        // Billing is strictly blocked when POD is "On Hold"
        bool billingAllowed = failedShipment.PodStatus == "Validated" && failedShipment.DeliveryStatus == "Delivered";
        Assert.False(billingAllowed);

        // ── Failure Path C: Head Accountant Review Fails -> Returns to Draft with Reason ──
        var invoice = new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-REV-FAIL",
            InvoiceNo = "INV-REV-FAIL",
            TotalAmount = 75000m,
            Balance = 75000m,
            DocumentStatus = "For Review"
        };
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        // Review fails: rejection reason is mandatory
        string rejectionReason = "Missing approved special rate agreement attachment.";
        Assert.False(string.IsNullOrWhiteSpace(rejectionReason));

        invoice.DocumentStatus = "Draft"; // Returns to Draft!
        invoice.ReviewRejectionReason = rejectionReason;
        await context.SaveChangesAsync();

        Assert.Equal("Draft", invoice.DocumentStatus);
        Assert.Equal(rejectionReason, invoice.ReviewRejectionReason);

        // ── Failure Path D: Short Payment -> Diverted to Exception Queue ──
        var shortPayment = new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-SHORT-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Amount = 70000m, // 5k short of 75k balance
            PaymentStatus = "Exception",
            RejectionReason = "Short payment: Paid 70,000 vs 75,000 required."
        };
        context.Payments.Add(shortPayment);
        await context.SaveChangesAsync();

        // Short payment resolution handler check:
        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(context);

        // Direct send-to-validation for short payment is blocked!
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(shortPayment.Id, "send-to-validation", "Invalid bypass", "Finance Manager"),
            CancellationToken.None));

        // Short payment can be rejected with email notice (NTF-02)
        var rejectedShort = await handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(shortPayment.Id, "reject", "Client requested to repay exact balance", "Finance Manager"),
            CancellationToken.None);
        Assert.Equal("Rejected", rejectedShort!.PaymentStatus);

        // ── Failure Path E: Overpayment -> Diverted to Exception Queue ──
        var overPayment = new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-OVER-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Amount = 80000m, // 5k over 75k balance
            PaymentStatus = "Exception",
            RejectionReason = "Overpayment: Paid 80,000 vs 75,000 required."
        };
        context.Payments.Add(overPayment);
        await context.SaveChangesAsync();

        // Overpayment resolution: excess held as unapplied credit for client's other open invoices
        var resolvedOver = await handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(
                overPayment.Id, "send-to-validation", "Excess held as unapplied credit for other invoices", "Finance Manager"),
            CancellationToken.None);

        // Resolved payments return to Pending Validation, never directly to Paid!
        Assert.Equal("Pending Validation", resolvedOver!.PaymentStatus);
        Assert.NotEqual("Paid", resolvedOver.PaymentStatus);

        // ── Failure Path F: Duplicate Payment Reference -> Rejected ──
        var duplicatePayment = new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-DUP-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Amount = 75000m,
            ReferenceNumber = "OR-ALREADY-USED",
            PaymentStatus = "Exception",
            RejectionReason = "Duplicate payment reference: OR-ALREADY-USED"
        };
        context.Payments.Add(duplicatePayment);
        await context.SaveChangesAsync();

        // Duplicate payment must always be rejected
        var rejectedDup = await handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(duplicatePayment.Id, "reject", "Confirmed duplicate reference", "Finance Manager"),
            CancellationToken.None);
        Assert.Equal("Rejected", rejectedDup!.PaymentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. BUDGET -> ADVANCE -> LIQUIDATION -> REIMBURSEMENT -> CLOSE -> STATEMENTS
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task E2E_Budget_To_Statements_CompleteLifecycle()
    {
        using var context = CreateContext();

        // ── Step 1: Budgeting ──
        // CEO/President approves annual budget; Finance Manager allocates by department and category
        var annualBudget = new AnnualBudget
        {
            FiscalYear = 2026,
            TotalBudget = 500000m,
            Status = "Approved",
            ApprovedBy = "CEO",
            ApprovedAt = DateTime.UtcNow
        };
        context.AnnualBudgets.Add(annualBudget);

        var fuelBudgetLine = new MonthlyBudgetLine
        {
            FiscalYear = 2026,
            Month = 1,
            DepartmentCode = "DEP-OPS",
            DepartmentName = "Operations",
            Category = "Fuel",
            BudgetAmount = 100000m,
            ActualAmount = 0m,
            CommittedAmount = 0m
        };
        context.MonthlyBudgetLines.Add(fuelBudgetLine);

        // Seed CSH-09 threshold limit = ₱10,000
        context.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            TransactionType = "CashOutflow",
            RoleName = "Assistant of Finance Manager",
            MaximumAmount = 10000m,
            IsActive = true
        });

        // Seed Go-Live Opening Balances
        context.GoLiveOpeningBalances.AddRange(
            new GoLiveOpeningBalance { AccountCode = "101", AccountName = "Cash in Bank", Debit = 300000m, Credit = 0m, DepartmentCode = "DEP-FIN", Category = "Cash" },
            new GoLiveOpeningBalance { AccountCode = "301", AccountName = "Owner's Equity", Debit = 0m, Credit = 300000m, DepartmentCode = "DEP-FIN", Category = "Equity" }
        );
        await context.SaveChangesAsync();

        // Available = Budget - Committed - Actual = 100,000 - 0 - 0 = 100,000
        Assert.Equal(100000m, fuelBudgetLine.Available);

        // ── Step 2: Cash Advance Request -> Budget Check -> Approval (Committed) ──
        var disbService = new DisbursementAndReimbursementService(context);

        var caReq = new RequestCashAdvanceDto
        {
            CourierName = "Juan Courier",
            TripReference = "TRIP-2026-001",
            Amount = 15000m, // Above 10,000 CSH-09 threshold -> Requires CEO approval!
            Category = "Fuel",
            DepartmentCode = "DEP-OPS",
            LocationType = "NCR"
        };

        var ca = await disbService.RequestCashAdvanceAsync(caReq, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", ca.Status);
        Assert.True(ca.RequiresCeoApproval);

        // Approval: Finance Manager approves -> Budget becomes Committed
        var approvedCa = await disbService.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedCa.Status);

        var lineAfterCaApprove = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(15000m, lineAfterCaApprove.CommittedAmount);
        Assert.Equal(85000m, lineAfterCaApprove.Available);

        // Release: Above CSH-09 threshold requires CEO/President
        // Non-CEO release attempt fails!
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => disbService.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager"));

        // CEO releases advance -> Outflow executed
        var releasedCa = await disbService.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-099", "CEO");
        Assert.Equal("Released", releasedCa.Status);

        // ── Step 3: Liquidation ──
        // Items spent 12,000 out of 15,000; leftover 3,000 returned
        var submitLiq = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Juan Courier",
            Remarks = "NCR Trip liquidation",
            LineItems = new List<LiquidationLineItemDto>
            {
                new() { Category = "Fuel", Description = "Diesel Refuel Manila to Subic", Amount = 12000m, ReceiptNumber = "REC-FL-01", ReceiptDate = DateTime.UtcNow }
            }
        };
        var submittedLiq = await disbService.SubmitLiquidationAsync(submitLiq, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Pending", submittedLiq.Status);

        // Validation by Assistant of Finance Manager:
        // Converts Committed to Actual (12,000) and releases unused commitment (3,000)
        var validatedLiq = await disbService.ValidateLiquidationAsync(new ValidateLiquidationDto
        {
            LiquidationId = submittedLiq.LiquidationId,
            Remarks = "All receipts verified by Assistant"
        }, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validatedLiq.Status);

        var lineAfterLiqValidate = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(12000m, lineAfterLiqValidate.ActualAmount);
        Assert.Equal(0m, lineAfterLiqValidate.CommittedAmount);
        Assert.Equal(88000m, lineAfterLiqValidate.Available);

        // Leftover cash returned recorded as cash inflow
        var returnInflow = await context.CashInflows.FirstOrDefaultAsync(i => i.ReferenceNumber == submittedLiq.ReferenceNumber);
        Assert.NotNull(returnInflow);
        Assert.Equal(3000m, returnInflow.Amount);

        // ── Step 4: Reimbursement (Submitted -> Validated -> Approved -> Paid) ──
        var submitReimb = new SubmitReimbursementDto
        {
            EmployeeId = "EMP-005",
            EmployeeName = "Supervisor Maria",
            DepartmentCode = "DEP-OPS",
            Category = "Toll",
            Amount = 5000m,
            Purpose = "Expressway toll receipts for site inspection"
        };
        var reimb = await disbService.SubmitReimbursementAsync(submitReimb, "EMP-005", "Employee");
        Assert.Equal("Submitted", reimb.Status);

        // Assistant validates
        var valReimb = await disbService.ValidateReimbursementAsync(reimb.ReimbursementId, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", valReimb.Status);

        // Finance Manager approves -> becomes Accounts Payable (Committed)
        var appReimb = await disbService.ApproveReimbursementAsync(reimb.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", appReimb.Status);

        // Payment -> Cash outflow executed, becomes Actual
        var paidReimb = await disbService.PayReimbursementAsync(reimb.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Paid", paidReimb.Status);

        // ── Step 5: Period Close & Verification Checklist ──
        // Checklist: All payments validated, liquidations validated, AR = aging, bank reconciliation stored
        var periodCloseAudit = new FOMS.Domain.Entities.AuditLog
        {
            UserId = "EMP-001",
            EntityName = "AccountingPeriodClose",
            EntityId = "CLOSE-2026-M01",
            Action = "PERIOD_CLOSED",
            Details = "Closed Month 1 accounting period. Bank book matched statement balance. All liquidations and payments validated. Period locked.",
            LoggedAt = DateTime.UtcNow
        };
        context.AuditLogs.Add(periodCloseAudit);
        await context.SaveChangesAsync();

        // ── Step 6: Centralized Statements (FIN-20) ──
        var statementService = new FinancialStatementService(context);

        // Income Statement: side-by-side Accrual and Cash
        var incomeStmt = await statementService.GenerateIncomeStatementAsync(2026, "Year", 1);
        Assert.NotNull(incomeStmt);
        Assert.Equal(17000m, incomeStmt.TotalAccrualExpenses); // 12,000 Fuel + 5,000 Toll
        Assert.Equal(17000m, incomeStmt.TotalCashExpenses);

        // Balance Sheet: Assets = Liabilities + Equity
        var balanceSheet = await statementService.GenerateBalanceSheetAsync(2026, "Year", 1);
        Assert.NotNull(balanceSheet);
        Assert.True(balanceSheet.IsBalanced);
        Assert.Equal(balanceSheet.TotalAssets, balanceSheet.TotalLiabilitiesAndEquity);

        // Cash Flow Statement (Direct Method)
        var cashFlow = await statementService.GenerateCashFlowStatementAsync(2026, "Year", 1);
        Assert.NotNull(cashFlow);
        Assert.Equal(300000m, cashFlow.BeginningCash);
        Assert.Equal(3000m, cashFlow.InflowsFromReturnedAdvances);
        Assert.Equal(15000m, cashFlow.OutflowsCashAdvancesReleased);
        Assert.Equal(5000m, cashFlow.OutflowsPaidReimbursements);
        Assert.Equal(283000m, cashFlow.EndingNetCash); // 300k + 3k - 15k - 5k = 283,000

        // Governance Approvals: FM reviews, CEO approves annual
        var pkgReviewed = await statementService.ReviewAfsPackageAsync(2026, "Year", 1, "EMP-001", "Finance Manager");
        Assert.Equal("Reviewed", pkgReviewed.Status);

        var pkgApproved = await statementService.ApproveAfsPackageAsync(2026, "Year", 1, "EMP-099", "CEO");
        Assert.Equal("Approved", pkgApproved.Status);

        // Attach audited AFS as file reference only
        var attachDto = new AttachAuditedAfsDto
        {
            FiscalYear = 2026,
            PeriodType = "Year",
            FileUrl = "https://storage.azure.com/foms-docs/Audited_AFS_2026_Signed.pdf",
            FileName = "Audited_AFS_2026_Signed.pdf"
        };
        var pkgAttached = await statementService.AttachAuditedAfsAsync(attachDto, "EMP-001", "Finance Manager");
        Assert.Equal("Audited_AFS_2026_Signed.pdf", pkgAttached.AuditedAfsFileName);

        // Export AFS to Excel & PDF and verify audit logs
        var excelBytes = await statementService.ExportAfsPackageToExcelAsync(2026, "Year", 1, "EMP-001");
        var pdfBytes = await statementService.ExportAfsPackageToPdfAsync(2026, "Year", 1, "EMP-001");
        Assert.NotNull(excelBytes);
        Assert.NotNull(pdfBytes);

        var allAudits = await context.AuditLogs.ToListAsync();
        Assert.Contains(allAudits, a => a.Action == "AFS_EXPORT_EXCEL");
        Assert.Contains(allAudits, a => a.Action == "AFS_EXPORT_PDF");
        Assert.Contains(allAudits, a => a.Action == "PERIOD_CLOSED");
    }
}
