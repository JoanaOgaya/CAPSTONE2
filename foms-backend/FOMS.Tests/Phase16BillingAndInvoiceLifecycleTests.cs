using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using FOMS.Api.Controllers;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Xunit;
using Invoice = FOMS.Domain.Entities.Invoice;

namespace FOMS.Tests;

public class Phase16BillingAndInvoiceLifecycleTests
{
    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateMssqlDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FomsDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string role, string id, string name)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("name", name)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal StaffUser() => CreatePrincipal("Accountant", "EMP-003", "Staff Accountant");
    private static ClaimsPrincipal HeadAccountantUser() => CreatePrincipal("Head Accountant", "EMP-002", "Head Accountant");
    private static ClaimsPrincipal FinanceManagerUser() => CreatePrincipal("Finance Manager", "EMP-001", "Finance Manager");

    // ─────────────────────────────────────────────────────────────────────────
    // 1. SECTION 5 GOLDEN BILLING TEST: REPRODUCE 773.20 EXACTLY
    //    2 boxes to NCR, declared value 1,000, 35x50x25 cm, 20 kg actual, 15% fuel
    //    CW = 25 kg, Freight = 600.00, Valuation = 10.00, Subtotal = 610.00,
    //    VAT = 73.20, Fuel = 90.00, Total = 773.20
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Section5_GoldenBillingComputation_ReproducesWorkedExample_773_20()
    {
        // Worked example parameters:
        int pieces = 2;
        decimal lengthCm = 35m;
        decimal widthCm = 50m;
        decimal heightCm = 25m;
        decimal actualWeight = 20m;
        decimal declaredValue = 1000m;
        string deliveryArea = "NCR";
        decimal minimumRate = 200m;
        decimal excessRatePerKg = 20m;
        decimal minimumKg = 5m;
        decimal fuelSurchargeRate = 0.15m;
        decimal valuationRate = 0.01m;
        decimal odaRate = 0m;

        var breakdown = BillingComputationService.ComputeWaybillCost(
            pieces: pieces,
            lengthCm: lengthCm,
            widthCm: widthCm,
            heightCm: heightCm,
            actualWeight: actualWeight,
            declaredValue: declaredValue,
            deliveryArea: deliveryArea,
            minimumRate: minimumRate,
            excessRatePerKg: excessRatePerKg,
            odaRate: odaRate,
            fuelSurchargeRate: fuelSurchargeRate,
            valuationRate: valuationRate,
            minimumKg: minimumKg,
            waybillNumber: "WB-GOLDEN-001"
        );

        // Volumetric: (35 * 50 * 25 * 2) / 3500 = 87500 / 3500 = 25.00 kg
        Assert.Equal(25m, breakdown.VolumetricWeight);
        // Chargeable weight = max(25, 20) = 25.00 kg
        Assert.Equal(25m, breakdown.ChargeableWeight);
        // Excess kg = max(0, 25 - 5) = 20.00 kg
        Assert.Equal(20m, breakdown.ExcessKg);
        // Freight = 200.00 + (20 * 20.00) = 600.00
        Assert.Equal(600.00m, breakdown.Freight);
        // Valuation = 1% * 1,000.00 = 10.00
        Assert.Equal(10.00m, breakdown.ValuationCharge);
        // Subtotal = Freight + Valuation + ODA = 610.00
        Assert.Equal(610.00m, breakdown.Subtotal);
        // VAT = 12% * 610.00 = 73.20
        Assert.Equal(73.20m, breakdown.VatAmount);
        // Fuel surcharge = 15% * 600.00 = 90.00
        Assert.Equal(90.00m, breakdown.FuelSurcharge);
        // Total = 610.00 + 73.20 + 90.00 = 773.20
        Assert.Equal(773.20m, breakdown.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DEFAULT RATES PER AREA (NCR, Luzon, Visayas, Mindanao)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("NCR", 200, 20, 0, 773.20)]
    [InlineData("Luzon", 250, 25, 50, 1019.70)]
    [InlineData("Visayas", 300, 30, 100, 1266.20)]
    [InlineData("Mindanao", 350, 35, 150, 1512.70)]
    public void DefaultRates_PerDeliveryArea_ComputeConsistentTotals(
        string area, decimal minRate, decimal excessRate, decimal oda, decimal expectedTotal)
    {
        var breakdown = BillingComputationService.ComputeWaybillCost(
            pieces: 2,
            lengthCm: 35m,
            widthCm: 50m,
            heightCm: 25m,
            actualWeight: 20m,
            declaredValue: 1000m,
            deliveryArea: area,
            minimumRate: minRate,
            excessRatePerKg: excessRate,
            odaRate: oda,
            fuelSurchargeRate: 0.15m,
            valuationRate: 0.01m,
            minimumKg: 5m,
            waybillNumber: $"WB-AREA-{area}"
        );

        Assert.Equal(25m, breakdown.ChargeableWeight);
        Assert.Equal(expectedTotal, breakdown.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. NO-SELF-APPROVAL EDGE CASE (OPTION 1 CONFIRMED):
    //    If Head Accountant created invoice, Finance Manager verifies and approves
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Option1EdgeCase_HeadAccountantCreator_VerifiedAndApprovedByFinanceManager()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var invoice = new Invoice
        {
            Id = "INV-HA-CREATOR-01",
            InvoiceNo = "INV-2026-9001",
            ClientId = "CL-001",
            ClientName = "Shopee Express",
            DocumentStatus = InvoiceDocumentStatus.ForReview,
            EncodedBy = "EMP-002", // Head Accountant created/encoded
            TotalAmount = 5000m,
            Subtotal = 4464.29m,
            VatAmount = 535.71m,
            Balance = 5000m
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // Head Accountant EMP-002 tries to verify own invoice -> 409 Conflict
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var selfVerifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        var conflict = Assert.IsType<ObjectResult>(selfVerifyRes);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("Separation of duties", conflict.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Finance Manager EMP-001 verifies and approves (Option 1) -> 200 OK
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } };
        var fmVerifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(fmVerifyRes);

        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Approved, cur!.DocumentStatus);
        Assert.Equal("EMP-001", cur.VerifiedBy);
        Assert.Equal("EMP-001", cur.ApprovedBy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. TEST 4 ASSERTION: VOIDING APPROVED INVOICE POSTS NO JOURNAL ENTRY
    //    Void allowed by Head Accountant prior to Sent.
    //    After Sent, adjustments require a Credit Memo.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test4Assertion_VoidingApprovedInvoice_PostsNoJournalEntry_AndPostSentRequiresCreditMemo()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var invoice = new Invoice
        {
            Id = "INV-VOID-TEST-01",
            InvoiceNo = "INV-2026-9002",
            ClientId = "CL-001",
            ClientName = "Lazada",
            DocumentStatus = InvoiceDocumentStatus.Approved,
            TotalAmount = 773.20m,
            Balance = 773.20m,
            EncodedBy = "EMP-003",
            PaymentStatus = InvoicePaymentStatus.Unpaid
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // 1. Head Accountant voids Approved invoice prior to Sent
        var voidRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest
        {
            VoidReason = "Billing error in volumetric calculation before delivery to client"
        });
        Assert.IsType<OkObjectResult>(voidRes);

        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Void, cur!.DocumentStatus);
        Assert.Equal("Billing error in volumetric calculation before delivery to client", cur.VoidReason);

        // 2. Assert: NO journal entry was posted (AR posts strictly at Sent)
        Assert.Empty(appDb.JournalEntries);
        Assert.Empty(appDb.GeneralLedgerEntries);

        // 3. Assert: Once an invoice is Sent, Void is forbidden (adjustments require Credit Memo)
        var sentInvoice = new Invoice
        {
            Id = "INV-SENT-VOID-FAIL",
            InvoiceNo = "INV-2026-9003",
            ClientId = "CL-001",
            ClientName = "Lazada",
            DocumentStatus = InvoiceDocumentStatus.Sent,
            TotalAmount = 1000m,
            Balance = 1000m,
            EncodedBy = "EMP-003",
            PaymentStatus = InvoicePaymentStatus.Unpaid
        };
        appDb.Invoices.Add(sentInvoice);
        await appDb.SaveChangesAsync();

        var voidSentRes = await controller.VoidInvoice(sentInvoice.Id, new VoidInvoiceRequest { VoidReason = "Attempt post-dispatch void" });
        var conflict = Assert.IsType<ObjectResult>(voidSentRes);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("Credit Memo", conflict.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. AR POSTS ONCE AT SENT WITH IDEMPOTENCY KEY (${InvoiceNo}_SENT)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ArAccrualRevenue_PostsOnceAtSent_WithIdempotencyProtection()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var invoice = new Invoice
        {
            Id = "INV-SENT-IDEMP-01",
            InvoiceNo = "INV-2026-9004",
            ClientId = "CL-001",
            ClientName = "Acme",
            DocumentStatus = InvoiceDocumentStatus.Approved,
            TotalAmount = 1120m,
            Subtotal = 1000m,
            VatAmount = 120m,
            Balance = 1120m,
            EncodedBy = "EMP-003"
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } }
        };

        // Transition Approved -> Sent
        var sendRes = await controller.TransitionInvoice(invoice.Id, "send", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(sendRes);

        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Sent, cur!.DocumentStatus);
        Assert.True(cur.AccrualRevenueRecognized);
        Assert.NotNull(cur.AccrualJournalEntryId);

        // Verify JournalEntry entry number matches canonical idempotency prefix
        var je = await appDb.JournalEntries.FirstOrDefaultAsync(j => j.InvoiceId == invoice.Id);
        Assert.NotNull(je);
        Assert.StartsWith($"JE-ACCRUAL-{invoice.InvoiceNo}", je.EntryNumber);

        // Retry sending or recognizing revenue: verify no double-posting
        await BillingComputationService.RecognizeAccrualRevenueAsync(cur, appDb);
        var jeCount = await appDb.JournalEntries.CountAsync(j => j.InvoiceId == invoice.Id);
        Assert.Equal(1, jeCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. PAYMENTS ACCEPTED ONLY FOR SENT OR RECEIVED INVOICES
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(InvoiceDocumentStatus.Draft, false)]
    [InlineData(InvoiceDocumentStatus.ForReview, false)]
    [InlineData(InvoiceDocumentStatus.Approved, false)]
    [InlineData(InvoiceDocumentStatus.Void, false)]
    [InlineData(InvoiceDocumentStatus.Sent, true)]
    [InlineData(InvoiceDocumentStatus.Received, true)]
    public async Task Payments_AcceptedOnlyForSentOrReceivedInvoices(string docStatus, bool shouldSucceed)
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var invoice = new Invoice
        {
            Id = $"INV-PAY-{Guid.NewGuid():N}",
            InvoiceNo = $"INV-PAY-{Guid.NewGuid():N}"[..12],
            ClientId = "CL-001",
            ClientName = "Test Client",
            DocumentStatus = docStatus,
            TotalAmount = 500m,
            Balance = 500m,
            EncodedBy = "EMP-003",
            PaymentStatus = InvoicePaymentStatus.Unpaid
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new PaymentsController(appDb, mssqlDb, Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } }
        };

        var res = await controller.RecordPayment(new PaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 500m,
            PaymentMethod = "Check",
            ReferenceNumber = $"REF-{Guid.NewGuid():N}"[..10],
            ProofImageUrl = "/uploads/check.pdf"
        });

        if (shouldSucceed)
        {
            Assert.IsType<OkObjectResult>(res);
        }
        else
        {
            var bad = Assert.IsType<BadRequestObjectResult>(res);
            var msg = bad.Value?.ToString() ?? "";
            Assert.True(msg.Contains("Payments are accepted only for Sent or Received invoices", StringComparison.OrdinalIgnoreCase) ||
                        msg.Contains("Cannot record payment for a void invoice", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. BIL-07: DISCOUNT CEO APPROVAL GATE
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Discount_WithoutCeoApprovalRecord_IsBlockedAtVerifyAndApprove()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var mockGateService = new Mock<ICeoApprovalGateService>();
        // Gate check returns unsatisfied for discount item
        mockGateService
            .Setup(g => g.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<DateTime?>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new CeoApprovalGateCheckResult { IsSatisfied = false, Message = "Item 7 requires an in-person CEO approval record." });

        var invoice = new Invoice
        {
            Id = "INV-DISC-BLOCKED",
            InvoiceNo = "INV-2026-DISC-01",
            ClientId = "CL-001",
            ClientName = "Big Client",
            DocumentStatus = InvoiceDocumentStatus.ForReview,
            DiscountAmount = 200m,
            DiscountRate = 0.10m,
            TotalAmount = 1800m,
            Balance = 1800m,
            EncodedBy = "EMP-003"
        };
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb, mockGateService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // Verify fails because discount has no CEO approval record
        var verifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        var badReq = Assert.IsType<BadRequestObjectResult>(verifyRes);
        Assert.Contains("strictly requires an in-person CEO/President approval record", badReq.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
