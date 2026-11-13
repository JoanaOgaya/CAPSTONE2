using System.Security.Claims;
using FOMS.Api.Controllers;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FOMS.Tests;

public class IamAndCrmAlignmentTests
{
    private FomsDbContext CreateInMemoryMssqlDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FomsDbContext(options);
    }

    private ApplicationDbContext CreateInMemoryAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Coordinator_CannotLogin_RejectedAtServerLevel()
    {
        var authServiceMock = new Mock<IAuthService>();
        authServiceMock.Setup(a => a.LoginAsync("EMP-006", "Password@123"))
            .ThrowsAsync(new UnauthorizedAccessException("Invalid employee number or password."));

        var controller = new AuthController(authServiceMock.Object);
        var result = await controller.Login(new LoginRequestDto { EmployeeNumber = "EMP-006", Password = "Password@123" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauthorized.Value);
    }

    [Fact]
    public async Task Client_WithoutSignedProposal_IsNotBillable()
    {
        var appDb = CreateInMemoryAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateInMemoryMssqlDb(Guid.NewGuid().ToString());

        var client = new FOMS.Domain.Entities.Client
        {
            Id = "TEST-001",
            ClientCode = "TEST-001",
            Name = "Unsigned Client",
            SignedProposalAttached = false,
            SignedProposalUrl = null
        };
        appDb.Clients.Add(client);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("name", "Finance Manager"),
                    new Claim(ClaimTypes.Role, "Finance Manager")
                }))
            }
        };

        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "TEST-001",
            ClientName = "Unsigned Client",
            FreightCharges = 1000m
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("not billable", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RateUpdate_PreservesOldRate_AndCreatesNewActiveVersion()
    {
        var mssqlDb = CreateInMemoryMssqlDb(Guid.NewGuid().ToString());
        var existingRate = new ClientRate
        {
            RateId = 10,
            ClientId = 1,
            DeliveryArea = "NCR",
            ServiceType = "Standard Delivery",
            MinimumKg = 5m,
            MinimumRate = 100m,
            ExcessRatePerKg = 25m,
            OdaRate = 0m,
            ValuationRate = 0.01m,
            FuelSurchargeRate = 0.15m,
            ApproverName = "Previous Approver",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-2),
            UpdatedAt = DateTime.UtcNow.AddMonths(-2)
        };
        mssqlDb.ClientRates.Add(existingRate);
        await mssqlDb.SaveChangesAsync();

        var controller = new BillingRatesController(mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, "Crystalyn Joyce C. Fajardo"),
                    new Claim(ClaimTypes.Role, "Finance Manager")
                }))
            }
        };

        var result = await controller.Update(10, new BillingRateDto
        {
            MinimumRate = 130m,
            ExcessRate = 20m,
            OdaRate = 45m,
            FuelSurchargeRate = 0.16m,
            ApproverName = "Crystalyn Joyce C. Fajardo",
            EffectiveDate = "2026-11-10",
            Region = "NCR"
        });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Check DB state: old rate deactivated, new rate created
        var oldRate = await mssqlDb.ClientRates.FindAsync(10);
        Assert.NotNull(oldRate);
        Assert.False(oldRate.IsActive);
        Assert.Equal(new DateOnly(2026, 11, 10), oldRate.EffectiveTo);

        var newRates = await mssqlDb.ClientRates.Where(r => r.ClientId == 1 && r.IsActive == true).ToListAsync();
        Assert.Single(newRates);
        var activeRate = newRates[0];
        Assert.Equal(130m, activeRate.MinimumRate);
        Assert.Equal("Crystalyn Joyce C. Fajardo", activeRate.ApproverName);
        Assert.Equal(new DateOnly(2026, 11, 10), activeRate.EffectiveFrom);
    }

    [Fact]
    public async Task InvoicesQuery_FiltersByClientId_PreventingCrossClientExposure()
    {
        var appDb = CreateInMemoryAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateInMemoryMssqlDb(Guid.NewGuid().ToString());

        appDb.Invoices.AddRange(
            new FOMS.Domain.Entities.Invoice { Id = "INV-001", InvoiceNo = "LZD-2026-0001", ClientId = "LZD-001", ClientName = "Lazada", FreightCharges = 5000m, TotalAmount = 5600m, Balance = 5600m },
            new FOMS.Domain.Entities.Invoice { Id = "INV-002", InvoiceNo = "SHP-2026-0001", ClientId = "SHP-001", ClientName = "Shopee", FreightCharges = 3000m, TotalAmount = 3360m, Balance = 3360m }
        );
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("name", "Lazada Client Viewer"),
                    new Claim("client_id", "LZD-001")
                }))
            }
        };

        var result = await controller.GetInvoices("LZD-001");
        var okResult = Assert.IsType<OkObjectResult>(result);
        var invoices = okResult.Value as System.Collections.IEnumerable;
        Assert.NotNull(invoices);

        int count = 0;
        foreach (var item in invoices)
        {
            count++;
            var clientIdProp = item.GetType().GetProperty("clientId")?.GetValue(item)?.ToString();
            Assert.Equal("LZD-001", clientIdProp);
        }
        Assert.Equal(1, count);
    }
}
