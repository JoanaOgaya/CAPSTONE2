using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using FOMS.Api.Controllers;
using FOMS.Infrastructure.Persistence;
using Xunit;
using Payment = FOMS.Domain.Entities.Payment;
using Invoice = FOMS.Domain.Entities.Invoice;
using InvoiceDocumentStatus = FOMS.Domain.Entities.InvoiceDocumentStatus;

namespace FOMS.Tests;

public class PaymentApprovalSecurityAndIntegrityTests
{
    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_App_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateFomsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_Foms_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string role, string name, string empId, bool authenticated = true)
    {
        if (!authenticated)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new(ClaimTypes.Name, name),
            new("name", name),
            new(ClaimTypes.NameIdentifier, empId),
            new("sub", empId),
            new("employeeNumber", empId),
            new("employee_id", empId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static void SetContext(ControllerBase controller, ClaimsPrincipal principal)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task TC01_SelfApproval_ShouldReturn403Forbidden()
    {
        using var appDb = CreateAppDb(nameof(TC01_SelfApproval_ShouldReturn403Forbidden));
        using var fomsDb = CreateFomsDb(nameof(TC01_SelfApproval_ShouldReturn403Forbidden));

        var payment = new Payment
        {
            Id = "PAY-SELF-01",
            InvoiceId = "INV-01",
            Amount = 1000m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-001",
            RecordedByEmployeeId = "EMP-001"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-01",
            InvoiceNo = "INV-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal("FINANCE_MANAGER", "FM User", "EMP-001"));

        var result = await controller.ApprovePayment("PAY-SELF-01", new FinancePaymentsController.ValidationActionRequest("approve"));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
    }

    [Theory]
    [InlineData("HEAD_ACCOUNTANT", "EMP-002", "Head Accountant User")]
    [InlineData("ACCOUNTANT", "EMP-003", "Accountant User")]
    [InlineData("ASSISTANT_FM", "EMP-004", "Assistant FM User")]
    public async Task TC02_WrongRole_OnFinalApprove_ShouldReturn403Forbidden(string role, string empId, string name)
    {
        using var appDb = CreateAppDb(nameof(TC02_WrongRole_OnFinalApprove_ShouldReturn403Forbidden));
        using var fomsDb = CreateFomsDb(nameof(TC02_WrongRole_OnFinalApprove_ShouldReturn403Forbidden));

        var payment = new Payment
        {
            Id = "PAY-ROLE-01",
            InvoiceId = "INV-02",
            Amount = 500m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-02",
            InvoiceNo = "INV-02",
            TotalAmount = 500m,
            Balance = 500m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal(role, name, empId));

        var result = await controller.ApprovePayment("PAY-ROLE-01", new FinancePaymentsController.ValidationActionRequest("approve"));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
    }

    [Fact]
    public async Task TC03_NoToken_Unauthenticated_ShouldReturn401Unauthorized()
    {
        using var appDb = CreateAppDb(nameof(TC03_NoToken_Unauthenticated_ShouldReturn401Unauthorized));
        using var fomsDb = CreateFomsDb(nameof(TC03_NoToken_Unauthenticated_ShouldReturn401Unauthorized));

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal("", "", "", authenticated: false));

        var result = await controller.ApprovePayment("PAY-ANY", new FinancePaymentsController.ValidationActionRequest("approve"));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task TC04_VoidInvoice_PaymentApproval_ShouldReturn400BadRequest()
    {
        using var appDb = CreateAppDb(nameof(TC04_VoidInvoice_PaymentApproval_ShouldReturn400BadRequest));
        using var fomsDb = CreateFomsDb(nameof(TC04_VoidInvoice_PaymentApproval_ShouldReturn400BadRequest));

        var payment = new Payment
        {
            Id = "PAY-VOID-01",
            InvoiceId = "INV-VOID-01",
            Amount = 1000m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-VOID-01",
            InvoiceNo = "INV-VOID-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DocumentStatus = InvoiceDocumentStatus.Void
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal("FINANCE_MANAGER", "FM User", "EMP-001"));

        var result = await controller.ApprovePayment("PAY-VOID-01", new FinancePaymentsController.ValidationActionRequest("approve"));

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Void", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task TC05_Overpayment_Approval_ShouldReturn400BadRequest()
    {
        using var appDb = CreateAppDb(nameof(TC05_Overpayment_Approval_ShouldReturn400BadRequest));
        using var fomsDb = CreateFomsDb(nameof(TC05_Overpayment_Approval_ShouldReturn400BadRequest));

        var payment = new Payment
        {
            Id = "PAY-OVER-01",
            InvoiceId = "INV-OVER-01",
            Amount = 1500m, // Exceeds balance of 1000
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-OVER-01",
            InvoiceNo = "INV-OVER-01",
            TotalAmount = 1000m,
            Balance = 1000m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal("FINANCE_MANAGER", "FM User", "EMP-001"));

        var result = await controller.ApprovePayment("PAY-OVER-01", new FinancePaymentsController.ValidationActionRequest("approve"));

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Overpayment", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task TC06_DoubleSubmit_IdempotentApproval_ShouldReturn200WithoutDuplicateReceipts()
    {
        using var appDb = CreateAppDb(nameof(TC06_DoubleSubmit_IdempotentApproval_ShouldReturn200WithoutDuplicateReceipts));
        using var fomsDb = CreateFomsDb(nameof(TC06_DoubleSubmit_IdempotentApproval_ShouldReturn200WithoutDuplicateReceipts));

        var payment = new Payment
        {
            Id = "PAY-IDEM-01",
            InvoiceId = "INV-IDEM-01",
            InvoiceNo = "INV-IDEM-01",
            ClientId = "CLI-IDEM-01",
            ClientName = "Apex Logistics Inc.",
            Amount = 1000m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-IDEM-01",
            InvoiceNo = "INV-IDEM-01",
            ClientId = "CLI-IDEM-01",
            ClientName = "Apex Logistics Inc.",
            TotalAmount = 1000m,
            Balance = 1000m,
            AmountPaid = 0m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetContext(controller, CreatePrincipal("FINANCE_MANAGER", "FM User", "EMP-001"));

        // First approval submit
        var firstResult = await controller.ApprovePayment("PAY-IDEM-01", new FinancePaymentsController.ValidationActionRequest("approve"));
        Assert.IsType<OkObjectResult>(firstResult);

        // Second approval submit (double click / network retry)
        var secondResult = await controller.ApprovePayment("PAY-IDEM-01", new FinancePaymentsController.ValidationActionRequest("approve"));
        Assert.IsType<OkObjectResult>(secondResult);

        // Verification: Exactly 1 OfficialReceipt issued for this PaymentId
        var receipts = await appDb.OfficialReceipts.Where(r => r.PaymentId == "PAY-IDEM-01").ToListAsync();
        Assert.Single(receipts);

        // Invoice balance only reduced once
        var updatedInvoice = await appDb.Invoices.FirstAsync(i => i.Id == "INV-IDEM-01");
        Assert.Equal(0m, updatedInvoice.Balance);
        Assert.Equal(1000m, updatedInvoice.AmountPaid);
    }

    [Fact]
    public async Task TC07_PaymentsController_ApprovalEndpoints_RouteToInternalAndEnforceIdempotency()
    {
        using var appDb = CreateAppDb(nameof(TC07_PaymentsController_ApprovalEndpoints_RouteToInternalAndEnforceIdempotency));
        using var fomsDb = CreateFomsDb(nameof(TC07_PaymentsController_ApprovalEndpoints_RouteToInternalAndEnforceIdempotency));

        var payment = new Payment
        {
            Id = "PAY-CTRL-01",
            InvoiceId = "INV-CTRL-01",
            Amount = 800m,
            PaymentStatus = "Validated",
            RecordedBy = "EMP-003",
            RecordedByEmployeeId = "EMP-003"
        };
        appDb.Payments.Add(payment);

        var invoice = new Invoice
        {
            Id = "INV-CTRL-01",
            InvoiceNo = "INV-CTRL-01",
            TotalAmount = 800m,
            Balance = 800m,
            AmountPaid = 0m,
            DocumentStatus = InvoiceDocumentStatus.Approved
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var paymentsController = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance);
        SetContext(paymentsController, CreatePrincipal("FINANCE_MANAGER", "FM User", "EMP-001"));

        // Call POST /api/payments/{id}/approve
        var result = await paymentsController.ApprovePayment("PAY-CTRL-01", new PaymentDto());
        Assert.IsType<OkObjectResult>(result);

        // Call PUT /payments/{id} with status = Approved
        var putResult = await paymentsController.UpdatePayment("PAY-CTRL-01", new PaymentDto { Status = "Approved" });
        Assert.IsType<OkObjectResult>(putResult);

        var receipts = await appDb.OfficialReceipts.Where(r => r.PaymentId == "PAY-CTRL-01").ToListAsync();
        Assert.Single(receipts);
    }

    [Fact]
    public async Task TC08_HeadAccountantController_ApprovePayment_Returns403Forbidden()
    {
        using var fomsDb = CreateFomsDb(nameof(TC08_HeadAccountantController_ApprovePayment_Returns403Forbidden));

        var haController = new HeadAccountantController(fomsDb);
        SetContext(haController, CreatePrincipal("HEAD_ACCOUNTANT", "HA User", "EMP-002"));

        var result = await haController.ApprovePayment("PAY-ANY");
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
    }
}
