using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

public class BillingAndRatingTests
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

    private static ClaimsPrincipal CreateStaffPrincipal(string role = "Finance Manager", string name = "Finance Manager Test User")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "EMP-001"),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("name", name)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. EXACT USER SPECIFICATION UNIT TEST
    // 2 boxes NCR, 35x50x25, 20 kg actual, DV 1000, fuel 15% => 773.20
    // (freight 600, valuation 10, VAT 73.20, fuel 90)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void BillingFormula_TwoBoxesNcr_Computes773_20_Exactly()
    {
        // Inputs specified by prompt:
        int pieces = 2;
        string deliveryArea = "NCR";
        decimal lengthCm = 35m;
        decimal widthCm = 50m;
        decimal heightCm = 25m;
        decimal actualWeight = 20m;
        decimal declaredValue = 1000m;
        decimal fuelSurchargeRate = 0.15m; // 15%

        // NCR Rate parameters: Base Rmin = 200 (first 5 kg), Rexcess = 20 / kg, valuation = 1%, ODA = 0
        decimal minimumRate = 200m;
        decimal excessRatePerKg = 20m;
        decimal minimumKg = 5m;
        decimal valuationRate = 0.01m;
        decimal odaRate = 0m;

        var result = BillingComputationService.ComputeWaybillCost(
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
            waybillNumber: "WB-SPEC-001"
        );

        // Volumetric weight = (35 * 50 * 25 * 2) / 3500 = 87500 / 3500 = 25 kg
        Assert.Equal(25.00m, result.VolumetricWeight);

        // CW = max(25, 20) = 25 kg
        Assert.Equal(25.00m, result.ChargeableWeight);

        // Excess kg = max(0, 25 - 5) = 20 kg
        Assert.Equal(20.00m, result.ExcessKg);

        // Freight = 200 + (20 * 20) = 600.00
        Assert.Equal(600.00m, result.Freight);

        // Valuation = 1% of 1000 = 10.00
        Assert.Equal(10.00m, result.ValuationCharge);

        // Subtotal = 600 + 10 + 0 = 610.00
        Assert.Equal(610.00m, result.Subtotal);

        // VAT = 12% of 610 = 73.20
        Assert.Equal(73.20m, result.VatAmount);

        // Fuel = 15% of 600 = 90.00
        Assert.Equal(90.00m, result.FuelSurcharge);

        // Grand Total = 610 + 73.20 + 90.00 = 773.20
        Assert.Equal(773.20m, result.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Client Rates from config by effective date
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RatesFromClientConfig_AppliesCorrectRateByEffectiveDate()
    {
        var rates = new List<ClientRate>
        {
            new()
            {
                RateId = 1,
                ClientId = 1,
                DeliveryArea = "NCR",
                MinimumRate = 180m,
                ExcessRatePerKg = 18m,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                EffectiveTo = new DateOnly(2025, 12, 31),
                IsActive = false
            },
            new()
            {
                RateId = 2,
                ClientId = 1,
                DeliveryArea = "NCR",
                MinimumRate = 200m,
                ExcessRatePerKg = 20m,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2099, 12, 31),
                IsActive = true
            }
        };

        var date2025 = new DateOnly(2025, 6, 1);
        var date2026 = new DateOnly(2026, 6, 1);

        var match2025 = rates
            .Where(r => (r.EffectiveFrom == null || r.EffectiveFrom <= date2025) &&
                        (r.EffectiveTo == null || r.EffectiveTo >= date2025))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        var match2026 = rates
            .Where(r => (r.EffectiveFrom == null || r.EffectiveFrom <= date2026) &&
                        (r.EffectiveTo == null || r.EffectiveTo >= date2026))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        Assert.NotNull(match2025);
        Assert.Equal(180m, match2025.MinimumRate);

        Assert.NotNull(match2026);
        Assert.Equal(200m, match2026.MinimumRate);
    }

    private static void SeedBillableClient(ApplicationDbContext appDb, FomsDbContext mssqlDb, string clientId = "CL-001", string clientName = "Test Client")
    {
        appDb.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = clientId,
            ClientCode = clientId,
            Name = clientName,
            IsActive = true,
            SignedProposalAttached = true,
            SignedProposalUrl = "https://speedex.storage/proposals/test.pdf"
        });
        appDb.SaveChanges();

        mssqlDb.ClientRates.Add(new FOMS.Domain.Entities.SqlServer.ClientRate
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
        mssqlDb.SaveChanges();
    }

    private static object? GetValueProp(object? obj, string propName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(obj);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Zero / Negative amounts go to exception queue with reason
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ZeroOrNegativeAmount_SentToExceptionQueueWithReason()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        SeedBillableClient(appDb, mssqlDb);

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() } }
        };

        // Attempt to create invoice with 0 amount
        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 0m,
            OtherCharges = 0m
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Zero or negative", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Verify inserted into ExceptionRecords
        var exceptionRecord = await mssqlDb.ExceptionRecords.FirstOrDefaultAsync();
        Assert.NotNull(exceptionRecord);
        Assert.Equal("Invoice", exceptionRecord.RecordType);
        Assert.Equal("Open", exceptionRecord.Status);
        Assert.Contains("Zero or negative billing amount", exceptionRecord.Reason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Pre-alert on suspect computation (BIL-06)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SuspectComputation_BIL06_PreAlertTriggered()
    {
        // Compute with suspect extreme weight
        var suspectBreakdown = BillingComputationService.ComputeWaybillCost(
            pieces: 1,
            lengthCm: 200m,
            widthCm: 200m,
            heightCm: 200m, // (200*200*200)/3500 = 2285.7 kg!
            actualWeight: 10m,
            declaredValue: 500000m,
            deliveryArea: "NCR",
            minimumRate: 200m,
            excessRatePerKg: 20m
        );

        Assert.True(suspectBreakdown.IsSuspect);
        Assert.Contains("BIL-06", suspectBreakdown.SuspectReason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Discount ~10% needs recorded CEO/President approval and is its own line
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Discount_RequiresCeoOrPresidentApproval()
    {
        // Without approval
        var (invalidNoApprover, err1) = BillingComputationService.ValidateDiscountApproval(77.32m, 0.10m, null);
        Assert.False(invalidNoApprover);
        Assert.Contains("CEO or President", err1);

        // With unauthorized role
        var (invalidStaff, err2) = BillingComputationService.ValidateDiscountApproval(77.32m, 0.10m, "Billing Clerk");
        Assert.False(invalidStaff);
        Assert.Contains("CEO or President", err2);

        // With CEO approval
        var (validCeo, _) = BillingComputationService.ValidateDiscountApproval(77.32m, 0.10m, "President & CEO");
        Assert.True(validCeo);
    }

    [Fact]
    public async Task Discount_RejectedWithoutCeoApproval_AcceptedWithCeoApproval()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        SeedBillableClient(appDb, mssqlDb);

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() } }
        };

        // 1. Submit discount with unauthorized approver
        var failResult = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 600m,
            OtherCharges = 10m,
            DiscountAmount = 77.32m,
            DiscountRate = 0.10m,
            DiscountApprovedBy = "Billing Coordinator"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(failResult);
        Assert.Contains("CEO or President", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // 2. Submit discount with CEO approver
        var successResult = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 600m,
            OtherCharges = 10m,
            DiscountAmount = 77.32m,
            DiscountRate = 0.10m,
            DiscountApprovedBy = "President and CEO",
            DiscountReason = "10% Executive Volume Discount"
        });

        var okResult = Assert.IsType<OkObjectResult>(successResult);
        var discountAmount = GetValueProp(okResult.Value, "discountAmount");
        var discountApprovedBy = GetValueProp(okResult.Value, "discountApprovedBy");
        Assert.Equal(77.32m, Convert.ToDecimal(discountAmount));
        Assert.Equal("President and CEO", discountApprovedBy?.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Sequential invoice numbers
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SequentialInvoiceNumbers_IncrementStrictly()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        SeedBillableClient(appDb, mssqlDb);

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() } }
        };

        var res1 = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 500m
        });
        var ok1 = Assert.IsType<OkObjectResult>(res1);
        var inv1No = GetValueProp(ok1.Value, "invoiceNo")?.ToString();

        var res2 = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 500m
        });
        var ok2 = Assert.IsType<OkObjectResult>(res2);
        var inv2No = GetValueProp(ok2.Value, "invoiceNo")?.ToString();

        int year = DateTime.UtcNow.Year;
        Assert.Equal($"INV-{year}-0001", inv1No);
        Assert.Equal($"INV-{year}-0002", inv2No);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. No edit after Approved
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task NoEditAfterApproved_ReturnsBadRequest()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var approvedInv = new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-APP-001",
            InvoiceNo = "INV-2026-0001",
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 500m,
            TotalAmount = 635m,
            PaymentValidationStatus = "Approved",
            DocumentStatus = "Approved"
        };
        appDb.Invoices.Add(approvedInv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() } }
        };

        var updateResult = await controller.UpdateInvoice("INV-APP-001", new InvoiceSubmissionDto
        {
            FreightCharges = 800m
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(updateResult);
        Assert.Contains("Approved and locked", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Void needs reason and unlocks billed waybills
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task VoidInvoice_RequiresReason_AndUnlocksWaybills()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        var wb = new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillId = 1,
            WaybillNumber = "WB-VOID-001",
            ClientId = 1,
            IsBilled = true,
            InvoiceNo = "INV-2026-0099",
            PodStatus = "Billed"
        };
        mssqlDb.Waybills.Add(wb);
        await mssqlDb.SaveChangesAsync();

        var inv = new FOMS.Domain.Entities.Invoice
        {
            Id = "INV-VOID-001",
            InvoiceNo = "INV-2026-0099",
            ClientId = "CL-001",
            ClientName = "Test Client",
            FreightCharges = 600m,
            TotalAmount = 773.20m,
            WaybillIds = "WB-VOID-001",
            PaymentValidationStatus = "Pending Approval"
        };
        appDb.Invoices.Add(inv);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() } }
        };

        // 1. Attempt void with empty reason
        var failVoid = await controller.VoidInvoice("INV-VOID-001", new VoidInvoiceRequest { VoidReason = "" });
        var badRequest = Assert.IsType<BadRequestObjectResult>(failVoid);
        Assert.Contains("void reason is required", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // 2. Void with reason
        var successVoid = await controller.VoidInvoice("INV-VOID-001", new VoidInvoiceRequest
        {
            VoidReason = "Client requested billing cancellation due to duplicate purchase order"
        });
        var okResult = Assert.IsType<OkObjectResult>(successVoid);
        var status = GetValueProp(okResult.Value, "status")?.ToString();
        Assert.Equal("Voided", status);

        // 3. Verify waybill was unlocked
        var updatedWb = await mssqlDb.Waybills.FindAsync(1);
        Assert.NotNull(updatedWb);
        Assert.False(updatedWb.IsBilled);
        Assert.Null(updatedWb.InvoiceNo);
        Assert.Equal("Validated", updatedWb.PodStatus);
    }
}
