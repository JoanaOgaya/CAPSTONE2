using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class PodLifecycleAndBillingTests
{
    private FomsDbContext CreateMssqlDb(string name)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options;
        return new FomsDbContext(options);
    }

    private ApplicationDbContext CreateAppDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private ClaimsPrincipal CreateStaffPrincipal(string name = "Staff Accountant", string role = "Accountant", string id = "EMP-003")
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim("name", name),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role)
        }, "TestAuth"));
    }

    [Fact]
    public async Task ManualEntry_WithoutExceptionReason_IsRejected()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var controller = new ShipmentRecordsController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        // Attempt manual entry without exception flag or reason
        var result = await controller.Create(new ManualWaybillDto
        {
            WaybillNumber = "WB-MAN-001",
            ClientId = 1,
            IsException = false
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("only permitted for exceptions", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualEntry_WithException_SucceedsAndLogsAudit()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var controller = new ShipmentRecordsController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        var result = await controller.Create(new ManualWaybillDto
        {
            WaybillNumber = "WB-EXC-001",
            ClientId = 1,
            IsException = true,
            ExceptionReason = "Network offline at province hub; paper waybill physical intake."
        });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var wb = Assert.IsType<FOMS.Domain.Entities.SqlServer.Waybill>(created.Value);
        Assert.True(wb.IsManualEntry);
        Assert.True(wb.IsException);
        Assert.Equal("Pending", wb.PodStatus);

        // Audit trail record written
        var audit = await mssqlDb.AuditLogs.FirstOrDefaultAsync(a => a.action == "MANUAL_WAYBILL_EXCEPTION_CREATED");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task DuplicateWaybill_AtEntry_AlertsAndRejects()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        mssqlDb.Waybills.Add(new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillId = 1,
            WaybillNumber = "WB-DUP-001",
            ClientId = 1,
            PodStatus = "Pending"
        });
        await mssqlDb.SaveChangesAsync();

        var controller = new ShipmentRecordsController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        // Try to manually enter the same waybill number
        var result = await controller.Create(new ManualWaybillDto
        {
            WaybillNumber = "WB-DUP-001",
            ClientId = 1,
            IsException = true,
            ExceptionReason = "Test re-entry"
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("duplicate", conflict.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Duplicate alert must be logged in WaybillDuplicateAlerts
        var alert = await mssqlDb.WaybillDuplicateAlerts.FirstOrDefaultAsync(a => a.WaybillNumber == "WB-DUP-001");
        Assert.NotNull(alert);
        Assert.Equal("Active", alert.Status);
    }

    [Fact]
    public async Task Billing_LackingValidatedClientSignatureOrAccountingCopy_TransitionsToOnHoldAndBlocks()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        // Client
        appDb.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "LZD-001",
            ClientCode = "LZD-001",
            Name = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.Clients.Add(new FOMS.Domain.Entities.SqlServer.Client
        {
            ClientId = 1,
            ClientCode = "LZD-001",
            CompanyName = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.ClientRates.Add(new ClientRate
        {
            RateId = 1,
            ClientId = 1,
            DeliveryArea = "NCR",
            ServiceType = "Standard Delivery",
            MinimumRate = 100m,
            IsActive = true
        });

        // Waybill with Validated status, BUT missing client signature or counterpart match
        mssqlDb.Waybills.Add(new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillId = 50,
            WaybillNumber = "WB-MISSING-MATCH-01",
            ClientId = 1,
            PodStatus = "Validated",
            ClientSigned = false, // Missing original client signature!
            AccountingCopyMatched = false,
            IsBilled = false
        });
        await appDb.SaveChangesAsync();
        await mssqlDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "LZD-001",
            FreightCharges = 1200m,
            WaybillIds = new List<string> { "WB-MISSING-MATCH-01" }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("On Hold", badRequest.Value?.ToString() ?? "");

        // Verify waybill was transitioned to On Hold
        var wb = await mssqlDb.Waybills.FindAsync(50);
        Assert.NotNull(wb);
        Assert.Equal("On Hold", wb.PodStatus);
    }

    [Fact]
    public async Task Billing_WithLostOriginal_RequiresApprovedCtc_AndFlagsInvoiceAndAudit()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        appDb.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "LZD-001",
            ClientCode = "LZD-001",
            Name = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.Clients.Add(new FOMS.Domain.Entities.SqlServer.Client
        {
            ClientId = 1,
            ClientCode = "LZD-001",
            CompanyName = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.ClientRates.Add(new ClientRate
        {
            RateId = 1,
            ClientId = 1,
            DeliveryArea = "NCR",
            ServiceType = "Standard Delivery",
            MinimumRate = 100m,
            IsActive = true
        });

        // Waybill with lost original -> Approved CTC with document, reason, approver
        mssqlDb.Waybills.Add(new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillId = 60,
            WaybillNumber = "WB-CTC-001",
            ClientId = 1,
            PodStatus = "CTC",
            HasCtc = true,
            CtcDocumentUrl = "/uploads/ctc/wb-ctc-001-certified.pdf",
            CtcReason = "Original lost in transit during heavy monsoon; receiver affidavit and certified courier copy attached.",
            CtcApprovedBy = "Crystalyn Joyce C. Fajardo",
            CtcApprovedAt = DateTime.UtcNow,
            IsBilled = false
        });
        await appDb.SaveChangesAsync();
        await mssqlDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        var result = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "LZD-001",
            FreightCharges = 1500m,
            WaybillIds = new List<string> { "WB-CTC-001" }
        });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Invoice must be flagged with CTC
        var invoice = await appDb.Invoices.FirstOrDefaultAsync(i => i.WaybillIds == "WB-CTC-001");
        Assert.NotNull(invoice);
        Assert.True(invoice.HasCtc);
        Assert.Contains("WB-CTC-001", invoice.CtcWaybills ?? "");

        // Audit trail record for CTC invoice billing must exist
        var ctcAudit = await appDb.AuditLogs.FirstOrDefaultAsync(a => a.Action == "CTC_WAYBILL_FLAGGED");
        Assert.NotNull(ctcAudit);
        Assert.Contains("WB-CTC-001", ctcAudit.Details);
    }

    [Fact]
    public async Task Waybill_BillsOnce_AndLocksToInvoice()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());

        appDb.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "LZD-001",
            ClientCode = "LZD-001",
            Name = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.Clients.Add(new FOMS.Domain.Entities.SqlServer.Client
        {
            ClientId = 1,
            ClientCode = "LZD-001",
            CompanyName = "Lazada Philippines",
            SignedProposalAttached = true
        });
        mssqlDb.ClientRates.Add(new ClientRate
        {
            RateId = 1,
            ClientId = 1,
            DeliveryArea = "NCR",
            ServiceType = "Standard Delivery",
            MinimumRate = 100m,
            IsActive = true
        });

        // Validated waybill with matched copies
        mssqlDb.Waybills.Add(new FOMS.Domain.Entities.SqlServer.Waybill
        {
            WaybillId = 70,
            WaybillNumber = "WB-LOCK-001",
            ClientId = 1,
            PodStatus = "Validated",
            ClientSigned = true,
            AccountingCopyMatched = true,
            IsBilled = false
        });
        await appDb.SaveChangesAsync();
        await mssqlDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateStaffPrincipal() }
        };

        // 1st billing run -> succeeds and locks
        var result1 = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "LZD-001",
            FreightCharges = 1200m,
            WaybillIds = new List<string> { "WB-LOCK-001" }
        });
        Assert.IsType<OkObjectResult>(result1);

        var wb = await mssqlDb.Waybills.FindAsync(70);
        Assert.NotNull(wb);
        Assert.True(wb.IsBilled);
        Assert.False(string.IsNullOrWhiteSpace(wb.InvoiceNo));

        // 2nd billing run -> BLOCKED! A waybill bills once and locks to its invoice
        var result2 = await controller.CreateInvoice(new InvoiceSubmissionDto
        {
            ClientId = "LZD-001",
            FreightCharges = 1200m,
            WaybillIds = new List<string> { "WB-LOCK-001" }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result2);
        Assert.Contains("already been billed and is locked", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
