using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class AccountingPeriodCloseTests
{
    private static ApplicationDbContext CreateApplicationContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FomsDbContext CreateFomsContext() =>
        new(new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ClosingPeriod_StoresChecklistTotalsAndAudit()
    {
        using var applicationContext = CreateApplicationContext();
        using var fomsContext = CreateFomsContext();
        var controller = CreateController(applicationContext, fomsContext);

        var result = await controller.ClosePeriod(new ClosePeriodRequest
        {
            PeriodType = "Month",
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd = new DateTime(2026, 10, 31),
            BankBookBalance = 250m,
            BankStatementBalance = 250m,
            BankStatementProofReference = "statement-2026-10.pdf",
            CashBookBalance = 50m,
            CountedCashBalance = 50m
        });

        Assert.IsType<CreatedAtActionResult>(result);
        var close = await applicationContext.AccountingPeriodCloses.SingleAsync();
        Assert.Equal("EMP-001", close.ClosedBy);
        Assert.Equal(0m, close.ReceivableLedgerTotal);
        Assert.Equal(0m, close.AgingReportTotal);
        Assert.Equal("statement-2026-10.pdf", close.BankStatementProofReference);
        Assert.Single(await applicationContext.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ClosingPeriod_RejectsUnvalidatedPayments()
    {
        using var applicationContext = CreateApplicationContext();
        using var fomsContext = CreateFomsContext();
        applicationContext.Payments.Add(new Payment
        {
            Id = "PAY-PENDING",
            PaymentDate = "2026-10-21",
            PaymentStatus = "Pending Validation"
        });
        await applicationContext.SaveChangesAsync(CancellationToken.None);
        var controller = CreateController(applicationContext, fomsContext);

        var result = await controller.ClosePeriod(CreateValidRequest());

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await applicationContext.AccountingPeriodCloses.ToListAsync());
    }

    [Fact]
    public async Task ClosedPeriod_BlocksBackdatedFinancialChangesButAllowsLaterCollection()
    {
        using var context = CreateApplicationContext();
        context.AccountingPeriodCloses.Add(new AccountingPeriodClose
        {
            PeriodType = "Month",
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd = new DateTime(2026, 10, 31),
            ClosedBy = "EMP-001"
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var payment = new Payment
        {
            Id = "PAY-CLOSED",
            PaymentDate = "2026-10-31",
            PaymentStatus = "Validated"
        };
        context.Payments.Add(payment);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(CancellationToken.None));

        payment.PaymentDate = "2026-11-01";
        await context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ClosedPeriod_AllowsLaterPaymentApplicationButBlocksInvoiceAmountEdits()
    {
        using var context = CreateApplicationContext();
        var invoice = new Invoice
        {
            Id = "INV-CLOSED",
            BillingDate = "2026-10-10",
            TotalAmount = 100m,
            Balance = 100m
        };
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync(CancellationToken.None);

        context.AccountingPeriodCloses.Add(new AccountingPeriodClose
        {
            PeriodType = "Month",
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd = new DateTime(2026, 10, 31),
            ClosedBy = "EMP-001"
        });
        await context.SaveChangesAsync(CancellationToken.None);

        invoice.AmountPaid = 100m;
        invoice.Balance = 0m;
        invoice.PaymentStatus = "Paid";
        await context.SaveChangesAsync(CancellationToken.None);

        invoice.TotalAmount = 110m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(CancellationToken.None));
    }

    private static ClosePeriodRequest CreateValidRequest() => new()
    {
        PeriodType = "Month",
        PeriodStart = new DateTime(2026, 10, 1),
        PeriodEnd = new DateTime(2026, 10, 31),
        BankBookBalance = 250m,
        BankStatementBalance = 250m,
        BankStatementProofReference = "statement-2026-10.pdf",
        CashBookBalance = 50m,
        CountedCashBalance = 50m
    };

    private static PeriodCloseController CreateController(ApplicationDbContext applicationContext, FomsDbContext fomsContext)
    {
        var controller = new PeriodCloseController(applicationContext, fomsContext);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "Finance Manager"),
            new Claim("employee_number", "EMP-001")
        ], "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }
}
