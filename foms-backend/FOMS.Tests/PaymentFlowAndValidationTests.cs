using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FOMS.Api.Controllers;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using SqlServer = FOMS.Domain.Entities.SqlServer;
using Xunit;

namespace FOMS.Tests;

public class PaymentFlowAndValidationTests
{
    private ApplicationDbContext CreateAppDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private FomsDbContext CreateFomsDbContext()
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private ClaimsPrincipal CreatePrincipal(string role, string name = "Test User", string empId = "EMP-999")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, empId),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("employeeNumber", empId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 1: Record Payment with valid method, proof upload (PDF/JPG/PNG),
    // and collector reference number = OR -> status Pending Validation.
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Check", "receipt.pdf")]
    [InlineData("Bank Transfer", "receipt.jpg")]
    [InlineData("Cash", "receipt.png")]
    public async Task TC01_RecordPayment_ValidMethodsAndProof_ShouldBePendingValidation(string method, string proofFilename)
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNo = "INV-001",
            ClientId = "CLI-001",
            ClientName = "Acme Corp",
            TotalAmount = 5000m,
            AmountPaid = 0m,
            Balance = 5000m,
            PaymentStatus = "Unpaid"
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant", "Mariel", "EMP-003") }
            }
        };

        var dto = new PaymentDto
        {
            InvoiceId = "INV-001",
            Amount = 5000m,
            PaymentMethod = method,
            CollectorReferenceNumber = "OR-COL-8899",
            ProofOfPaymentUrl = proofFilename,
            PaymentDate = "2026-11-06"
        };

        var result = await controller.RecordPayment(dto);
        var ok = Assert.IsType<OkObjectResult>(result);
        dynamic response = ok.Value!;

        var payment = await appDb.Payments.FirstOrDefaultAsync();
        Assert.NotNull(payment);
        Assert.Equal("Pending Validation", payment.PaymentStatus);
        Assert.Equal("OR-COL-8899", payment.OrNumber);
        Assert.Equal("OR-COL-8899", payment.ReferenceNumber);
        Assert.Equal(5000m, payment.Amount);
        Assert.Equal(method, payment.PaymentMethod);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 2: Reject Invalid Proof of Payment extension (must be PDF/JPG/PNG).
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("receipt.exe")]
    [InlineData("receipt.docx")]
    [InlineData("script.bat")]
    public async Task TC02_RecordPayment_InvalidProofFormat_ShouldReturnBadRequest(string invalidProof)
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNo = "INV-001",
            ClientId = "CLI-001",
            TotalAmount = 5000m,
            Balance = 5000m
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant") }
            }
        };

        var dto = new PaymentDto
        {
            InvoiceId = "INV-001",
            Amount = 5000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-100",
            ProofOfPaymentUrl = invalidProof
        };

        var result = await controller.RecordPayment(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("PDF, JPG, or PNG", badRequest.Value!.ToString()!);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 3: Duplicate Reference Alert & Routing to Exception Queue.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC03_DuplicateReference_AlertAndRouteToExceptionQueue()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNo = "INV-001",
            ClientId = "CLI-001",
            TotalAmount = 5000m,
            Balance = 5000m
        };
        appDb.Invoices.Add(invoice);
        appDb.Payments.Add(new Payment
        {
            Id = "PAY-EXISTING",
            InvoiceId = "INV-001",
            Amount = 5000m,
            ReferenceNumber = "DUP-REF-12345",
            OrNumber = "OR-12345",
            PaymentStatus = "Pending Validation"
        });
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant") }
            }
        };

        // Pre-validation duplicate reference check
        var checkResult = await controller.CheckReference("DUP-REF-12345", null);
        var checkOk = Assert.IsType<OkObjectResult>(checkResult);
        var json = System.Text.Json.JsonSerializer.Serialize(checkOk.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isDuplicate").GetBoolean());

        // Record duplicate payment -> routes to Exception Queue
        var recordResult = await controller.RecordPayment(new PaymentDto
        {
            InvoiceId = "INV-001",
            Amount = 5000m,
            PaymentMethod = "Cash",
            ReferenceNumber = "DUP-REF-12345"
        });

        var ok = Assert.IsType<OkObjectResult>(recordResult);
        var newPayment = await appDb.Payments.FirstOrDefaultAsync(p => p.Id != "PAY-EXISTING");
        Assert.NotNull(newPayment);
        Assert.Equal("Exception", newPayment.PaymentStatus);
        Assert.Contains("Duplicate payment reference", newPayment.RejectionReason!);

        // Verify exception record added to SQL ExceptionRecords
        var exc = await fomsDb.ExceptionRecords.FirstOrDefaultAsync(e => e.RecordId == newPayment.Id);
        Assert.NotNull(exc);
        Assert.Equal("Payment", exc.RecordType);
        Assert.Equal("Open", exc.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 4: Unidentified payment -> placed "On Hold" until claimed.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC04_UnidentifiedPayment_OnHoldUntilClaimed()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var controller = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant") }
            }
        };

        // Payment with no invoice / flagged unidentified
        var dto = new PaymentDto
        {
            Amount = 10000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "UNID-REF-999",
            IsUnidentified = true
        };

        var result = await controller.RecordPayment(dto);
        Assert.IsType<OkObjectResult>(result);

        var payment = await appDb.Payments.FirstOrDefaultAsync(p => p.ReferenceNumber == "UNID-REF-999");
        Assert.NotNull(payment);
        Assert.Equal("On Hold", payment.PaymentStatus);

        // Invoice arrives later and payment is claimed
        var invoice = new Invoice
        {
            Id = "INV-CLAIM-1",
            InvoiceNo = "INV-CLAIM-1",
            ClientId = "CLI-CLAIM",
            TotalAmount = 10000m,
            Balance = 10000m,
            PaymentStatus = "Unpaid"
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var claimResult = await controller.ClaimPayment(payment.Id, new PaymentsController.ClaimPaymentDto
        {
            InvoiceId = "INV-CLAIM-1"
        });

        Assert.IsType<OkObjectResult>(claimResult);
        var claimedPayment = await appDb.Payments.FirstOrDefaultAsync(p => p.Id == payment.Id);
        Assert.NotNull(claimedPayment);
        Assert.Equal("Pending Validation", claimedPayment.PaymentStatus);
        Assert.Equal("INV-CLAIM-1", claimedPayment.InvoiceId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 5: Short and Over Payment Diverted to Exception Queue.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC05_ShortAndOverPayment_DivertedToExceptionQueue()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice
        {
            Id = "INV-MISMATCH",
            InvoiceNo = "INV-MISMATCH",
            ClientId = "CLI-01",
            TotalAmount = 10000m,
            Balance = 10000m
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant") }
            }
        };

        // Short payment (8,000 received for 10,000 balance)
        var shortRes = await controller.RecordPayment(new PaymentDto
        {
            InvoiceId = "INV-MISMATCH",
            Amount = 8000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-SHORT-1"
        });
        Assert.IsType<OkObjectResult>(shortRes);
        var shortPay = await appDb.Payments.FirstOrDefaultAsync(p => p.ReferenceNumber == "REF-SHORT-1");
        Assert.NotNull(shortPay);
        Assert.Equal("Exception", shortPay.PaymentStatus);
        Assert.Contains("Short payment", shortPay.RejectionReason!);

        // Overpayment (12,000 received for 10,000 balance)
        var overRes = await controller.RecordPayment(new PaymentDto
        {
            InvoiceId = "INV-MISMATCH",
            Amount = 12000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-OVER-1"
        });
        Assert.IsType<OkObjectResult>(overRes);
        var overPay = await appDb.Payments.FirstOrDefaultAsync(p => p.ReferenceNumber == "REF-OVER-1");
        Assert.NotNull(overPay);
        Assert.Equal("Exception", overPay.PaymentStatus);
        Assert.Contains("Overpayment", overPay.RejectionReason!);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 6: Validation Authority: Only Finance Manager or Active Delegate.
    // Unauthorized roles (Accountant, Assistant) rejected with 403 Forbidden.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC06_ValidationAuthority_NonAuthorizedRejectedWith403()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice { Id = "INV-10", InvoiceNo = "INV-10", Balance = 5000m, TotalAmount = 5000m };
        var payment = new Payment { Id = "PAY-10", InvoiceId = "INV-10", Amount = 5000m, PaymentStatus = "Pending Validation" };
        appDb.Invoices.Add(invoice);
        appDb.Payments.Add(payment);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Accountant", "Mariel", "EMP-003") }
            }
        };

        var result = await controller.ValidatePayment("PAY-10", new FinancePaymentsController.ValidationActionRequest("Attempt"));
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 7: Configured and Logged Delegate Validates Successfully.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC07_ConfiguredDelegate_CanValidateFundsArrived()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice { Id = "INV-20", InvoiceNo = "INV-20", ClientId = "CLI-20", Balance = 5000m, TotalAmount = 5000m };
        var payment = new Payment { Id = "PAY-20", InvoiceId = "INV-20", ClientId = "CLI-20", Amount = 5000m, PaymentStatus = "Pending Validation" };
        appDb.Invoices.Add(invoice);
        appDb.Payments.Add(payment);
        await appDb.SaveChangesAsync();

        // Add active delegate in SQL database
        fomsDb.FinanceManagerDelegates.Add(new SqlServer.FinanceManagerDelegate
        {
            DelegateUserId = "EMP-004",
            DelegateName = "Arthur Assistant",
            AssignedBy = "Finance Manager",
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(7),
            Reason = "FM on official leave",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await fomsDb.SaveChangesAsync();

        // Call as Assistant with delegated authority
        var controller = new FinancePaymentsController(appDb, fomsDb, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Assistant of Finance Manager", "Arthur Assistant", "EMP-004") }
            }
        };

        var result = await controller.ValidatePayment("PAY-20", new FinancePaymentsController.ValidationActionRequest("Funds confirmed in BDO"));
        Assert.IsType<OkObjectResult>(result);

        var updatedPayment = await appDb.Payments.FirstOrDefaultAsync(p => p.Id == "PAY-20");
        Assert.NotNull(updatedPayment);
        Assert.Equal("Validated", updatedPayment.PaymentStatus);
        Assert.Contains("Delegate", updatedPayment.ValidatedBy!);

        var updatedInvoice = await appDb.Invoices.FirstOrDefaultAsync(i => i.Id == "INV-20");
        Assert.NotNull(updatedInvoice);
        Assert.Equal("Paid", updatedInvoice.PaymentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 8: Exact Match Rule: Invoice becomes Paid ONLY IF validated payment
    // equals invoice amount exactly.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC08_ExactMatchRule_InvoiceBecomesPaidOnlyOnExactMatch()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var invoice = new Invoice { Id = "INV-EXACT", InvoiceNo = "INV-EXACT", ClientId = "CLI-EXACT", Balance = 10000m, TotalAmount = 10000m };
        var mismatchPayment = new Payment { Id = "PAY-MIS", InvoiceId = "INV-EXACT", ClientId = "CLI-EXACT", Amount = 9500m, PaymentStatus = "Pending Validation" };
        var exactPayment = new Payment { Id = "PAY-EXACT", InvoiceId = "INV-EXACT", ClientId = "CLI-EXACT", Amount = 10000m, PaymentStatus = "Pending Validation" };

        appDb.Invoices.Add(invoice);
        appDb.Payments.AddRange(mismatchPayment, exactPayment);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Finance Manager", "Crystalyn", "EMP-001") }
            }
        };

        // Mismatched payment fails exact match validation
        var badResult = await controller.ValidatePayment("PAY-MIS", new FinancePaymentsController.ValidationActionRequest("Attempt"));
        var badReq = Assert.IsType<BadRequestObjectResult>(badResult);
        Assert.Contains("Invoice becomes Paid only if validated payment equals invoice amount exactly", badReq.Value!.ToString()!);

        // Exact match validation succeeds
        var okResult = await controller.ValidatePayment("PAY-EXACT", new FinancePaymentsController.ValidationActionRequest("Validated"));
        Assert.IsType<OkObjectResult>(okResult);

        var refreshedInvoice = await appDb.Invoices.FirstOrDefaultAsync(i => i.Id == "INV-EXACT");
        Assert.NotNull(refreshedInvoice);
        Assert.Equal("Paid", refreshedInvoice.PaymentStatus);
        Assert.Equal(0m, refreshedInvoice.Balance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 9: Exception Resolution (PAY-10): Duplicate Payment MUST be rejected.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC09_Pay10_DuplicatePaymentMustBeRejected()
    {
        using var appDb = CreateAppDbContext();

        var dupPayment = new Payment
        {
            Id = "PAY-DUP-EXC",
            InvoiceId = "INV-DUP",
            Amount = 5000m,
            PaymentStatus = "Exception",
            RejectionReason = "Duplicate payment reference: DUP-999."
        };
        appDb.Payments.Add(dupPayment);
        await appDb.SaveChangesAsync();

        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(appDb);

        // Attempting to send duplicate to validation throws exception
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
                "PAY-DUP-EXC", "send-to-validation", "Try approve", "FM"), CancellationToken.None));

        // Rejecting duplicate succeeds
        var result = await handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
            "PAY-DUP-EXC", "reject", "Duplicate receipt reference confirmed by bank statement.", "FM"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Rejected", result.PaymentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 10: Exception Resolution (PAY-10): Overpayment & Short Payment return
    // to "Pending Validation" (NEVER directly "Paid").
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC10_Pay10_ResolvedPaymentsReturnToPendingValidation_NeverDirectlyPaid()
    {
        using var appDb = CreateAppDbContext();

        var client = new Client { Id = "CLI-RESOLVE", Name = "Client A", AvailableCredit = 0m };
        var invoice = new Invoice { Id = "INV-RES", InvoiceNo = "INV-RES", ClientId = "CLI-RESOLVE", Balance = 10000m, TotalAmount = 10000m };

        var overPayment = new Payment
        {
            Id = "PAY-OVER-EXC",
            InvoiceId = "INV-RES",
            ClientId = "CLI-RESOLVE",
            Amount = 15000m,
            PaymentStatus = "Exception",
            RejectionReason = "Overpayment: received 15,000.00; invoice balance is 10,000.00."
        };
        appDb.Clients.Add(client);
        appDb.Invoices.Add(invoice);
        appDb.Payments.Add(overPayment);
        await appDb.SaveChangesAsync();

        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(appDb);

        var resolved = await handler.Handle(new PaymentFeatures.ResolvePaymentExceptionCommand(
            "PAY-OVER-EXC", "send-to-validation", "Overpayment approved; excess to be credited to other invoices.", "Finance Manager"), CancellationToken.None);

        Assert.NotNull(resolved);
        // CRUCIAL: Resolved payment returns to Pending Validation, never directly Paid!
        Assert.Equal("Pending Validation", resolved.PaymentStatus);
        Assert.NotEqual("Paid", resolved.PaymentStatus);
        Assert.Contains("unapplied credit", resolved.ExceptionResolutionReason!);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 11: Validated payment auto-updates AR, cash flow, dashboard.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TC11_ValidatedPayment_AutoUpdates_Invoice_AR_CashFlow_Dashboard()
    {
        using var appDb = CreateAppDbContext();
        using var fomsDb = CreateFomsDbContext();

        var client = new Client { Id = "CLI-SYNC", Name = "Beta Corp", AvailableCredit = 0m };
        var invoice = new Invoice
        {
            Id = "INV-SYNC",
            InvoiceNo = "INV-SYNC",
            ClientId = "CLI-SYNC",
            ClientName = "Beta Corp",
            TotalAmount = 8000m,
            Balance = 8000m,
            AmountPaid = 0m,
            PaymentStatus = "Unpaid"
        };
        var receivable = new ReceivableBalance
        {
            InvoiceId = "INV-SYNC",
            ClientId = "CLI-SYNC",
            BalanceAmount = 8000m,
            PaidAmount = 0m,
            Status = "Unpaid"
        };
        var payment = new Payment
        {
            Id = "PAY-SYNC",
            InvoiceId = "INV-SYNC",
            ClientId = "CLI-SYNC",
            ClientName = "Beta Corp",
            Amount = 8000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-SYNC-01",
            OrNumber = "OR-2026-0001",
            PaymentStatus = "Pending Validation"
        };

        appDb.Clients.Add(client);
        appDb.Invoices.Add(invoice);
        appDb.ReceivableBalances.Add(receivable);
        appDb.Payments.Add(payment);
        await appDb.SaveChangesAsync();

        var controller = new FinancePaymentsController(appDb, fomsDb, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("Finance Manager", "Crystalyn Fajardo", "EMP-001") }
            }
        };

        var result = await controller.ValidatePayment("PAY-SYNC", new FinancePaymentsController.ValidationActionRequest("Funds verified in bank account"));
        Assert.IsType<OkObjectResult>(result);

        // 1. Invoice Auto-Updated
        var refInv = await appDb.Invoices.FirstAsync(i => i.Id == "INV-SYNC");
        Assert.Equal("Paid", refInv.PaymentStatus);
        Assert.Equal("Validated", refInv.PaymentValidationStatus);
        Assert.Equal(0m, refInv.Balance);
        Assert.Equal(8000m, refInv.AmountPaid);

        // 2. AR Auto-Updated (cleared or zeroed out on full payment)
        var refAr = await appDb.ReceivableBalances.FirstOrDefaultAsync(r => r.InvoiceId == "INV-SYNC");
        Assert.True(refAr == null || refAr.BalanceAmount == 0m);

        // 3. Cash Flow Auto-Updated (Inflow logged)
        var cashFlow = await appDb.CashFlowTransactions.FirstOrDefaultAsync(c => c.ReferenceNo == "OR-2026-0001" || c.ReferenceNo == "PAY-SYNC");
        Assert.NotNull(cashFlow);
        Assert.Equal("Inflow", cashFlow.Type);
        Assert.Equal(8000m, cashFlow.Amount);

        // 4. SQL Server Cash Inflow Synchronized
        var sqlInflow = await fomsDb.CashInflows.FirstOrDefaultAsync(c => c.ReferenceNumber == "OR-2026-0001");
        Assert.NotNull(sqlInflow);
        Assert.Equal(8000m, sqlInflow.Amount);
    }
}
