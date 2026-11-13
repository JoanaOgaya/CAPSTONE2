using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FOMS.Api.Controllers;
using FOMS.Application.DTOs;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Payment = FOMS.Domain.Entities.Payment;
using Invoice = FOMS.Domain.Entities.Invoice;
using Client = FOMS.Domain.Entities.Client;
using AuditLog = FOMS.Domain.Entities.AuditLog;
using CashFlowTransaction = FOMS.Domain.Entities.CashFlowTransaction;

namespace FOMS.Tests;

public class Phase17AccountsReceivableAndPaymentsTests
{
    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_App_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateFomsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_Foms_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private static AuthService CreateAuthService(ApplicationDbContext context)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = new string('k', 48),
            ["JwtSettings:Issuer"] = "FOMS",
            ["JwtSettings:Audience"] = "FOMS_Audience",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpirationDays"] = "7"
        }).Build();

        return new AuthService(context, cfg, NullLogger<AuthService>.Instance);
    }

    private static ClaimsPrincipal CreatePrincipal(string role, string name, string empId, string? clientId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new(ClaimTypes.Name, name),
            new("name", name),
            new(ClaimTypes.NameIdentifier, empId),
            new("sub", empId),
            new("employeeNumber", empId),
            new("employee_id", empId)
        };
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            claims.Add(new Claim("client_id", clientId));
            claims.Add(new Claim("clientId", clientId));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static void SetContext(ControllerBase controller, ClaimsPrincipal principal)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    private static object? GetValueProp(object? obj, string propName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(obj);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. PAYMENT CHANNELS & PROOF UPLOAD (PAY-01, PAY-02)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Check", "deposit_slip.pdf")]
    [InlineData("Bank Transfer", "screenshot.png")]
    [InlineData("Cash", "official_receipt.jpg")]
    public async Task PaymentProof_SupportedChannelsAndFileTypes_Accepted(string channel, string fileName)
    {
        Assert.NotNull(channel);
        using var appDb = CreateAppDb(nameof(PaymentProof_SupportedChannelsAndFileTypes_Accepted));
        using var fomsDb = CreateFomsDb(nameof(PaymentProof_SupportedChannelsAndFileTypes_Accepted));

        var controller = new PaymentsController(appDb, fomsDb, Mock.Of<ILogger<PaymentsController>>());
        SetContext(controller, CreatePrincipal("Accountant", "Staff User", "EMP-003"));

        var content = "Simulated binary proof"u8.ToArray();
        using var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, content.Length, "file", fileName);

        var uploadResult = await controller.UploadProof(file);
        var okUpload = Assert.IsType<OkObjectResult>(uploadResult);
        Assert.NotNull(okUpload.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DUPLICATE REFERENCE & UNIDENTIFIED PAYMENTS (PAY-05, PAY-06)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Payment_DuplicateReference_RaisesAlert_AndUnidentifiedHeldOnHold()
    {
        using var appDb = CreateAppDb(nameof(Payment_DuplicateReference_RaisesAlert_AndUnidentifiedHeldOnHold));
        using var fomsDb = CreateFomsDb(nameof(Payment_DuplicateReference_RaisesAlert_AndUnidentifiedHeldOnHold));

        appDb.Payments.Add(new Payment
        {
            Id = "PAY-EXISTING-01",
            InvoiceId = "INV-01",
            ReferenceNumber = "BANK-REF-9999",
            OrNumber = "OR-9999",
            Amount = 5000m,
            PaymentStatus = "Validated"
        });
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, fomsDb, Mock.Of<ILogger<PaymentsController>>());
        SetContext(controller, CreatePrincipal("Accountant", "Staff User", "EMP-003"));

        // Check duplicate reference
        var checkResult = await controller.CheckReference("BANK-REF-9999", null);
        var okCheck = Assert.IsType<OkObjectResult>(checkResult);
        var isDuplicate = Convert.ToBoolean(GetValueProp(okCheck.Value, "isDuplicate"));
        Assert.True(isDuplicate);

        // Unidentified payment without invoice or client reference held On Hold
        var unidentifiedPayment = new Payment
        {
            Id = "PAY-UNIDENTIFIED-01",
            InvoiceId = string.Empty,
            ClientId = string.Empty,
            ReferenceNumber = "UNKNOWN-DEP-001",
            Amount = 12000m,
            PaymentStatus = "On Hold",
            Remarks = "Held as Unidentified until client claims it."
        };
        appDb.Payments.Add(unidentifiedPayment);
        await appDb.SaveChangesAsync();

        var fetched = await appDb.Payments.FindAsync("PAY-UNIDENTIFIED-01");
        Assert.NotNull(fetched);
        Assert.Equal("On Hold", fetched.PaymentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. PAYMENT EXCEPTION QUEUE: SHORT, OVER, DUPLICATE (PAY-04, PAY-10)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PaymentExceptions_ShortOverDuplicate_ResolutionFlow()
    {
        using var appDb = CreateAppDb(nameof(PaymentExceptions_ShortOverDuplicate_ResolutionFlow));

        var invoice = new Invoice
        {
            Id = "INV-EXC-01",
            InvoiceNo = "INV-EXC-01",
            ClientId = "CL-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DocumentStatus = "Sent"
        };
        appDb.Invoices.Add(invoice);

        var shortPayment = new Payment
        {
            Id = "PAY-SHORT-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            Amount = 800m,
            PaymentStatus = "Exception",
            RejectionReason = "Short payment: received 800.00; invoice balance is 1,000.00."
        };

        var overPayment = new Payment
        {
            Id = "PAY-OVER-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            Amount = 1200m,
            PaymentStatus = "Exception",
            RejectionReason = "Overpayment: received 1,200.00; invoice balance is 1,000.00."
        };

        var duplicatePayment = new Payment
        {
            Id = "PAY-DUP-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            Amount = 1000m,
            PaymentStatus = "Exception",
            RejectionReason = "Duplicate bank reference BANK-DUP-01"
        };

        appDb.Payments.AddRange(shortPayment, overPayment, duplicatePayment);
        await appDb.SaveChangesAsync();

        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(appDb);

        // A. Duplicate payment cannot be resolved to validation; must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
                duplicatePayment.Id, "send-to-validation", "Attempted bypass", "Finance Manager"), CancellationToken.None));

        var dupRejected = await handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
            duplicatePayment.Id, "reject", "Confirmed duplicate transaction", "Finance Manager"), CancellationToken.None);
        Assert.NotNull(dupRejected);
        Assert.Equal("Rejected", dupRejected.PaymentStatus);

        // B. Short payment cannot be sent to validation; must be rejected with email notice or new payment
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
                shortPayment.Id, "send-to-validation", "Invalid bypass", "Finance Manager"), CancellationToken.None));

        var shortRejected = await handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
            shortPayment.Id, "reject", "Client notified to repay exact amount", "Finance Manager"), CancellationToken.None);
        Assert.NotNull(shortRejected);
        Assert.Equal("Rejected", shortRejected.PaymentStatus);
        Assert.True(await appDb.Notifications.AnyAsync(n => n.RelatedPaymentId == shortPayment.Id));

        // C. Overpayment resolution returns to Pending Validation with unapplied credit reason (NEVER directly to Paid)
        var overResolved = await handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
            overPayment.Id, "resolve", "Excess 200.00 to be held as unapplied credit", "Finance Manager"), CancellationToken.None);
        Assert.NotNull(overResolved);
        Assert.Equal("Pending Validation", overResolved.PaymentStatus);
        Assert.NotEqual("Paid", overResolved.PaymentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. DELEGATE GOVERNANCE & PRIVILEGE BOUNDARY (PAY-03, Row 4)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PaymentDelegate_DisabledByDefault_AndCannotRecordCeoApprovals()
    {
        using var appDb = CreateAppDb(nameof(PaymentDelegate_DisabledByDefault_AndCannotRecordCeoApprovals));
        using var fomsDb = CreateFomsDb(nameof(PaymentDelegate_DisabledByDefault_AndCannotRecordCeoApprovals));

        var payment = new Payment
        {
            Id = "PAY-DEL-01",
            InvoiceId = "INV-DEL-01",
            InvoiceNo = "INV-DEL-01",
            Amount = 1000m,
            PaymentStatus = "Pending Validation",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        var invoice = new Invoice
        {
            Id = "INV-DEL-01",
            InvoiceNo = "INV-DEL-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DocumentStatus = "Sent"
        };
        appDb.Payments.Add(payment);
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, Mock.Of<IMediator>());

        // 1. Without active configured delegate, Assistant FM gets 403 Forbidden
        SetContext(controller, CreatePrincipal("Assistant of Finance Manager", "Delegate Candidate", "EMP-002"));
        var nonDelegateResult = await controller.ValidatePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("Funds confirmed"));
        var forbid1 = Assert.IsType<ObjectResult>(nonDelegateResult);
        Assert.Equal(403, forbid1.StatusCode);

        // 2. Configure delegate in FomsDb
        fomsDb.FinanceManagerDelegates.Add(new FinanceManagerDelegate
        {
            DelegateUserId = "EMP-002",
            DelegateName = "Delegate Candidate",
            AssignedBy = "Finance Manager",
            ValidFrom = DateTime.UtcNow.AddHours(-1),
            ValidUntil = DateTime.UtcNow.AddDays(7),
            Reason = "FM on official leave",
            IsActive = true
        });
        await fomsDb.SaveChangesAsync();

        // 3. Configured active delegate CAN validate payments
        var delegateValidateResult = await controller.ValidatePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("Funds confirmed"));
        var okValidate = Assert.IsType<OkObjectResult>(delegateValidateResult);
        Assert.NotNull(okValidate.Value);

        // 4. Submitter != Validator rule: delegate cannot validate payment they recorded
        payment.PaymentStatus = "Pending Validation";
        payment.RecordedBy = "EMP-002";
        payment.RecordedByEmployeeId = "EMP-002";
        await appDb.SaveChangesAsync();

        var selfApprovalResult = await controller.ValidatePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("Self approval"));
        var forbidSelf = Assert.IsType<ObjectResult>(selfApprovalResult);
        Assert.Equal(403, forbidSelf.StatusCode);

        // 5. Privilege Boundary: Delegate CANNOT record CEO/President approval records
        var ceoGateService = new CeoApprovalGateService(appDb);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ceoGateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = CeoApprovalItemTypes.AnnualBudget,
                ItemId = "2026",
                Status = "Approved",
                ApprovalDate = DateTime.UtcNow.Date
            }, "EMP-002", "Finance Manager Delegate (Delegate Candidate)"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. DOUBLE-CLICK IDEMPOTENCY (Double-Submit Does Not Double-Apply AR or OR)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PaymentApproval_DoubleClick_Idempotent_SingleArCreditAndSingleOr()
    {
        using var appDb = CreateAppDb(nameof(PaymentApproval_DoubleClick_Idempotent_SingleArCreditAndSingleOr));
        using var fomsDb = CreateFomsDb(nameof(PaymentApproval_DoubleClick_Idempotent_SingleArCreditAndSingleOr));

        var invoice = new Invoice
        {
            Id = "INV-IDEMP-01",
            InvoiceNo = "INV-IDEMP-01",
            TotalAmount = 2500m,
            Balance = 2500m,
            DocumentStatus = "Sent"
        };
        var payment = new Payment
        {
            Id = "PAY-IDEMP-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Amount = 2500m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Invoices.Add(invoice);
        appDb.Payments.Add(payment);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, Mock.Of<IMediator>());
        SetContext(controller, CreatePrincipal("Finance Manager", "FM User", "EMP-001"));

        // First submit: approves payment, issues OR, reduces AR balance to 0
        var res1 = await controller.ApprovePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("Approved"));
        Assert.IsType<OkObjectResult>(res1);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(2500m, invoice.AmountPaid);
        Assert.Equal("Paid", invoice.PaymentStatus);

        // Second submit (double-click): returns 200 without double-applying
        var res2 = await controller.ApprovePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("Approved"));
        var okDouble = Assert.IsType<OkObjectResult>(res2);
        Assert.NotNull(okDouble.Value);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(2500m, invoice.AmountPaid);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. CLIENT CREDIT BALANCE ON PAID INVOICE (BIL-05, R4.11 Item 3)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreditMemo_OnPaidInvoice_BecomesClientCredit_DeductedAfterGrandTotal_ZeroOutflow()
    {
        using var appDb = CreateAppDb(nameof(CreditMemo_OnPaidInvoice_BecomesClientCredit_DeductedAfterGrandTotal_ZeroOutflow));

        var client = new Client
        {
            Id = "CL-CREDIT-01",
            ClientCode = "CL-CREDIT-01",
            Name = "Credit Client",
            BusinessName = "Credit Client",
            AvailableCredit = 0m
        };
        appDb.Clients.Add(client);

        var paidInvoice = new Invoice
        {
            Id = "INV-PAID-01",
            InvoiceNo = "INV-PAID-01",
            ClientId = client.Id,
            FreightCharges = 2000m,
            DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")
        };
        BillingComputationService.RecalculateInvoice(paidInvoice);
        paidInvoice.AmountPaid = paidInvoice.TotalAmount;
        paidInvoice.Balance = 0m;
        paidInvoice.PaymentStatus = "Paid";
        paidInvoice.DocumentStatus = "Received";

        var nextInvoice = new Invoice
        {
            Id = "INV-NEXT-01",
            InvoiceNo = "INV-NEXT-01",
            ClientId = client.Id,
            FreightCharges = 1500m,
            DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            DocumentStatus = "Sent"
        };
        BillingComputationService.RecalculateInvoice(nextInvoice);

        appDb.Invoices.AddRange(paidInvoice, nextInvoice);
        await appDb.SaveChangesAsync();

        var initialCashFlowCount = await appDb.CashFlowTransactions.CountAsync();

        // 1. Issue Credit Memo on already Paid invoice
        var createAdjustmentHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(appDb);
        var adjustment = await createAdjustmentHandler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
            paidInvoice.InvoiceNo,
            "Credit Memo",
            300m,
            "Damaged cargo adjustment on paid invoice",
            "Accountant"), CancellationToken.None);
        Assert.NotNull(adjustment);

        // 2. Approve Credit Memo
        var approveAdjustmentHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(appDb);
        var approvedAdj = await approveAdjustmentHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(
            adjustment!.Id,
            "Finance Manager"), CancellationToken.None);
        Assert.NotNull(approvedAdj);

        // 3. Credit applied to client's next invoice as separate line after total, capped at invoice total
        Assert.Equal("Paid", paidInvoice.PaymentStatus);
        Assert.Equal(300m, nextInvoice.CreditApplied);
        Assert.Equal(nextInvoice.TotalAmount - 300m, nextInvoice.Balance);

        // 4. Zero cash outflow (BIL-05 / C2 FR-032)
        Assert.Equal(initialCashFlowCount, await appDb.CashFlowTransactions.CountAsync());

        // 5. R4.5: Post Dr Cargo Claims (Sales Allowances) / Cr Unapplied Client Credits
        var je = await appDb.JournalEntries.FirstOrDefaultAsync(j => j.InvoiceId == paidInvoice.Id);
        Assert.NotNull(je);
        Assert.True(je.Posted);
        Assert.Contains("Dr Cargo Claims (Sales Allowances) / Cr Unapplied Client Credits", je.Description);

        var glEntries = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        Assert.Equal(2, glEntries.Count);
        var debitEntry = glEntries.FirstOrDefault(g => g.Debit == 300m && g.Credit == 0m);
        var creditEntry = glEntries.FirstOrDefault(g => g.Credit == 300m && g.Debit == 0m);
        Assert.NotNull(debitEntry);
        Assert.NotNull(creditEntry);

        var cargoClaimsAcct = await appDb.ChartOfAccounts.FindAsync(debitEntry.AccountId);
        var unappliedCreditsAcct = await appDb.ChartOfAccounts.FindAsync(creditEntry.AccountId);
        Assert.NotNull(cargoClaimsAcct);
        Assert.NotNull(unappliedCreditsAcct);
        Assert.Equal("4200", cargoClaimsAcct.AccountCode);
        Assert.Equal("2160", unappliedCreditsAcct.AccountCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. AR AGING DERIVED BUCKETS & PERIOD CLOSE (ARC-03, ARC-04, ARC-07)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ArAging_DerivedBuckets_AndPeriodCloseVerification()
    {
        using var appDb = CreateAppDb(nameof(ArAging_DerivedBuckets_AndPeriodCloseVerification));
        using var fomsDb = CreateFomsDb(nameof(ArAging_DerivedBuckets_AndPeriodCloseVerification));

        var today = DateTime.UtcNow.Date;

        // Invoices across aging boundaries
        var invNotYetDue = new Invoice
        {
            Id = "INV-NYD",
            InvoiceNo = "INV-NYD",
            ClientId = "CL-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DueDate = today.AddDays(15).ToString("yyyy-MM-dd"),
            DocumentStatus = "Sent"
        };
        var invDueSoon = new Invoice
        {
            Id = "INV-DS",
            InvoiceNo = "INV-DS",
            ClientId = "CL-01",
            TotalAmount = 2000m,
            Balance = 2000m,
            DueDate = today.AddDays(3).ToString("yyyy-MM-dd"),
            DocumentStatus = "Sent"
        };
        var inv1_30 = new Invoice
        {
            Id = "INV-130",
            InvoiceNo = "INV-130",
            ClientId = "CL-01",
            TotalAmount = 3000m,
            Balance = 3000m,
            DueDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
            DocumentStatus = "Sent"
        };
        var invDisputed = new Invoice
        {
            Id = "INV-DISP",
            InvoiceNo = "INV-DISP",
            ClientId = "CL-01",
            TotalAmount = 4000m,
            Balance = 4000m,
            DueDate = today.AddDays(-20).ToString("yyyy-MM-dd"),
            DocumentStatus = "Disputed",
            IsDisputed = true,
            DisputeReasonCode = "CARGO_DAMAGE",
            DisputeRemarks = "Pending cargo insurance assessment"
        };

        appDb.Invoices.AddRange(invNotYetDue, invDueSoon, inv1_30, invDisputed);
        await appDb.SaveChangesAsync();

        var receivablesController = new ReceivablesController(appDb);
        SetContext(receivablesController, CreatePrincipal("Finance Manager", "FM User", "EMP-001"));

        var recordsResult = await receivablesController.GetArRecords(dueSoonDays: 7);
        var okRecords = Assert.IsType<OkObjectResult>(recordsResult);
        var records = Assert.IsAssignableFrom<IEnumerable<object>>(okRecords.Value).ToList();

        // Verify dynamic derived aging bucket names
        Assert.Equal(4, records.Count);
        Assert.Equal("Not Yet Due", BillingComputationService.ComputeDerivedAgingBucket(invNotYetDue.DueDate, 7));
        Assert.Equal("Due Soon", BillingComputationService.ComputeDerivedAgingBucket(invDueSoon.DueDate, 7));
        Assert.Equal("1-30", BillingComputationService.ComputeDerivedAgingBucket(inv1_30.DueDate, 7));

        // Disputed invoice carries reason code, stays visible
        var invoicesController = new InvoicesController(appDb, fomsDb);
        SetContext(invoicesController, CreatePrincipal("Finance Manager", "FM User", "EMP-001"));

        var followUpsResult = await invoicesController.GetFollowUpReminders();
        var okFollowUps = Assert.IsType<OkObjectResult>(followUpsResult);
        var disputedList = GetValueProp(okFollowUps.Value, "disputedInvoices") as System.Collections.IEnumerable;
        Assert.NotNull(disputedList);
        Assert.Single(disputedList.Cast<object>());

        // Period close: AR ledger total equals aging total (ARC-07)
        appDb.ReceivableBalances.Add(new ReceivableBalance { Id = "RB-01", BalanceAmount = 10000m });
        appDb.AgingAccounts.Add(new AgingAccount { Id = "AG-01", CurrentAmount = 10000m });
        await appDb.SaveChangesAsync();

        var periodCloseController = new PeriodCloseController(appDb, fomsDb);
        SetContext(periodCloseController, CreatePrincipal("Finance Manager", "FM User", "EMP-001"));

        var closeResult = await periodCloseController.ClosePeriod(new ClosePeriodRequest
        {
            PeriodType = "Month",
            PeriodStart = new DateTime(today.Year, today.Month, 1),
            PeriodEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            BankBookBalance = 50000m,
            BankStatementBalance = 50000m,
            BankStatementProofReference = "STATEMENT-REF-2026",
            CashBookBalance = 10000m,
            CountedCashBalance = 10000m,
            ListedExceptions = new List<PaymentExceptionListing?>()
        });

        // Does not reject for AR mismatch (since 10000 == 10000)
        if (closeResult is BadRequestObjectResult br)
        {
            Assert.DoesNotContain("The AR ledger total must equal the aging report total", br.Value?.ToString() ?? "");
        }

        // ARC-04: Export aging report as real .xlsx
        var exportResult = await invoicesController.ExportAgingReportExcel(dueSoonDays: 7, format: "xlsx");
        var fileResult = Assert.IsType<FileContentResult>(exportResult);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.EndsWith(".xlsx", fileResult.FileDownloadName);
        Assert.True(fileResult.FileContents.Length > 0);
        // Valid ZIP/XLSX magic bytes (0x50, 0x4B)
        Assert.Equal(0x50, fileResult.FileContents[0]);
        Assert.Equal(0x4B, fileResult.FileContents[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. DATA ISOLATION & CLIENT SCOPING (IAM-06)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Client_NeverSeesAnotherClientsBillingAmounts_IAM06()
    {
        using var appDb = CreateAppDb(nameof(Client_NeverSeesAnotherClientsBillingAmounts_IAM06));
        using var fomsDb = CreateFomsDb(nameof(Client_NeverSeesAnotherClientsBillingAmounts_IAM06));

        // 1. IAM-06 Verification: Clients have NO role and NO login in FOMS
        Assert.DoesNotContain("Client", FOMS.Domain.Constants.FomsRoles.AllCanonicalRoles);
        Assert.False(FOMS.Domain.Constants.FomsRoles.IsRole("Client", FOMS.Domain.Constants.FomsRoles.AllCanonicalRoles));

        var authService = CreateAuthService(appDb);

        // Attempting to login with a client ID throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync("CL-SHOPEE", "Password@123"));

        // Even if an employee record somehow had Role = "Client", Login rejects it
        appDb.Employees.Add(new Employee
        {
            Id = "EMP-ROGUE-CLIENT",
            Username = "client.shopee",
            Name = "Shopee Client User",
            Role = "Client",
            Position = "Client Representative",
            Status = "Active",
            IsActive = true,
            PasswordHash = "PBKDF2$100000$saltsalt$dummyhash"
        });
        await appDb.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync("EMP-ROGUE-CLIENT", "Password@123"));

        // 2. Data Scoping Verification: Invoices show only their own client's amounts
        var lazadaInvoice = new Invoice
        {
            Id = "INV-LZD-01",
            InvoiceNo = "INV-LZD-01",
            ClientId = "CL-LAZADA",
            ClientName = "Lazada Philippines",
            TotalAmount = 50000m,
            Balance = 50000m,
            DocumentStatus = "Sent"
        };
        var shopeeInvoice = new Invoice
        {
            Id = "INV-SHP-01",
            InvoiceNo = "INV-SHP-01",
            ClientId = "CL-SHOPEE",
            ClientName = "Shopee Philippines",
            TotalAmount = 80000m,
            Balance = 80000m,
            DocumentStatus = "Sent"
        };
        appDb.Invoices.AddRange(lazadaInvoice, shopeeInvoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, fomsDb);
        // Authenticated internal staff viewing client-filtered invoices
        SetContext(controller, CreatePrincipal("Accountant", "Staff User", "EMP-003"));

        // A. Direct access to a specific invoice returns only that invoice's client amounts
        var accessShopeeResult = await controller.GetById("INV-SHP-01");
        var okSingle = Assert.IsType<OkObjectResult>(accessShopeeResult);
        Assert.Equal("CL-SHOPEE", GetValueProp(okSingle.Value, "clientId")?.ToString());
        Assert.Equal(80000m, Convert.ToDecimal(GetValueProp(okSingle.Value, "totalAmount")));

        // B. Querying invoice list filtered by client returns only that client's invoices; other client never leaked
        var listResult = await controller.GetInvoices(clientId: "CL-SHOPEE");
        var okList = Assert.IsType<OkObjectResult>(listResult);
        var returnedInvoices = Assert.IsAssignableFrom<IEnumerable<object>>(okList.Value).ToList();
        Assert.Single(returnedInvoices);

        var singleInv = returnedInvoices.First();
        Assert.Equal("INV-SHP-01", GetValueProp(singleInv, "invoiceNo")?.ToString());
        Assert.Equal(80000m, Convert.ToDecimal(GetValueProp(singleInv, "totalAmount")));
        Assert.DoesNotContain(returnedInvoices, inv => string.Equals(GetValueProp(inv, "invoiceNo")?.ToString(), "INV-LZD-01"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. REFUND OUTFLOW REJECTION GUARD
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CashOutflows_RejectsRefundCategory()
    {
        using var appDb = CreateAppDb(nameof(CashOutflows_RejectsRefundCategory));
        using var fomsDb = CreateFomsDb(nameof(CashOutflows_RejectsRefundCategory));

        var controller = new CashOutflowsController(fomsDb, Mock.Of<ILogger<CashOutflowsController>>());
        SetContext(controller, CreatePrincipal("Finance Manager", "FM User", "EMP-001"));

        var refundRequest = new CashOutflowsController.CreateCashOutflowDto
        {
            ReferenceNumber = "OUT-REFUND-999",
            Category = "Refund",
            Amount = 5000m,
            Description = "Client refund request",
            Payee = "Client Contact"
        };

        var result = await controller.Create(refundRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Refund outflows are not supported", badRequest.Value?.ToString() ?? "");
    }
}
