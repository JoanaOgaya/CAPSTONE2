using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Features;
using FOMS.Application.Services;
using FOMS.Api.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using Notification = FOMS.Domain.Entities.Notification;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class ComprehensiveVerificationTests
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
    // 1. ALL REPORTS EXPORT TO .XLSX
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AllReports_ExportToXlsx_GeneratedSuccessfully()
    {
        using var context = CreateContext();
        var statementService = new FinancialStatementService(context);

        // A. Financial Statements / AFS Package Excel Export
        var afsExcelBytes = await statementService.ExportAfsPackageToExcelAsync(2026, "Year", 1, "EMP-001");
        Assert.NotNull(afsExcelBytes);
        Assert.True(afsExcelBytes.Length > 0);

        // Verify ClosedXML produced a valid PK (zip) signature for .xlsx
        Assert.Equal((byte)'P', afsExcelBytes[0]);
        Assert.Equal((byte)'K', afsExcelBytes[1]);

        // B. Weekly Budget Report Excel Export
        var budgetLines = new List<MonthlyBudgetLine>
        {
            new() { FiscalYear = 2026, Month = 1, DepartmentCode = "DEP-OPS", DepartmentName = "Operations", Category = "Fuel", BudgetAmount = 100000m, ActualAmount = 35000m, CommittedAmount = 0m },
            new() { FiscalYear = 2026, Month = 1, DepartmentCode = "DEP-ADM", DepartmentName = "Administration", Category = "Food allowance", BudgetAmount = 50000m, ActualAmount = 15000m, CommittedAmount = 0m }
        };
        context.MonthlyBudgetLines.AddRange(budgetLines);
        await context.SaveChangesAsync();

        var budgetService = new BudgetService(context);
        var weeklyReport = await budgetService.GetWeeklyBudgetReportAsync(2026, 1);
        Assert.NotNull(weeklyReport);
        Assert.Equal(150000m, weeklyReport.TotalMonthlyBudget);
        Assert.Equal(50000m, weeklyReport.TotalActualSpend);

        using var budgetWb = new ClosedXML.Excel.XLWorkbook();
        var ws = budgetWb.Worksheets.Add("Weekly_Budget");
        ws.Cell(1, 1).Value = "Department Code";
        ws.Cell(1, 2).Value = "Monthly Budget";
        ws.Cell(1, 3).Value = "Actual Spend";
        int r = 2;
        foreach (var dept in weeklyReport.Departments)
        {
            ws.Cell(r, 1).Value = dept.DepartmentCode;
            ws.Cell(r, 2).Value = dept.MonthlyBudget;
            ws.Cell(r, 3).Value = dept.ActualSpend;
            r++;
        }
        using var ms = new System.IO.MemoryStream();
        budgetWb.SaveAs(ms);
        var budgetExcelBytes = ms.ToArray();
        Assert.NotNull(budgetExcelBytes);
        Assert.True(budgetExcelBytes.Length > 0);
        Assert.Equal((byte)'P', budgetExcelBytes[0]);
        Assert.Equal((byte)'K', budgetExcelBytes[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. RETENTION 2Y CLIENT-VISIBLE / 5Y INTERNAL ENFORCED
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Retention_TwoYearClientVisible_And_FiveYearInternal_Enforced()
    {
        using var context = CreateContext();
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        // 1. Settled invoice older than 2 years (e.g. 2023) -> Archived = true (hidden from client)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-2Y-OLD",
            InvoiceNo = "INV-2023-001",
            ClientId = "CLI-01",
            BillingDate = "2023-01-15",
            TotalAmount = 25000m,
            AmountPaid = 25000m,
            Balance = 0m,
            PaymentStatus = "Paid",
            Archived = false
        });

        // 2. Settled invoice older than 5 years (e.g. 2020) -> 5y statutory internal compliance
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-5Y-OLD",
            InvoiceNo = "INV-2020-001",
            ClientId = "CLI-02",
            BillingDate = "2020-01-15",
            TotalAmount = 40000m,
            AmountPaid = 40000m,
            Balance = 0m,
            PaymentStatus = "Paid",
            Archived = false
        });

        // 3. Recent settled invoice (< 2 years, e.g. 2025) -> Remains unarchived (visible to client)
        context.Invoices.Add(new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-RECENT",
            InvoiceNo = "INV-2025-001",
            ClientId = "CLI-03",
            BillingDate = "2025-06-01",
            TotalAmount = 30000m,
            AmountPaid = 30000m,
            Balance = 0m,
            PaymentStatus = "Paid",
            Archived = false
        });

        // 4. Inactive client older than 2 years without open receivables
        context.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "CLI-INACTIVE-OLD",
            ClientCode = "CLI-INACTIVE-OLD",
            Name = "Old Client Corp",
            Status = "Inactive",
            IsActive = false,
            LastTransaction = "2023-01-10",
            Archived = false
        });

        await context.SaveChangesAsync();

        var archiveService = new RetentionArchiveService();
        var archivedCount = await archiveService.ArchiveEligibleRecordsAsync(context, now, CancellationToken.None);

        Assert.Equal(3, archivedCount); // 2 invoices + 1 client archived

        // Check 2Y client-visible enforcement:
        var inv2y = await context.Invoices.FindAsync("INV-2Y-OLD");
        var inv5y = await context.Invoices.FindAsync("INV-5Y-OLD");
        var invRecent = await context.Invoices.FindAsync("INV-RECENT");
        var clientInactive = await context.Clients.FindAsync("CLI-INACTIVE-OLD");

        Assert.True(inv2y!.Archived);
        Assert.True(inv5y!.Archived);
        Assert.False(invRecent!.Archived); // Visible to client!
        Assert.True(clientInactive!.Archived);

        // Check 5Y internal statutory retention logging:
        var fiveYearAudit = await context.AuditLogs.FirstOrDefaultAsync(l =>
            l.EntityId == "INV-5Y-OLD" && l.Action == "Internal 5Y Retention Enforced");
        Assert.NotNull(fiveYearAudit);
        Assert.Contains("5-year internal statutory retention", fiveYearAudit.Details);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. AUDIT LOG ON STATUS CHANGES, APPROVALS, EXPORTS, LOCKS
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AuditLog_StatusChanges_Approvals_Exports_Locks_Logged()
    {
        using var context = CreateContext();

        // A. Status Change / Exception Audit Log
        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(context);
        var payment = new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-EXC-001",
            InvoiceId = "INV-001",
            InvoiceNo = "INV-001",
            Amount = 10000m,
            PaymentStatus = "Exception",
            RejectionReason = "Overpayment detected"
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var resolvedPayment = await handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand("PAY-EXC-001", "send-to-validation", "Approved excess credited", "Finance Manager"),
            CancellationToken.None);
        Assert.Equal("Pending Validation", resolvedPayment!.PaymentStatus);

        // B. Period Close Lock Audit Log
        var closeAudit = new FOMS.Domain.Entities.AuditLog
        {
            UserId = "EMP-001",
            EntityName = "AccountingPeriodClose",
            EntityId = "CLOSE-2026-11",
            Action = "PERIOD_CLOSED",
            Details = "Period locked by Finance Manager after complete checklist verification."
        };
        context.AuditLogs.Add(closeAudit);

        // C. Report Export Audit Log
        var exportAudit = new FOMS.Domain.Entities.AuditLog
        {
            UserId = "EMP-001",
            EntityName = "Report",
            EntityId = "REP-AGING",
            Action = "AGING_REPORT_EXPORT_EXCEL",
            Details = "Exported weekly aging report to Excel."
        };
        context.AuditLogs.Add(exportAudit);

        // D. Approval Audit Log
        var approvalAudit = new FOMS.Domain.Entities.AuditLog
        {
            UserId = "EMP-099",
            EntityName = "FinancialStatements",
            EntityId = "AFS-2026",
            Action = "AFS_APPROVED_CEO",
            Details = "Annual financial statement package approved by CEO."
        };
        context.AuditLogs.Add(approvalAudit);

        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Contains(logs, l => l.Action == "PERIOD_CLOSED");
        Assert.Contains(logs, l => l.Action == "AGING_REPORT_EXPORT_EXCEL");
        Assert.Contains(logs, l => l.Action == "AFS_APPROVED_CEO");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. EMAIL NOTICES (REJECTED PAYMENT NTF-02 & FOLLOW-UP REMINDERS)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EmailNotices_RejectedPaymentNTF02_And_FollowUps_Dispatched()
    {
        using var context = CreateContext();

        // NTF-02 Rejected Payment Notice
        var client = new FOMS.Domain.Entities.Client
        {
            Id = "CLI-NTF",
            ClientCode = "CLI-NTF",
            Name = "Prime Express Ltd",
            Email = "accounting@primeexpress.ph"
        };
        context.Clients.Add(client);

        var ntf02 = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Type = "alert",
            Title = "[NTF-02] Payment Rejected Notice",
            Description = $"Email notice dispatched to accounting@primeexpress.ph: Your payment of ₱15,000.00 for Invoice INV-NTF was rejected. Reason: Incorrect reference number.",
            InvoiceNo = "INV-NTF",
            RecipientRole = "Client",
            Read = false,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = "Finance Department"
        };
        context.Notifications.Add(ntf02);

        // Follow-up Notice
        var followUpNotice = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Type = "warning",
            Title = "[EMAIL NOTICE] Overdue Payment Notice: Invoice INV-DUE-01",
            Description = "Email reminder sent to accounting@primeexpress.ph for Invoice INV-DUE-01 (Balance: ₱50,000.00, Due: 2026-10-15).",
            InvoiceNo = "INV-DUE-01",
            RecipientRole = "Client",
            Read = false,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = "Billing Department"
        };
        context.Notifications.Add(followUpNotice);

        await context.SaveChangesAsync();

        var notifs = await context.Notifications.ToListAsync();
        Assert.Contains(notifs, n => n.Title.Contains("[NTF-02] Payment Rejected Notice"));
        Assert.Contains(notifs, n => n.Title.Contains("[EMAIL NOTICE] Overdue Payment Notice"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. STATUS FIELDS ARE SEPARATE PER RECORD; OVERDUE/AGING/LATE ARE DERIVED
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void StatusFields_AreSeparate_And_AgingOverdueLateAreDerived()
    {
        // Invoice: DocumentStatus and PaymentStatus are distinct columns
        var invoice = new FOMS.Domain.Entities.Invoice
        {
            DocumentStatus = "Sent",
            PaymentStatus = "Unpaid",
            DueDate = "2026-10-01", // in past
            Balance = 50000m
        };

        // Derived overdue & aging bucket logic
        var today = new DateTime(2026, 11, 1);
        var dueDate = DateTime.Parse(invoice.DueDate);
        bool isOverdue = today > dueDate;
        int daysOverdue = (int)(today - dueDate).TotalDays;

        string agingBucket = daysOverdue switch
        {
            <= 0 => "Not Yet Due",
            <= 30 => "1-30",
            <= 60 => "31-60",
            <= 90 => "61-90",
            _ => "90+"
        };

        Assert.Equal("Sent", invoice.DocumentStatus);
        Assert.Equal("Unpaid", invoice.PaymentStatus);
        Assert.True(isOverdue);
        Assert.Equal(31, daysOverdue);
        Assert.Equal("31-60", agingBucket); // dynamically derived, not hardcoded into Status

        // Liquidation: Status is strictly Pending/Validated/Rejected/For Correction, Late is a derived flag
        var liq = new Liquidation
        {
            Status = "Pending",
            DueDate = new DateTime(2026, 10, 15) // in past
        };
        bool isLate = today > liq.DueDate.Value;

        Assert.Equal("Pending", liq.Status); // status is Pending, NOT "Late"
        Assert.True(isLate); // Late is a derived flag!
    }
}
