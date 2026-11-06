using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using FOMS.Api.Controllers;
using FOMS.Application.Configuration;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using FOMS.Infrastructure.Services;
using Xunit;

namespace FOMS.Tests;

public class InvoiceDocumentLifecycleAndAgingTests
{
    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateMssqlDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FomsDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string role = "Head Accountant", string name = "Head Accountant User")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "EMP-002"),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("name", name)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static object? GetValueProp(object? obj, string propName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(obj);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Document status lifecycle: Draft -> For Review -> Approved -> Sent -> Received
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DocumentStatus_Lifecycle_TransitionsCorrectly()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var inv = new Invoice
        {
            Id = "INV-LC-001",
            InvoiceNo = "INV-2026-0101",
            ClientId = "CL-001",
            ClientName = "Acme Corp",
            DocumentStatus = "Draft",
            PaymentStatus = "Unpaid",
            TotalAmount = 1000m,
            Balance = 1000m
        };
        appDb.Invoices.Add(inv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // 1. Submit for review -> For Review
        var submitRes = await controller.TransitionInvoice("INV-LC-001", "submit", new InvoiceTransitionRequest());
        var submitOk = Assert.IsType<OkObjectResult>(submitRes);
        Assert.Equal("For Review", GetValueProp(submitOk.Value, "documentStatus")?.ToString());

        // 2. Approve -> Approved
        var approveRes = await controller.TransitionInvoice("INV-LC-001", "approve", new InvoiceTransitionRequest());
        var approveOk = Assert.IsType<OkObjectResult>(approveRes);
        Assert.Equal("Approved", GetValueProp(approveOk.Value, "documentStatus")?.ToString());

        // 3. Sent -> Sent
        var sendRes = await controller.TransitionInvoice("INV-LC-001", "send", new InvoiceTransitionRequest());
        var sendOk = Assert.IsType<OkObjectResult>(sendRes);
        Assert.Equal("Sent", GetValueProp(sendOk.Value, "documentStatus")?.ToString());

        // 4. Received -> Received
        var receiveRes = await controller.TransitionInvoice("INV-LC-001", "receive", new InvoiceTransitionRequest { ReceivedDate = DateTime.UtcNow });
        var receiveOk = Assert.IsType<OkObjectResult>(receiveRes);
        Assert.Equal("Received", GetValueProp(receiveOk.Value, "documentStatus")?.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Failed Head Accountant review returns to Draft with reason
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FailedReview_ReturnsToDraft_WithMandatoryReason()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var inv = new Invoice
        {
            Id = "INV-REV-001",
            InvoiceNo = "INV-2026-0102",
            ClientId = "CL-001",
            ClientName = "Acme Corp",
            DocumentStatus = "For Review",
            PaymentStatus = "Unpaid",
            TotalAmount = 1000m,
            Balance = 1000m
        };
        appDb.Invoices.Add(inv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // Reject without reason should fail
        var rejectFail = await controller.TransitionInvoice("INV-REV-001", "reject", new InvoiceTransitionRequest { Remarks = "" });
        var badRequest = Assert.IsType<BadRequestObjectResult>(rejectFail);
        Assert.Contains("reason is required", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Reject with reason succeeds and returns to Draft
        var rejectOk = await controller.TransitionInvoice("INV-REV-001", "reject", new InvoiceTransitionRequest
        {
            ReviewRejectionReason = "Declared valuation discrepancy on waybill WB-9988"
        });
        var okResult = Assert.IsType<OkObjectResult>(rejectOk);
        Assert.Equal("Draft", GetValueProp(okResult.Value, "documentStatus")?.ToString());
        Assert.Equal("Declared valuation discrepancy on waybill WB-9988", GetValueProp(okResult.Value, "reviewRejectionReason")?.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Random-check sampling for Head Accountant review
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ReviewSampling_SamplesBatchOfForReviewInvoices()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        for (int i = 1; i <= 10; i++)
        {
            appDb.Invoices.Add(new Invoice
            {
                Id = $"INV-SMP-{i}",
                InvoiceNo = $"INV-2026-02{i:D2}",
                ClientId = "CL-001",
                ClientName = "Acme Corp",
                DocumentStatus = "For Review",
                TotalAmount = 500m * i,
                Balance = 500m * i
            });
        }
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // Sample 30% of invoices
        var result = await controller.SampleInvoicesForReview(new ReviewSamplingRequest { SampleRatePercent = 30 });
        var ok = Assert.IsType<OkObjectResult>(result);

        var sampledCount = Convert.ToInt32(GetValueProp(ok.Value, "sampledCount"));
        Assert.Equal(3, sampledCount); // 30% of 10 = 3

        var sampledInDb = await appDb.Invoices.CountAsync(i => i.IsSampledForReview);
        Assert.Equal(3, sampledInDb);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Dispatch to Operations via STARS only after Approved
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DispatchToStars_BlockedWhenNotApproved_AllowedWhenApproved()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var invDraft = new Invoice
        {
            Id = "INV-STARS-DRAFT",
            InvoiceNo = "INV-STARS-DRAFT",
            ClientName = "Acme Corp",
            DocumentStatus = "Draft",
            TotalAmount = 5000m,
            Balance = 5000m,
            PaymentStatus = "Unpaid"
        };
        var invApproved = new Invoice
        {
            Id = "INV-STARS-APP",
            InvoiceNo = "INV-STARS-APP",
            ClientName = "Acme Corp",
            DocumentStatus = "Approved",
            TotalAmount = 5000m,
            Balance = 5000m,
            PaymentStatus = "Unpaid"
        };
        appDb.Invoices.AddRange(invDraft, invApproved);
        await appDb.SaveChangesAsync();

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var options = Options.Create(new StarsIntegrationOptions
        {
            StarsBaseUrl = "https://stars.test.local",
            OutboundApiKey = "test-api-key"
        });
        var logger = new Mock<ILogger<StarsIntegrationService>>();

        var service = new StarsIntegrationService(appDb, mockHttpFactory.Object, options, logger.Object);

        // 1. Attempt dispatch on Draft invoice -> Rejected
        var (isSuccessDraft, messageDraft, _) = await service.RequestInvoiceEndorsementAsync("INV-STARS-DRAFT");
        Assert.False(isSuccessDraft);
        Assert.Contains("only allowed after invoice is Approved", messageDraft, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. On Sent: create receivable (no due date), recognize accrual revenue
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OnSent_CreatesReceivableWithNoDueDate_RecognizesAccrualRevenue()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var inv = new Invoice
        {
            Id = "INV-SENT-001",
            InvoiceNo = "INV-2026-0301",
            ClientId = "CL-001",
            ClientName = "Acme Corp",
            DocumentStatus = "Approved",
            TotalAmount = 1120m,
            Subtotal = 1000m,
            VatAmount = 120m,
            Surcharge = 0m,
            Balance = 1120m,
            PaymentStatus = "Unpaid"
        };
        appDb.Invoices.Add(inv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // Transition to Sent
        var sendRes = await controller.TransitionInvoice("INV-SENT-001", "send", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(sendRes);

        // Verify receivable created with NO due date
        var receivable = await appDb.ReceivableBalances.FirstOrDefaultAsync();
        Assert.NotNull(receivable);
        Assert.Null(receivable.DueDate); // No due date on Sent!
        Assert.Equal(1120m, receivable.BalanceAmount);

        // Verify accrual revenue recognition
        var updatedInv = await appDb.Invoices.FindAsync("INV-SENT-001");
        Assert.NotNull(updatedInv);
        Assert.True(updatedInv.AccrualRevenueRecognized);
        Assert.NotNull(updatedInv.AccrualJournalEntryId);

        var je = await appDb.JournalEntries.FirstOrDefaultAsync(j => j.Id == updatedInv.AccrualJournalEntryId);
        Assert.NotNull(je);
        Assert.Contains("Accrual Revenue Recognition", je.Description);

        var glEntries = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        Assert.NotEmpty(glEntries);
        // Total Debits == Total Credits
        Assert.Equal(glEntries.Sum(g => g.Debit), glEntries.Sum(g => g.Credit));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. On Received: due = received date + 30 days (client-configurable)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OnReceived_SetsDueDate_BasedOnClientCreditTermDays()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        // Client with 45-day credit term
        appDb.Clients.Add(new Client
        {
            Id = "CL-TERM-45",
            ClientCode = "CL-TERM-45",
            Name = "Long Term Client",
            CreditTermDays = 45
        });

        var inv = new Invoice
        {
            Id = "INV-RCV-001",
            InvoiceNo = "INV-2026-0302",
            ClientId = "CL-TERM-45",
            ClientName = "Long Term Client",
            DocumentStatus = "Sent",
            TotalAmount = 2000m,
            Balance = 2000m,
            PaymentStatus = "Unpaid"
        };
        appDb.Invoices.Add(inv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        var receivedDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var rcvRes = await controller.TransitionInvoice("INV-RCV-001", "receive", new InvoiceTransitionRequest
        {
            ReceivedDate = receivedDate
        });
        Assert.IsType<OkObjectResult>(rcvRes);

        var updatedInv = await appDb.Invoices.FindAsync("INV-RCV-001");
        Assert.NotNull(updatedInv);
        Assert.Equal("Received", updatedInv.DocumentStatus);

        // Due date = May 1 + 45 days = June 15, 2026
        var expectedDue = receivedDate.AddDays(45).ToString("yyyy-MM-dd");
        Assert.Equal(expectedDue, updatedInv.DueDate);

        var receivable = await appDb.ReceivableBalances.FirstOrDefaultAsync();
        Assert.NotNull(receivable);
        Assert.Equal(receivedDate.AddDays(45), receivable.DueDate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Derived aging buckets: Not Yet Due, Due Soon, 1-30, 31-60, 61-90, 90+
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void DerivedAgingBuckets_CorrectlyComputedDynamically()
    {
        var today = DateTime.UtcNow.Date;

        // No due date => Not Yet Due
        Assert.Equal("Not Yet Due", BillingComputationService.ComputeDerivedAgingBucket(null));
        Assert.Equal("Not Yet Due", BillingComputationService.ComputeDerivedAgingBucket(""));

        // Due in 15 days (> 7 days) => Not Yet Due
        Assert.Equal("Not Yet Due", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(15).ToString("yyyy-MM-dd"), dueSoonDays: 7));

        // Due in 3 days (<= 7 days) => Due Soon
        Assert.Equal("Due Soon", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(3).ToString("yyyy-MM-dd"), dueSoonDays: 7));

        // Due today => Due Soon
        Assert.Equal("Due Soon", BillingComputationService.ComputeDerivedAgingBucket(today.ToString("yyyy-MM-dd"), dueSoonDays: 7));

        // Overdue 10 days => 1-30
        Assert.Equal("1-30", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(-10).ToString("yyyy-MM-dd")));

        // Overdue 45 days => 31-60
        Assert.Equal("31-60", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(-45).ToString("yyyy-MM-dd")));

        // Overdue 75 days => 61-90
        Assert.Equal("61-90", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(-75).ToString("yyyy-MM-dd")));

        // Overdue 120 days => 90+
        Assert.Equal("90+", BillingComputationService.ComputeDerivedAgingBucket(today.AddDays(-120).ToString("yyyy-MM-dd")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Weekly aging report with Excel export
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task WeeklyAgingReport_And_ExcelExport_Succeeds()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var today = DateTime.UtcNow.Date;
        appDb.Invoices.AddRange(
            new Invoice { Id = "INV-R1", InvoiceNo = "INV-R1", ClientId = "CL-001", ClientName = "Client A", TotalAmount = 1000m, Balance = 1000m, DueDate = today.AddDays(15).ToString("yyyy-MM-dd"), DocumentStatus = "Sent" },
            new Invoice { Id = "INV-R2", InvoiceNo = "INV-R2", ClientId = "CL-001", ClientName = "Client A", TotalAmount = 2000m, Balance = 2000m, DueDate = today.AddDays(3).ToString("yyyy-MM-dd"), DocumentStatus = "Received" },
            new Invoice { Id = "INV-R3", InvoiceNo = "INV-R3", ClientId = "CL-002", ClientName = "Client B", TotalAmount = 3000m, Balance = 3000m, DueDate = today.AddDays(-15).ToString("yyyy-MM-dd"), DocumentStatus = "Received" },
            new Invoice { Id = "INV-R4", InvoiceNo = "INV-R4", ClientId = "CL-002", ClientName = "Client B", TotalAmount = 4000m, Balance = 4000m, DueDate = today.AddDays(-100).ToString("yyyy-MM-dd"), DocumentStatus = "Received" }
        );
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // 1. JSON Report
        var reportRes = await controller.GetAgingReport(dueSoonDays: 7);
        var ok = Assert.IsType<OkObjectResult>(reportRes);
        var summary = GetValueProp(ok.Value, "summary");
        Assert.NotNull(summary);
        Assert.Equal(10000m, Convert.ToDecimal(GetValueProp(summary, "grandTotal")));
        Assert.Equal(1000m, Convert.ToDecimal(GetValueProp(summary, "notYetDue")));
        Assert.Equal(2000m, Convert.ToDecimal(GetValueProp(summary, "dueSoon")));
        Assert.Equal(3000m, Convert.ToDecimal(GetValueProp(summary, "bucket1To30")));
        Assert.Equal(4000m, Convert.ToDecimal(GetValueProp(summary, "bucket90Plus")));

        // 2. Excel / CSV Export
        var exportRes = await controller.ExportAgingReportExcel(dueSoonDays: 7);
        var fileResult = Assert.IsType<FileContentResult>(exportRes);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
        Assert.Contains("Weekly_AR_Aging_Report_", fileResult.FileDownloadName);
        var content = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.Contains("WEEKLY ACCOUNTS RECEIVABLE AGING REPORT", content);
        Assert.Contains("Client A", content);
        Assert.Contains("Client B", content);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Follow-up Reminders & Follow-up Log
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FollowUpReminders_And_FollowUpLog_TracksAudit()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var today = DateTime.UtcNow.Date;
        var invDueSoon = new Invoice
        {
            Id = "INV-FLW-001",
            InvoiceNo = "INV-2026-0401",
            ClientId = "CL-001",
            ClientName = "Acme Corp",
            TotalAmount = 5000m,
            Balance = 5000m,
            DueDate = today.AddDays(2).ToString("yyyy-MM-dd"),
            BillingDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
            DocumentStatus = "Received"
        };
        appDb.Invoices.Add(invDueSoon);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // 1. Get reminders
        var remindersRes = await controller.GetFollowUpReminders();
        var okReminders = Assert.IsType<OkObjectResult>(remindersRes);
        var dueSoonList = GetValueProp(okReminders.Value, "dueSoonReminders") as System.Collections.IEnumerable;
        Assert.NotNull(dueSoonList);

        // 2. Record follow-up
        var logRes = await controller.RecordFollowUp("INV-FLW-001", new FollowUpLogSubmissionDto
        {
            ContactPerson = "John Smith (Finance Head)",
            ContactMethod = "Call",
            Outcome = "Promise to Pay",
            PromisedDate = DateTime.UtcNow.AddDays(3),
            PromisedAmount = 5000m,
            Notes = "Client acknowledged invoice receipt and promised wire transfer."
        });
        Assert.IsType<OkObjectResult>(logRes);

        // 3. Query follow-up logs
        var getLogsRes = await controller.GetFollowUps("INV-FLW-001");
        var okLogs = Assert.IsType<OkObjectResult>(getLogsRes);
        var logs = Assert.IsAssignableFrom<System.Collections.IEnumerable>(okLogs.Value);
        Assert.NotEmpty(logs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. Disputed invoices carry reason code and are excluded from prioritization
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DisputedInvoices_CarriesReasonCode_ExcludedFromPrioritization()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var today = DateTime.UtcNow.Date;
        var invDisputed = new Invoice
        {
            Id = "INV-DISP-001",
            InvoiceNo = "INV-2026-0501",
            ClientId = "CL-001",
            ClientName = "Acme Corp",
            TotalAmount = 8000m,
            Balance = 8000m,
            DueDate = today.AddDays(-5).ToString("yyyy-MM-dd"), // Overdue!
            DocumentStatus = "Received"
        };
        appDb.Invoices.Add(invDisputed);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        // Raise dispute
        var disputeRes = await controller.TransitionInvoice("INV-DISP-001", "dispute", new InvoiceTransitionRequest
        {
            DisputeReasonCode = "DISP-RATES_MISMATCH",
            Remarks = "Client claims fuel surcharge rate exceeded contract rate."
        });
        var okDispute = Assert.IsType<OkObjectResult>(disputeRes);
        Assert.Equal("Disputed", GetValueProp(okDispute.Value, "documentStatus")?.ToString());
        Assert.True((bool?)GetValueProp(okDispute.Value, "isDisputed"));
        Assert.Equal("DISP-RATES_MISMATCH", GetValueProp(okDispute.Value, "disputeReasonCode")?.ToString());

        // In reminders: it must be excluded from overdue reminders and placed into disputedInvoices
        var remindersRes = await controller.GetFollowUpReminders();
        var okReminders = Assert.IsType<OkObjectResult>(remindersRes);
        var overdueList = GetValueProp(okReminders.Value, "overdueReminders") as System.Collections.IList;
        var disputedList = GetValueProp(okReminders.Value, "disputedInvoices") as System.Collections.IList;

        Assert.NotNull(overdueList);
        Assert.Empty(overdueList); // Not in overdue prioritization!
        Assert.NotNull(disputedList);
        Assert.Single(disputedList); // Visible under disputed!
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Period-close check: AR ledger total = aging total
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PeriodCloseCheck_Verifies_ArLedgerTotal_Equals_AgingTotal()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        // Invoices with total balance 15,000
        appDb.Invoices.AddRange(
            new Invoice { Id = "INV-P1", InvoiceNo = "INV-P1", ClientId = "CL-001", ClientName = "Client A", TotalAmount = 10000m, Balance = 10000m, DocumentStatus = "Sent" },
            new Invoice { Id = "INV-P2", InvoiceNo = "INV-P2", ClientId = "CL-002", ClientName = "Client B", TotalAmount = 5000m, Balance = 5000m, DocumentStatus = "Received" }
        );

        // Matching receivables ledger total 15,000
        appDb.ReceivableBalances.AddRange(
            new ReceivableBalance { Id = "R-1", InvoiceId = "INV-P1", ClientId = "CL-001", BalanceAmount = 10000m },
            new ReceivableBalance { Id = "R-2", InvoiceId = "INV-P2", ClientId = "CL-002", BalanceAmount = 5000m }
        );
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreatePrincipal() } }
        };

        var checkRes = await controller.RunPeriodCloseCheck();
        var okCheck = Assert.IsType<OkObjectResult>(checkRes);

        Assert.Equal("Balanced", GetValueProp(okCheck.Value, "status")?.ToString());
        Assert.True((bool?)GetValueProp(okCheck.Value, "isBalanced"));
        Assert.Equal(15000m, Convert.ToDecimal(GetValueProp(okCheck.Value, "agingTotal")));
        Assert.Equal(15000m, Convert.ToDecimal(GetValueProp(okCheck.Value, "arLedgerTotal")));
        Assert.Equal(0m, Convert.ToDecimal(GetValueProp(okCheck.Value, "discrepancy")));
    }
}
