using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Application.Services;
using FOMS.Api.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class RetentionArchiveTests
{
    [Fact]
    public async Task ArchiveEligibleRecords_ShouldArchiveSettledAndInactiveRecordsButKeepOpenReceivables()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Clients.AddRange(
            CreateClient("settled-client", "Active", "2023-01-01"),
            CreateClient("open-client", "Inactive", "2023-01-01"),
            CreateClient("recent-client", "Active", "2025-01-01"),
            CreateClient("inactive-client", "Inactive", "2023-01-01"));
        context.Invoices.AddRange(
            CreateInvoice("old-paid", "settled-client", "2023-01-01", "Paid", 0m),
            CreateInvoice("old-unpaid", "open-client", "2023-01-01", "Unpaid", 100m),
            CreateInvoice("recent-paid", "recent-client", "2025-01-01", "Paid", 0m));
        await context.SaveChangesAsync();

        var service = new RetentionArchiveService();
        var archivedCount = await service.ArchiveEligibleRecordsAsync(
            context,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, archivedCount);
        Assert.True((await context.Invoices.FindAsync("old-paid"))!.Archived);
        Assert.False((await context.Invoices.FindAsync("old-unpaid"))!.Archived);
        Assert.False((await context.Invoices.FindAsync("recent-paid"))!.Archived);
        Assert.True((await context.Clients.FindAsync("inactive-client"))!.Archived);
        Assert.False((await context.Clients.FindAsync("open-client"))!.Archived);
        var openInvoice = await context.Invoices.FindAsync("old-unpaid");
        openInvoice!.Archived = true;
        await BillingComputationService.SyncClientBalanceAsync("open-client", context);
        Assert.Equal(100m, (await context.Clients.FindAsync("open-client"))!.CurrentBalance);
        Assert.Equal(3, await context.Invoices.CountAsync());
        Assert.Equal(2, await context.AuditLogs.CountAsync());
    }

    [Fact]
    public void CanViewArchivedRecords_ShouldOnlyAllowFinanceManagerAndHeadAccountant()
    {
        var accountant = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Accountant")],
            "test"));
        var manager = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Finance Manager")],
            "test"));
        var headAccountant = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Head Accountant")],
            "test"));

        Assert.False(ArchivedRecordAccess.CanViewArchivedRecords(accountant));
        Assert.True(ArchivedRecordAccess.CanViewArchivedRecords(manager));
        Assert.True(ArchivedRecordAccess.CanViewArchivedRecords(headAccountant));
    }

    private static Client CreateClient(string id, string status, string lastTransaction)
    {
        return new Client
        {
            Id = id,
            ClientCode = id,
            Name = id,
            BusinessName = id,
            Status = status,
            IsActive = true,
            DateRegistered = "2020-01-01",
            LastTransaction = lastTransaction,
            CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static Invoice CreateInvoice(string id, string clientId, string billingDate, string paymentStatus, decimal balance)
    {
        return new Invoice
        {
            Id = id,
            InvoiceNo = id,
            ClientId = clientId,
            ClientName = clientId,
            BillingDate = billingDate,
            TotalAmount = 100m,
            AmountPaid = balance == 0m ? 100m : 0m,
            Balance = balance,
            PaymentStatus = paymentStatus
        };
    }
}
