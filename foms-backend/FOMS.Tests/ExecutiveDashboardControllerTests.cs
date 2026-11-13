using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class ExecutiveDashboardControllerTests
{
    [Fact]
    public async Task Get_RejectsNonFinanceManager()
    {
        await using var appDb = CreateApplicationDbContext();
        await using var fomsDb = CreateFomsDbContext();
        var controller = CreateController(appDb, fomsDb, "Accountant");

        var result = await controller.Get(null);

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsAggregatesForSelectedClient()
    {
        await using var appDb = CreateApplicationDbContext();
        await using var fomsDb = CreateFomsDbContext();
        appDb.Invoices.AddRange(
            new Invoice
            {
                Id = "invoice-a",
                InvoiceNo = "INV-001",
                ClientId = "client-a",
                ClientName = "Acme",
                DueDate = DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd"),
                TotalAmount = 1000m,
                AmountPaid = 400m,
                Balance = 600m
            },
            new Invoice
            {
                Id = "invoice-b",
                InvoiceNo = "INV-002",
                ClientId = "client-b",
                ClientName = "Other Client",
                DueDate = DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd"),
                TotalAmount = 500m,
                Balance = 500m
            });
        appDb.Payments.Add(new Payment
        {
            Id = "payment-a",
            ClientId = "client-a",
            ClientName = "Acme",
            Amount = 400m,
            Flow = "inflow",
            PaymentStatus = "Validated"
        });
        appDb.CashFlowTransactions.Add(new CashFlowTransaction
        {
            Id = "cashflow-in",
            TransactionDate = DateTime.UtcNow.Date,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Type = "Inflow",
            Amount = 250m
        });
        appDb.AuditLogs.Add(new AuditLog
        {
            Id = "audit-current",
            LoggedAt = DateTime.UtcNow,
            UserId = "EMP-001",
            EntityName = "Invoice",
            EntityId = "invoice-a",
            Action = "Invoice approved",
            Details = "Invoice INV-001 was approved."
        });
        await appDb.SaveChangesAsync();

        var controller = CreateController(appDb, fomsDb, "Finance Manager");
        var result = await controller.Get("client-a");
        var json = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal(1, data.GetProperty("totalInvoicesBilled").GetInt32());
        Assert.Equal(1000m, data.GetProperty("totalInvoiceAmount").GetDecimal());
        Assert.Equal(400m, data.GetProperty("totalCollected").GetDecimal());
        Assert.Equal(600m, data.GetProperty("totalArOutstanding").GetDecimal());
        Assert.Equal(40d, data.GetProperty("collectionRate").GetDouble());
        Assert.Equal(1, data.GetProperty("accountsNearDueDate").GetInt32());
        Assert.Equal("Acme", data.GetProperty("nearDueAccounts")[0].GetProperty("clientName").GetString());
        Assert.Equal(250m, data.GetProperty("cashInflowThisMonth").GetDecimal());
        Assert.Contains(
            data.GetProperty("cashFlowAnalytics").EnumerateArray(),
            period => period.GetProperty("inflow").GetDecimal() == 250m);
        var recentActivity = data.GetProperty("recentActivity");
        Assert.Single(recentActivity.EnumerateArray());
        Assert.Equal("Invoice approved", recentActivity[0].GetProperty("action").GetString());
        Assert.Equal("EMP-001", recentActivity[0].GetProperty("userFullName").GetString());

        var auditPageResult = Assert.IsType<OkObjectResult>(await new AuditLogsController(appDb).GetAll());
        using var auditPageDocument = JsonDocument.Parse(JsonSerializer.Serialize(auditPageResult.Value));
        Assert.True(auditPageDocument.RootElement.ValueKind != JsonValueKind.Undefined);
    }

    [Fact]
    public async Task RecordedAndApprovedPayment_AppearsInDashboardInflow()
    {
        await using var appDb = CreateApplicationDbContext();
        await using var fomsDb = CreateFomsDbContext();

        // 1. Seed approved invoice
        var invoice = new Invoice
        {
            Id = "inv-dash-flow",
            InvoiceNo = "BI-DASH-001",
            ClientId = "client-dash",
            ClientName = "Acme Corp",
            TotalAmount = 5000m,
            Balance = 5000m,
            AmountPaid = 0m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);

        // 2. Record payment by EMP-003 Accountant
        var payment = new Payment
        {
            Id = "pay-dash-01",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            ClientName = invoice.ClientName,
            Amount = 3000m,
            Flow = "inflow",
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003",
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-DASH-001"
        };
        appDb.Payments.Add(payment);
        await appDb.SaveChangesAsync();

        // 3. Approve payment by EMP-001 Finance Manager
        var mediatorMock = new Moq.Mock<MediatR.IMediator>();
        var finPaymentsController = new FinancePaymentsController(appDb, fomsDb, mediatorMock.Object);
        finPaymentsController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "Finance Manager"),
                    new Claim("role", "Finance Manager"),
                    new Claim("employeeNumber", "EMP-001"),
                    new Claim("name", "Fiona Manager")
                }, "TestAuth"))
            }
        };

        var approveResult = await finPaymentsController.ApprovePayment(payment.Id, new FinancePaymentsController.ValidationActionRequest("approve"));
        Assert.IsType<OkObjectResult>(approveResult);

        // 4. Check ExecutiveDashboardController.Get()
        var dashboardController = CreateController(appDb, fomsDb, "Finance Manager");
        var dashboardResult = await dashboardController.Get(null);
        var okResult = Assert.IsType<OkObjectResult>(dashboardResult);

        var json = JsonSerializer.Serialize(okResult.Value);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");

        // Verify payment is approved and appears in dashboard cashInflowThisMonth
        Assert.Equal(3000m, data.GetProperty("cashInflowThisMonth").GetDecimal());
        Assert.Equal(3000m, data.GetProperty("totalCollected").GetDecimal());
        Assert.Equal(2000m, data.GetProperty("totalArOutstanding").GetDecimal());

        // Verify topPayingClients includes the client
        var topClients = data.GetProperty("topPayingClients").EnumerateArray().ToList();
        Assert.Contains(topClients, c => c.GetProperty("clientName").GetString() == "Acme Corp" && c.GetProperty("amount").GetDecimal() == 3000m);
    }

    private static ApplicationDbContext CreateApplicationDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateFomsDbContext()
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FomsDbContext(options);
    }

    private static ExecutiveDashboardController CreateController(
        ApplicationDbContext appDb,
        FomsDbContext fomsDb,
        string role)
    {
        var controller = new ExecutiveDashboardController(appDb, fomsDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, role) },
                    "TestAuth"))
            }
        };
        return controller;
    }
}