using System.Security.Claims;
using System.Text.Json;
using FOMS.Api.Controllers;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Tests;

public class ExecutiveDashboardControllerTests
{
    [Fact]
    public async Task Get_RejectsNonFinancialManager()
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
        fomsDb.Invoices.AddRange(
            new FOMS.Domain.Entities.SqlServer.Invoice
            {
                Id = "invoice-a",
                InvoiceNo = "INV-001",
                ClientIdStr = "client-a",
                ClientName = "Acme",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
                DueDateStr = DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd"),
                TotalAmount = 1000m,
                AmountPaid = 400m,
                Balance = 600m
            },
            new FOMS.Domain.Entities.SqlServer.Invoice
            {
                Id = "invoice-b",
                InvoiceNo = "INV-002",
                ClientIdStr = "client-b",
                ClientName = "Other Client",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
                DueDateStr = DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd"),
                TotalAmount = 500m,
                Balance = 500m
            });
        fomsDb.Payments.Add(new FOMS.Domain.Entities.SqlServer.Payment
        {
            Id = "payment-a",
            ClientIdStr = "client-a",
            ClientName = "Acme",
            Amount = 400m,
            Flow = "inflow",
            PaymentStatus = "Validated"
        });
        fomsDb.CashInflows.Add(new FOMS.Domain.Entities.SqlServer.CashInflow
        {
            InflowId = 1,
            TransactionDate = DateTime.UtcNow.Date,
            Amount = 250m
        });
        fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            audit_id = 1,
            created_at = DateTime.UtcNow,
            user_id = 1,
            role = "Financial Manager",
            action = "Invoice approved",
            module = "Invoices",
            remarks = "Invoice INV-001 was approved."
        });
        fomsDb.Users.Add(new FOMS.Domain.Entities.SqlServer.User
        {
            UserId = 1,
            EmployeeNumber = "EMP-001",
            FullName = "EMP-001",
            Username = "emp001"
        });
        await fomsDb.SaveChangesAsync();

        var controller = CreateController(appDb, fomsDb, "Financial Manager");
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

        var auditController = new AuditLogsController(fomsDb)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Role, "Head Accountant"),
                        new Claim(ClaimTypes.NameIdentifier, "EMP-001")
                    }, "TestAuth"))
                }
            }
        };
        var auditPageResult = Assert.IsType<OkObjectResult>(await auditController.GetAll());
        using var auditPageDocument = JsonDocument.Parse(JsonSerializer.Serialize(auditPageResult.Value));
        Assert.NotNull(auditPageDocument.RootElement);
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
        var controller = new ExecutiveDashboardController(fomsDb);
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
