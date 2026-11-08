using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using FOMS.Api.Controllers;
using FOMS.Application.Interfaces;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using FOMS.Infrastructure.Services;
using Xunit;

namespace FOMS.Tests;

public class StarsIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<StarsIntegrationService>> _loggerMock;
    private readonly StarsIntegrationOptions _options;

    public StarsIntegrationTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(dbOptions);

        _loggerMock = new Mock<ILogger<StarsIntegrationService>>();
        _options = new StarsIntegrationOptions
        {
            StarsBaseUrl = "https://stars.test.local",
            InboundApiKey = "test-inbound-stars-key",
            OutboundApiKey = "test-outbound-stars-key"
        };
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private StarsIntegrationService CreateService(HttpClient? customClient = null)
    {
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var client = customClient ?? new HttpClient();
        mockHttpFactory.Setup(f => f.CreateClient(StarsIntegrationService.HttpClientName))
            .Returns(client);

        return new StarsIntegrationService(
            _context,
            mockHttpFactory.Object,
            Options.Create(_options),
            _loggerMock.Object);
    }

    [Fact]
    public void ValidateApiKey_RejectsMissingOrMismatch_AcceptsMatch()
    {
        Assert.False(StarsIntegrationService.ValidateApiKey(null, "expected-key"));
        Assert.False(StarsIntegrationService.ValidateApiKey("", "expected-key"));
        Assert.False(StarsIntegrationService.ValidateApiKey("wrong-key", "expected-key"));
        Assert.True(StarsIntegrationService.ValidateApiKey("expected-key", "expected-key"));
    }

    [Fact]
    public async Task RequestInvoiceEndorsementAsync_DispatchesPayload_CreatesEndorsementLink()
    {
        var responsePayload = new
        {
            isSuccess = true,
            data = new
            {
                starsTaskId = Guid.NewGuid().ToString(),
                starsTaskNumber = "TASK8888",
                invoiceNumber = "INV-2026-TEST",
                assignedTo = "Coord Test",
                message = "Task created"
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.ToString().Contains("/api/integration/foms/endorsement-request")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            });

        var service = CreateService(new HttpClient(handlerMock.Object));

        var invoice = new Invoice
        {
            Id = "inv-1",
            InvoiceNo = "INV-2026-TEST",
            ClientName = "Acme Corp",
            TotalAmount = 50000m,
            Balance = 50000m,
            PaymentStatus = "Unpaid",
            DocumentStatus = "Approved"
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(default);

        var (isSuccess, message, data) = await service.RequestInvoiceEndorsementAsync("INV-2026-TEST", "Please endorse");

        Assert.True(isSuccess);
        Assert.NotNull(data);
        Assert.Equal("TASK8888", data.StarsTaskNumber);

        var link = await _context.StarsInvoiceEndorsementLinks.FirstOrDefaultAsync(l => l.InvoiceNumber == "INV-2026-TEST");
        Assert.NotNull(link);
        Assert.Equal("TASK8888", link.StarsTaskNumber);
        Assert.Equal("EndorsementRequested", link.Status);
    }

    [Fact]
    public async Task ProcessEndorsementCompletionAsync_CalculatesStandard30DayPaymentTerm_UpdatesInvoiceAndLink()
    {
        var service = CreateService();

        var invoice = new Invoice
        {
            Id = "inv-endorsed",
            InvoiceNo = "INV-ENDORSED-001",
            ClientName = "Metro Logistics",
            TotalAmount = 35000m,
            Balance = 35000m,
            PaymentStatus = "Unpaid",
            PaymentValidationStatus = "Pending Endorsement"
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(default);

        var receivedDate = new DateTime(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc);
        var expectedDueDate = receivedDate.AddDays(30).ToString("yyyy-MM-dd");

        var callback = new StarsEndorsementCompletionDto
        {
            InvoiceNumber = "INV-ENDORSED-001",
            StarsTaskId = Guid.NewGuid(),
            StarsTaskNumber = "STARS999",
            CompletionResult = "Completed",
            ActualReceivedDate = receivedDate,
            SignedCopyUrl = "https://stars.blob.core.windows.net/signed/inv-endorsed.pdf",
            DeliveryRemarks = "Signed by Accounting Manager",
            CompletedBy = "Operations Coordinator"
        };

        var (isSuccess, message) = await service.ProcessEndorsementCompletionAsync(callback);

        Assert.True(isSuccess);
        Assert.Contains("30-day payment term calculated", message);

        var updatedInvoice = await _context.Invoices.FindAsync("inv-endorsed");
        Assert.Equal(expectedDueDate, updatedInvoice!.DueDate);
        Assert.Equal("Validated", updatedInvoice.PaymentValidationStatus);

        var link = await _context.StarsInvoiceEndorsementLinks.FirstOrDefaultAsync(l => l.InvoiceNumber == "INV-ENDORSED-001");
        Assert.NotNull(link);
        Assert.Equal("Endorsed", link.Status);
        Assert.Equal(receivedDate, link.ActualReceivedDate);
        Assert.Equal("https://stars.blob.core.windows.net/signed/inv-endorsed.pdf", link.SignedCopyUrl);
    }

    [Fact]
    public async Task NotifyPaymentStatusChangeAsync_PushesPaymentUpdateToStars()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.ToString().Contains("/api/integration/foms/payment-status")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"isSuccess\":true}")
            });

        var service = CreateService(new HttpClient(handlerMock.Object));

        var invoice = new Invoice
        {
            Id = "inv-pay",
            InvoiceNo = "INV-PAY-001",
            ClientName = "Pay Corp",
            TotalAmount = 25000m,
            AmountPaid = 25000m,
            Balance = 0m,
            PaymentStatus = "Paid"
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(default);

        var (isSuccess, message) = await service.NotifyPaymentStatusChangeAsync("INV-PAY-001");
        Assert.True(isSuccess);
        Assert.Contains("successfully", message);
    }

    [Fact]
    public async Task SendOverdueAlertAsync_PushesOverdueAlertToStars()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.ToString().Contains("/api/integration/foms/overdue-alert")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"isSuccess\":true}")
            });

        var service = CreateService(new HttpClient(handlerMock.Object));

        var invoice = new Invoice
        {
            Id = "inv-overdue",
            InvoiceNo = "INV-OVERDUE-999",
            ClientName = "Late Payer Ltd",
            TotalAmount = 80000m,
            Balance = 80000m,
            DaysOverdue = 45,
            AgingBucket = "31-60",
            PaymentStatus = "Unpaid"
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(default);

        var (isSuccess, message) = await service.SendOverdueAlertAsync("INV-OVERDUE-999");
        Assert.True(isSuccess);
        Assert.Contains("successfully", message);
    }

    [Fact]
    public async Task IntegrationController_Rejects_MissingApiKeyHeader_OnM2MEndpoint()
    {
        var service = CreateService();
        var mockUserService = new Mock<IUserService>();
        var fomsDbOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var fomsDb = new FomsDbContext(fomsDbOptions);

        var controller = new IntegrationController(
            service,
            mockUserService.Object,
            _context,
            fomsDb,
            Options.Create(_options));

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.ReceiveEndorsementCompletion(new StarsEndorsementCompletionDto());
        Assert.IsType<UnauthorizedObjectResult>(response);
    }

    [Fact]
    public async Task IntegrationController_FailsClosed_WhenInboundApiKey_NotConfigured()
    {
        _options.InboundApiKey = "";
        var service = CreateService();
        var mockUserService = new Mock<IUserService>();
        var fomsDbOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var fomsDb = new FomsDbContext(fomsDbOptions);

        var controller = new IntegrationController(
            service,
            mockUserService.Object,
            _context,
            fomsDb,
            Options.Create(_options));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "any-key";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.ReceiveEndorsementCompletion(new StarsEndorsementCompletionDto());
        var objResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(503, objResult.StatusCode);
    }

    [Fact]
    public async Task IntegrationController_AllowsAuthenticatedJwtUser_WithoutApiKey()
    {
        var service = CreateService();
        var mockUserService = new Mock<IUserService>();
        var fomsDbOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var fomsDb = new FomsDbContext(fomsDbOptions);

        var controller = new IntegrationController(
            service,
            mockUserService.Object,
            _context,
            fomsDb,
            Options.Create(_options));

        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "EMP-001"),
            new Claim(ClaimTypes.Role, "Finance Manager")
        }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.GetStarsArSummary();
        Assert.IsType<OkObjectResult>(response);
    }
}