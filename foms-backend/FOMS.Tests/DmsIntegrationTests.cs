using System;
using System.Collections.Generic;
using System.Net.Http;
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
using FOMS.Application.Contracts;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using FOMS.Infrastructure.Services;
using Xunit;

namespace FOMS.Tests;

public class DmsIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _appContext;
    private readonly FomsDbContext _fomsContext;
    private readonly Mock<ILogger<DmsIntegrationService>> _serviceLoggerMock;
    private readonly Mock<ILogger<DmsIntegrationController>> _controllerLoggerMock;
    private readonly DmsIntegrationOptions _options;

    public DmsIntegrationTests()
    {
        var appDbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _appContext = new ApplicationDbContext(appDbOptions);

        var fomsDbOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _fomsContext = new FomsDbContext(fomsDbOptions);

        _serviceLoggerMock = new Mock<ILogger<DmsIntegrationService>>();
        _controllerLoggerMock = new Mock<ILogger<DmsIntegrationController>>();
        _options = new DmsIntegrationOptions
        {
            BaseUrl = "https://speedex-dms.azurewebsites.net",
            InboundApiKey = "test-inbound-dms-key",
            OutboundApiKey = "test-outbound-dms-key"
        };
    }

    public void Dispose()
    {
        _appContext.Database.EnsureDeleted();
        _appContext.Dispose();
        _fomsContext.Database.EnsureDeleted();
        _fomsContext.Dispose();
    }

    private DmsIntegrationService CreateService(HttpClient? customClient = null)
    {
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var client = customClient ?? new HttpClient();
        mockHttpFactory.Setup(f => f.CreateClient(DmsIntegrationService.HttpClientName))
            .Returns(client);

        return new DmsIntegrationService(
            _appContext,
            _fomsContext,
            mockHttpFactory.Object,
            Options.Create(_options),
            _serviceLoggerMock.Object);
    }

    private DmsIntegrationController CreateController(IDmsIntegrationService service, string? apiKey = null, bool isAuthenticatedUser = false)
    {
        var controller = new DmsIntegrationController(
            service,
            Options.Create(_options),
            _controllerLoggerMock.Object);

        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpContext.Request.Headers["X-Api-Key"] = apiKey;
        }

        if (isAuthenticatedUser)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "testuser"),
                new Claim(ClaimTypes.Role, "Admin")
            }, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task DeliveryCompleted_CreatesWaybill_And_GeneratesInvoice()
    {
        var service = CreateService();
        var dto = new DmsDeliveryCompletedDto
        {
            WaybillNumber = "WB-COMP-001",
            ClientName = "Acme Corp",
            DeliveryArea = "NCR",
            ActualWeight = 2.5m,
            PodImageUrl = "https://speedex.app/pod/001.jpg",
            RecipientName = "John Doe",
            AssignedDriver = "Driver Alex"
        };

        var result = await service.ProcessDeliveryCompletedAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("WB-COMP-001", result.WaybillNumber);
        Assert.NotNull(result.InvoiceNumber);
        Assert.True(result.ComputedAmount > 0);

        var savedWaybill = await _fomsContext.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == "WB-COMP-001");
        Assert.NotNull(savedWaybill);
        Assert.Equal("Delivered", savedWaybill.DeliveryStatus);
        Assert.NotEqual("Billing Hold", savedWaybill.PodStatus);
    }

    [Fact]
    public async Task DeliveryFailed_SetsBillingHold_And_RecordsFailureReason()
    {
        var service = CreateService();
        var dto = new DmsDeliveryFailedDto
        {
            WaybillNumber = "WB-FAIL-001",
            FailureReason = "Customer Unreachable",
            AttemptCount = 1
        };

        var result = await service.ProcessDeliveryFailedAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.True(result.BillingHoldActive);

        var savedWaybill = await _fomsContext.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == "WB-FAIL-001");
        Assert.NotNull(savedWaybill);
        Assert.Equal("Failed", savedWaybill.PodStatus);
        Assert.Equal("Failed", savedWaybill.DeliveryStatus);
        Assert.False(savedWaybill.IsBilled);
    }

    [Fact]
    public async Task RedeliveryCompleted_LiftsBillingHold_And_CompletesDelivery()
    {
        var service = CreateService();

        // Setup failed order
        await service.ProcessDeliveryFailedAsync(new DmsDeliveryFailedDto
        {
            WaybillNumber = "WB-REDEL-001",
            FailureReason = "First Attempt Failed",
            AttemptCount = 1
        });

        // Redelivery succeeds
        var redeliveryDto = new DmsRedeliveryCompletedDto
        {
            WaybillNumber = "WB-REDEL-001",
            NewPodImageUrl = "https://speedex.app/pod/redel-001.jpg",
            RecipientName = "Jane Doe",
            RedeliveryAttemptCount = 2
        };

        var result = await service.ProcessRedeliveryCompletedAsync(redeliveryDto);

        Assert.True(result.IsSuccess);
        Assert.False(result.BillingHoldActive);
        Assert.Equal("HoldLiftedAndBilled", result.Status);

        var savedWaybill = await _fomsContext.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == "WB-REDEL-001");
        Assert.NotNull(savedWaybill);
        Assert.Equal("Delivered", savedWaybill.DeliveryStatus);
        Assert.True(savedWaybill.IsBilled);
    }

    [Fact]
    public async Task CashAdvance_And_Liquidation_RecordsCorrectly()
    {
        var service = CreateService();

        // Request Cash Advance
        var caResult = await service.ProcessCashAdvanceRequestAsync(new DmsCashAdvanceRequestDto
        {
            RequestId = "CA-TEST-001",
            DeliveryReference = "TRIP-999",
            RequesterName = "Driver Juan",
            Purpose = "Fuel and Toll",
            RequestedAmount = 1500.00m
        });

        Assert.True(caResult.IsSuccess);
        Assert.NotNull(caResult.AdvanceNumber);
        Assert.Equal("Approved", caResult.Status);

        // Submit Liquidation
        var liqResult = await service.ProcessLiquidationSubmissionAsync(new DmsLiquidationSubmissionDto
        {
            RequestId = "LIQ-TEST-001",
            FomsCashAdvanceId = caResult.CashAdvanceId,
            SubmittedByName = "Driver Juan",
            TotalAmount = 1450.00m,
            Receipts = new List<DmsLiquidationReceiptItemDto>
            {
                new() { Description = "Fuel", Amount = 1200.00m },
                new() { Description = "Toll", Amount = 250.00m }
            }
        });

        Assert.True(liqResult.IsSuccess);
        Assert.NotNull(liqResult.LiquidationId);
        Assert.True(liqResult.LiquidationId.Value > 0);
        Assert.Equal("Validated", liqResult.Status);
    }

    [Fact]
    public async Task Controller_Rejects_InvalidApiKey()
    {
        var service = CreateService();
        var controller = CreateController(service, apiKey: "wrong-api-key");

        var response = await controller.DeliveryCompleted(new DmsDeliveryCompletedRequest());

        Assert.IsType<UnauthorizedObjectResult>(response);
    }

    [Fact]
    public async Task Controller_Accepts_ValidApiKey()
    {
        var service = CreateService();
        var controller = CreateController(service, apiKey: "test-inbound-dms-key");

        var response = await controller.DeliveryCompleted(new DmsDeliveryCompletedRequest
        {
            WaybillNumber = "WB-API-TEST-001",
            ClientName = "VIP Client",
            DeliveryArea = "NCR",
            ActualWeight = 1.0m,
            PodImageUrl = "https://speedex.app/pod/test.jpg"
        });

        Assert.IsType<OkObjectResult>(response);
    }
}
