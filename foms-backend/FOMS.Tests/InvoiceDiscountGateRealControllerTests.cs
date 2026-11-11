// Item 8 — Invoice Discount Gate: real InvoicesController.cs:718-730 and
// AccountantController.cs:273-282 with the real CeoApprovalGateService.
// Tests verify:
//   (a) Approver text "CEO" without a recorded CEO approval record is rejected (400 Bad Request).
//   (b) With an Approved CeoApprovalRecord (InvoiceDiscount) the call succeeds.
//   (c) Other paths that can apply a discount are listed in a class-level documentation region.
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Domain.Constants;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

/// <summary>
/// Item 8 — Invoice discount gate through real InvoicesController and AccountantController.
///
/// Analysis of all paths that can apply a discount across the system:
///   1. InvoicesController.CreateInvoice (lines 718-730):
///      - Checks BillingComputationService.ValidateDiscountApproval (text approver check).
///      - Calls _gateService.CheckGateAsync(InvoiceDiscount, discountItemId, discountAmount).
///      - Item ID is the invoice number (submission.InvoiceNumber / generated invNo).
///   2. InvoicesController.TransitionInvoice (lines 1027 and 1050):
///      - When transitioning to Verified or Approved, if invoice.DiscountAmount > 0,
///        re-verifies _gateService.CheckGateAsync(InvoiceDiscount, invoice.InvoiceNo, invoice.DiscountAmount).
///   3. AccountantController.CreateInvoice (lines 273-282):
///      - Checks BillingComputationService.ValidateDiscountApproval.
///      - Calls _gateService.CheckGateAsync(InvoiceDiscount, discountItemId, discountAmount).
///      - Item ID is (string?)body?.invoiceNumber ?? (string?)body?.clientId ?? "NEW_INVOICE".
///   4. BillingComputationService.RecalculateInvoice (line 748):
///      - Pure calculation routine: TotalAmount = max(0, TotalAmount - CreditMemoAmount - DiscountAmount).
///      - Used during billing runs, invoice recalculations, and credit adjustments.
///      - Does NOT invoke the gate check directly; gate check is enforced by the caller (controller / command handler).
///   5. Client Config / Rates:
///      - ClientRate has ValuationRate, FuelSurchargeRate, MinimumRate, ExcessRatePerKg.
///      - These are rate components, not "discounts" under Item 7 / BIL-07.
/// </summary>
public class InvoiceDiscountGateRealControllerTests
{
    private static ApplicationDbContext CreateAppDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateFomsDb()
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FomsDbContext(options);
    }

    private static void SeedInvoiceClient(ApplicationDbContext appDb, FomsDbContext fomsDb)
    {
        appDb.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "CL-DISC",
            ClientCode = "CL-DISC",
            Name = "Discount Test Client",
            IsActive = true,
            SignedProposalAttached = true,
            SignedProposalUrl = "https://test/proposal.pdf"
        });
        appDb.SaveChanges();

        fomsDb.ClientRates.Add(new ClientRate
        {
            DeliveryArea = "NCR",
            MinimumRate = 200m,
            ExcessRatePerKg = 20m,
            MinimumKg = 5m,
            ValuationRate = 0.01m,
            FuelSurchargeRate = 0.15m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });
        fomsDb.SaveChanges();
    }

    private static void SeedFomsClient(FomsDbContext fomsDb, int clientId = 1)
    {
        fomsDb.Clients.Add(new FOMS.Domain.Entities.SqlServer.Client
        {
            ClientId = clientId,
            CompanyName = "Acct Test Client",
            IsActive = true
        });

        fomsDb.Waybills.Add(new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillNumber = "WB-DISC-001",
            ClientId = clientId,
            IsBilled = false,
            PodStatus = "Validated",
            IsDuplicateFlagged = false,
            DeliveryArea = "NCR",
            NumberOfBoxes = 1,
            ActualWeight = 10m,
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 30m,
            DeclaredValue = 500m
        });

        fomsDb.ClientRates.Add(new ClientRate
        {
            DeliveryArea = "NCR",
            ClientId = clientId,
            MinimumRate = 200m,
            ExcessRatePerKg = 20m,
            MinimumKg = 5m,
            ValuationRate = 0.01m,
            FuelSurchargeRate = 0.15m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });

        fomsDb.SaveChanges();
    }

    // InvoicesController.CreateInvoice requires Staff (Accountant, EMP-003)
    private static ClaimsPrincipal CreateStaffPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "EMP-003"),
            new(ClaimTypes.Name, "Maria Staff"),
            new(ClaimTypes.Role, "Accountant"),
            new("role", "Accountant"),
            new("employee_id", "EMP-003"),
            new("name", "Maria Staff")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static object? GetProp(object? obj, string name)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(obj);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. InvoicesController Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvoicesController_ApproverTextCeo_WithoutRecordedApproval_IsRejected()
    {
        // Arrange
        var appDb = CreateAppDb();
        var fomsDb = CreateFomsDb();
        SeedInvoiceClient(appDb, fomsDb);

        // Real gate service with no approved record
        var gateService = new CeoApprovalGateService(appDb);

        var controller = new InvoicesController(appDb, fomsDb, gateService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
            }
        };

        // Act: discount with approver text "CEO" but NO recorded approval in CeoApprovalRecords
        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-DISC",
            ClientName = "Discount Test Client",
            FreightCharges = 600m,
            OtherCharges = 10m,
            DiscountAmount = 77.32m,
            DiscountRate = 0.10m,
            DiscountApprovedBy = "President and CEO",
            DiscountReason = "10% Executive Volume Discount"
        });

        // Assert: 400 Bad Request with explicit message about CEO approval record
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var msg = badRequest.Value?.ToString() ?? "";
        Assert.Contains("CEO/President approval record", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvoicesController_ApproverTextCeo_WithRecordedApproval_Succeeds()
    {
        // Arrange
        var appDb = CreateAppDb();
        var fomsDb = CreateFomsDb();
        SeedInvoiceClient(appDb, fomsDb);

        var gateService = new CeoApprovalGateService(appDb);

        // Record CEO approval for this invoice number
        string invNo = "INV-2026-DISC-01";
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.InvoiceDiscount,
            ItemId = invNo,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        var controller = new InvoicesController(appDb, fomsDb, gateService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
            }
        };

        // Act: create invoice with matching invoice number and CEO approver text
        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            InvoiceNumber = invNo,
            ClientId = "CL-DISC",
            ClientName = "Discount Test Client",
            FreightCharges = 600m,
            OtherCharges = 10m,
            DiscountAmount = 77.32m,
            DiscountRate = 0.10m,
            DiscountApprovedBy = "President and CEO",
            DiscountReason = "10% Executive Volume Discount"
        });

        // Assert: 200 OK
        var okResult = Assert.IsType<OkObjectResult>(result);
        var discountAmount = GetProp(okResult.Value, "discountAmount");
        Assert.Equal(77.32m, Convert.ToDecimal(discountAmount));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. AccountantController Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountantController_ApproverTextCeo_WithoutRecordedApproval_IsRejected()
    {
        // Arrange
        var appDb = CreateAppDb();
        var fomsDb = CreateFomsDb();
        SeedFomsClient(fomsDb, clientId: 1);

        var gateService = new CeoApprovalGateService(appDb);

        var controller = new AccountantController(fomsDb, gateService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
            }
        };

        // Act: dynamic body with all required properties for AccountantController
        dynamic body = new ExpandoObject();
        body.clientId = 1;
        body.invoiceNumber = "INV-2026-ACCT-01";
        body.discountAmount = 50m;
        body.discountRate = 0.05m;
        body.discountApprovedBy = "CEO";
        body.discountReason = "Volume discount";

        var result = await controller.CreateInvoice(body);

        // Assert: 400 Bad Request
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var msg = badRequest.Value?.ToString() ?? "";
        Assert.Contains("CEO/President approval record", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountantController_ApproverTextCeo_WithRecordedApproval_Succeeds()
    {
        // Arrange
        var appDb = CreateAppDb();
        var fomsDb = CreateFomsDb();
        SeedFomsClient(fomsDb, clientId: 1);

        var gateService = new CeoApprovalGateService(appDb);

        // Record CEO approval matching the invoice number
        string invNo = "INV-2026-ACCT-02";
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.InvoiceDiscount,
            ItemId = invNo,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        var controller = new AccountantController(fomsDb, gateService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
            }
        };

        dynamic body = new ExpandoObject();
        body.clientId = 1;
        body.invoiceNumber = invNo;
        body.discountAmount = 50m;
        body.discountRate = 0.05m;
        body.discountApprovedBy = "President and CEO";
        body.discountReason = "Volume discount";

        var result = await controller.CreateInvoice(body);

        // Assert: 200 OK
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.True((bool)(GetProp(okResult.Value, "success") ?? false));
    }
}
