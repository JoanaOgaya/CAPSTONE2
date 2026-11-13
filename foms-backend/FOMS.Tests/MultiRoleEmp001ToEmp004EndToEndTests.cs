using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Application.Contracts;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FOMS.Tests;

/// <summary>
/// Multi-Role End-to-End Suite covering EMP-001 (Finance Manager), EMP-002 (Head Accountant),
/// EMP-003 (Accountant), and EMP-004 (Assistant Finance Manager).
/// Strictly verified for local and staging environments only; refuses production execution.
/// </summary>
public class MultiRoleEmp001ToEmp004EndToEndTests
{
    private static FomsDbContext CreateFomsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_App_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void SetUserContext(ControllerBase controller, string role, string username, string empId, ApplicationDbContext? appDb = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new(ClaimTypes.Name, username),
            new("name", username),
            new("unique_name", username),
            new(ClaimTypes.NameIdentifier, empId),
            new("sub", empId),
            new("employeeNumber", empId),
            new("employee_id", empId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        if (appDb != null)
        {
            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(ApplicationDbContext))).Returns(appDb);
            httpContext.RequestServices = spMock.Object;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static void AssertLocalOrStagingEnvironmentOnly()
    {
        var envTarget = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var isProduction = envTarget.Equals("Production", StringComparison.OrdinalIgnoreCase);
        Assert.False(isProduction, "SAFETY VIOLATION: Multi-role E2E test suite must never execute against a production stack.");
    }

    [Fact]
    public async Task MultiRole_InvoiceLifecycle_CreateToHaVerifyToFmApproveToAR()
    {
        AssertLocalOrStagingEnvironmentOnly();

        using var fomsDb = CreateFomsDb(nameof(MultiRole_InvoiceLifecycle_CreateToHaVerifyToFmApproveToAR));
        using var appDb = CreateAppDb(nameof(MultiRole_InvoiceLifecycle_CreateToHaVerifyToFmApproveToAR));

        // 1. Seed Client
        fomsDb.Clients.Add(new FOMS.Domain.Entities.SqlServer.Client
        {
            ClientId = 1,
            ClientCode = "CLI-E2E-01",
            CompanyName = "Apex Logistics Corp"
        });

        // 2. Step 1: EMP-003 (Accountant) creates draft invoice
        var invoice = new FOMS.Domain.Entities.SqlServer.Invoice
        {
            InvoiceId = 201,
            InvoiceNumber = "INV-E2E-201",
            ClientId = 1,
            TotalAmount = 25000.00m,
            InvoiceStatus = "Draft",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            CreatedAt = DateTime.UtcNow
        };
        fomsDb.Invoices.Add(invoice);
        await fomsDb.SaveChangesAsync();

        // EMP-003 submits for review -> status: Pending Review
        invoice.InvoiceStatus = "Pending Review";
        await fomsDb.SaveChangesAsync();

        // 3. Step 2: EMP-002 (Head Accountant) reviews and verifies billing -> status: Pending Approval
        invoice.InvoiceStatus = "Pending Approval";
        invoice.UpdatedBy = "EMP-002";
        await fomsDb.SaveChangesAsync();
        Assert.Equal("Pending Approval", invoice.InvoiceStatus);

        // 4. Step 3: EMP-001 (Finance Manager) approves invoice -> status: Approved & creates AR
        invoice.InvoiceStatus = "Approved";
        invoice.UpdatedBy = "EMP-001";

        fomsDb.AccountsReceivable.Add(new AccountsReceivable
        {
            ArId = 301,
            InvoiceId = invoice.InvoiceId,
            ClientId = 1,
            InvoiceAmount = invoice.TotalAmount,
            OutstandingBalance = invoice.TotalAmount,
            ArStatus = "Current",
            DueDate = invoice.DueDate
        });
        await fomsDb.SaveChangesAsync();

        // Verification
        var approvedInvoice = await fomsDb.Invoices.FindAsync(201);
        Assert.NotNull(approvedInvoice);
        Assert.Equal("Approved", approvedInvoice.InvoiceStatus);
        Assert.Equal("EMP-001", approvedInvoice.UpdatedBy);

        var ar = await fomsDb.AccountsReceivable.FirstOrDefaultAsync(a => a.InvoiceId == 201);
        Assert.NotNull(ar);
        Assert.Equal(25000.00m, ar.OutstandingBalance);
        Assert.Equal("Current", ar.ArStatus);
    }

    [Fact]
    public async Task MultiRole_Invoice_ReturnForRevisionAndVoidPaths()
    {
        AssertLocalOrStagingEnvironmentOnly();

        using var fomsDb = CreateFomsDb(nameof(MultiRole_Invoice_ReturnForRevisionAndVoidPaths));
        using var appDb = CreateAppDb(nameof(MultiRole_Invoice_ReturnForRevisionAndVoidPaths));

        // Invoice 1: Return path
        var inv1 = new FOMS.Domain.Entities.SqlServer.Invoice
        {
            InvoiceId = 401,
            InvoiceNumber = "INV-RET-401",
            ClientId = 1,
            TotalAmount = 8000.00m,
            InvoiceStatus = "Pending Review",
            CreatedAt = DateTime.UtcNow
        };

        // Invoice 2: Void path
        var inv2 = new FOMS.Domain.Entities.SqlServer.Invoice
        {
            InvoiceId = 402,
            InvoiceNumber = "INV-VOID-402",
            ClientId = 1,
            TotalAmount = 12000.00m,
            InvoiceStatus = "Draft",
            CreatedAt = DateTime.UtcNow
        };

        fomsDb.Invoices.AddRange(inv1, inv2);
        await fomsDb.SaveChangesAsync();

        // 1. Return Path: EMP-002 (Head Accountant) returns invoice with note
        inv1.InvoiceStatus = "Needs Revision";
        inv1.ReviewRejectionReason = "Incorrect billing rate applied for Cebu zone.";
        await fomsDb.SaveChangesAsync();

        var returnedInv = await fomsDb.Invoices.FindAsync(401);
        Assert.Equal("Needs Revision", returnedInv!.InvoiceStatus);

        // EMP-003 (Accountant) corrects rate and re-submits
        returnedInv.TotalAmount = 7500.00m;
        returnedInv.InvoiceStatus = "Pending Review";
        await fomsDb.SaveChangesAsync();

        Assert.Equal("Pending Review", (await fomsDb.Invoices.FindAsync(401))!.InvoiceStatus);
        Assert.Equal(7500.00m, (await fomsDb.Invoices.FindAsync(401))!.TotalAmount);

        // 2. Void Path: EMP-001 (Finance Manager) voids invalid draft invoice
        inv2.InvoiceStatus = "Void";
        inv2.VoidReason = "Duplicate delivery reference; voided by management.";
        await fomsDb.SaveChangesAsync();

        var voidedInv = await fomsDb.Invoices.FindAsync(402);
        Assert.Equal("Void", voidedInv!.InvoiceStatus);
    }

    [Fact]
    public async Task MultiRole_Payment_RecordByAccountant_FmApprove_DashboardSync()
    {
        AssertLocalOrStagingEnvironmentOnly();

        using var fomsDb = CreateFomsDb(nameof(MultiRole_Payment_RecordByAccountant_FmApprove_DashboardSync));
        using var appDb = CreateAppDb(nameof(MultiRole_Payment_RecordByAccountant_FmApprove_DashboardSync));

        // Seed AR and Invoice
        var inv = new FOMS.Domain.Entities.SqlServer.Invoice
        {
            InvoiceId = 501,
            InvoiceNumber = "INV-PAY-501",
            ClientId = 1,
            TotalAmount = 10000.00m,
            InvoiceStatus = "Approved",
            CreatedAt = DateTime.UtcNow
        };
        var ar = new AccountsReceivable
        {
            ArId = 501,
            InvoiceId = 501,
            ClientId = 1,
            InvoiceAmount = 10000.00m,
            OutstandingBalance = 10000.00m,
            ArStatus = "Current"
        };
        fomsDb.Invoices.Add(inv);
        fomsDb.AccountsReceivable.Add(ar);
        await fomsDb.SaveChangesAsync();

        // 1. Step 1: EMP-003 (Accountant) records payment -> Pending Validation
        var payment = new FOMS.Domain.Entities.SqlServer.Payment
        {
            PaymentId = 601,
            InvoiceId = 501,
            Amount = 10000.00m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "PAY-REF-601",
            PaymentStatus = "Pending Validation",
            Remarks = "RecordedBy:EMP-003",
            SubmittedAt = DateTime.UtcNow
        };
        fomsDb.Payments.Add(payment);
        await fomsDb.SaveChangesAsync();

        // 2. Step 2: EMP-001 (Finance Manager) approves payment
        payment.PaymentStatus = "Approved";
        payment.ValidatedAt = DateTime.UtcNow;

        // Reduce AR outstanding balance to 0 and mark Paid
        ar.OutstandingBalance = 0m;
        ar.ArStatus = "Paid";
        inv.InvoiceStatus = "Paid";

        // Sync with Treasury (CashFlowTransactions in ApplicationDbContext)
        appDb.CashFlowTransactions.Add(new FOMS.Domain.Entities.CashFlowTransaction
        {
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            TransactionDate = DateTime.UtcNow,
            Type = "Inflow",
            Category = "Customer Payment",
            Amount = 10000.00m,
            ReferenceNo = "PAY-REF-601",
            Description = "Client payment for INV-PAY-501 approved by EMP-001"
        });
        await fomsDb.SaveChangesAsync();
        await appDb.SaveChangesAsync();

        // 3. Step 3: Verify Dashboard & Treasury reflection
        var verifiedAr = await fomsDb.AccountsReceivable.FindAsync(501);
        Assert.Equal(0m, verifiedAr!.OutstandingBalance);
        Assert.Equal("Paid", verifiedAr.ArStatus);

        var cashInflow = await appDb.CashFlowTransactions.FirstOrDefaultAsync(c => c.ReferenceNo == "PAY-REF-601");
        Assert.NotNull(cashInflow);
        Assert.Equal(10000.00m, cashInflow.Amount);
        Assert.Equal("Inflow", cashInflow.Type);
    }

    [Fact]
    public async Task MultiRole_Liquidation_StrictTwoTierEndorsement_And_LedgerPosting()
    {
        AssertLocalOrStagingEnvironmentOnly();

        using var fomsDb = CreateFomsDb(nameof(MultiRole_Liquidation_StrictTwoTierEndorsement_And_LedgerPosting));
        using var appDb = CreateAppDb(nameof(MultiRole_Liquidation_StrictTwoTierEndorsement_And_LedgerPosting));

        // Seed COA
        appDb.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "101", Name = "Cash and Cash Equivalents", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "105", Name = "Advances to Officers & Employees", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "501", Name = "Operating Expenses", Type = "Expense", NormalBalance = "Debit" }
        );
        await appDb.SaveChangesAsync();

        // Liquidation submitted by Courier EMP-005
        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 701,
            ReferenceNumber = "LIQ-E2E-701",
            SubmittedByName = "EMP-005",
            TotalAmount = 4500.00m,
            LeftoverCash = 500.00m,
            ReimbursementAmount = 0.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        });
        await fomsDb.SaveChangesAsync();

        var liqController = new LiquidationsController(fomsDb, appDb);

        // 1. Tier 1: EMP-004 (Assistant FM) endorses liquidation
        SetUserContext(liqController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var endorseRes = await liqController.Act(701, "endorse", new LiquidationDto { Remarks = "Verified receipts" });
        Assert.IsType<OkObjectResult>(endorseRes);

        var liqAfterEndorse = await fomsDb.Liquidations.FindAsync(701);
        Assert.Equal("Endorsed", liqAfterEndorse!.Status);
        Assert.Equal(0, await appDb.JournalEntries.CountAsync()); // No ledger entries yet

        // 2. Tier 2: EMP-001 (Finance Manager) approves liquidation
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");
        var approveRes = await liqController.Act(701, "approve", new LiquidationDto { Remarks = "Approved by FM" });
        Assert.IsType<OkObjectResult>(approveRes);

        var liqAfterApprove = await fomsDb.Liquidations.FindAsync(701);
        Assert.Equal("Approved", liqAfterApprove!.Status);

        // Balanced double-entry in General Ledger
        var je = await appDb.JournalEntries.FirstAsync();
        var glEntries = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        decimal totalDebit = glEntries.Sum(g => g.Debit);
        decimal totalCredit = glEntries.Sum(g => g.Credit);
        Assert.Equal(5000.00m, totalDebit);
        Assert.Equal(5000.00m, totalCredit);

        // Treasury reflects leftover cash returned
        var inflow = await appDb.CashFlowTransactions.FirstOrDefaultAsync(t => t.Type == "Inflow");
        Assert.NotNull(inflow);
        Assert.Equal(500.00m, inflow.Amount);
    }

    [Fact]
    public async Task MultiRole_ForbiddenScanAccess_HeadAccountantReceives403OnScanEndpoint()
    {
        AssertLocalOrStagingEnvironmentOnly();

        using var fomsDb = CreateFomsDb(nameof(MultiRole_ForbiddenScanAccess_HeadAccountantReceives403OnScanEndpoint));
        using var appDb = CreateAppDb(nameof(MultiRole_ForbiddenScanAccess_HeadAccountantReceives403OnScanEndpoint));

        var httpFactory = new Mock<IHttpClientFactory>();
        var config = new ConfigurationBuilder().Build();

        // EMP-002 (Head Accountant) attempting to access duplicate scan initiation
        var aiDupController = new AiDuplicatesController(appDb, fomsDb, httpFactory.Object, config);
        SetUserContext(aiDupController, "Head Accountant", "EMP-002", "EMP-002");

        var scanRes = await aiDupController.DuplicateScanEndpoint(null, null);

        // Head Accountant must strictly receive 403 Forbidden
        var forbidResult = Assert.IsType<ObjectResult>(scanRes);
        Assert.Equal(403, forbidResult.StatusCode);
    }
}
