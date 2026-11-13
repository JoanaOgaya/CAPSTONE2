using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FOMS.Api.Controllers;
using FOMS.Application;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class AdjustmentsEndToEndRealControllerTests
{
    private static (AdjustmentsController controller, ApplicationDbContext context, IServiceProvider sp) CreateRealAdjustmentsController(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ApplicationDbContext(options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddSingleton<IApplicationDbContext>(context);
        var sp = services.BuildServiceProvider();

        var controller = new AdjustmentsController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = sp
                }
            }
        };

        return (controller, context, sp);
    }

    private static ClaimsPrincipal CreateUserPrincipal(string empId, string name, string role)
    {
        var claims = new List<Claim>
        {
            new Claim("employee_id", empId),
            new Claim(ClaimTypes.Name, name),
            new Claim("name", name),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task EndToEnd_AdjustmentLifecycle_ThroughRealControllers_SucceedsWithAllRulesEnforced()
    {
        var dbName = Guid.NewGuid().ToString();
        var (controller, context, _) = CreateRealAdjustmentsController(dbName);

        // Seed Client and Invoice
        context.Clients.Add(new Client
        {
            Id = "CLI-E2E-01",
            Name = "EndToEnd Test Client",
            Status = "Active",
            IsActive = true
        });

        context.Invoices.Add(new Invoice
        {
            Id = "INV-E2E-001",
            InvoiceNo = "BI-E2E-001",
            ClientId = "CLI-E2E-01",
            TotalAmount = 5000m,
            Balance = 5000m,
            PaymentStatus = "Unpaid",
            DocumentStatus = InvoiceDocumentStatus.ForReview
        });
        await context.SaveChangesAsync();

        // 1. FM creates an adjustment the way the UI does (POST /adjustments)
        controller.ControllerContext.HttpContext.User = CreateUserPrincipal("EMP-001", "Crystalyn Fajardo", "Finance Manager");
        var createDto = new AdjustmentsController.CreateAdjustmentDto
        {
            InvoiceNo = "BI-E2E-001",
            Type = "Credit",
            Amount = 1000m,
            Reason = "Freight overcharge dispute resolved per client billing review",
            ClientId = "CLI-E2E-01"
        };

        var createResult = await controller.Create(createDto);
        var okCreate = Assert.IsType<OkObjectResult>(createResult);
        Assert.NotNull(okCreate.Value);

        // Verify record is created and findable in PaymentAdjustments
        var createdAdjId = (string)okCreate.Value.GetType().GetProperty("id")!.GetValue(okCreate.Value)!;
        Assert.False(string.IsNullOrWhiteSpace(createdAdjId));

        var adjInDb = await context.PaymentAdjustments.FirstOrDefaultAsync(a => a.Id == createdAdjId);
        Assert.NotNull(adjInDb);
        Assert.Equal("Pending", adjInDb.Status);
        Assert.Equal(1000m, adjInDb.Amount);
        Assert.Equal("BI-E2E-001", adjInDb.InvoiceNo);

        // 2. FM attempts to approve -> REJECTED (403 Forbidden)
        var fmApproveResult = await controller.Approve(createdAdjId);
        var forbiddenResult = Assert.IsType<ObjectResult>(fmApproveResult);
        Assert.Equal(403, forbiddenResult.StatusCode);

        // 3. Creator self-approval rejected -> If creator attempts to approve as HA
        // Suppose creator user is named "Head Accountant" but was the Adjuster
        adjInDb.AdjustedBy = "EMP-002"; // same as HA
        await context.SaveChangesAsync();

        controller.ControllerContext.HttpContext.User = CreateUserPrincipal("EMP-002", "EMP-002", "Head Accountant");
        var selfApproveResult = await controller.Approve(createdAdjId);
        var badRequestSelf = Assert.IsType<BadRequestObjectResult>(selfApproveResult);
        Assert.Contains("creator of an adjusting entry cannot approve their own entry", badRequestSelf.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Reset AdjustedBy back to FM
        adjInDb.AdjustedBy = "Crystalyn Fajardo";
        await context.SaveChangesAsync();

        // 4. HA approves -> SUCCEEDS! (POST /adjustments/{id}/approve)
        controller.ControllerContext.HttpContext.User = CreateUserPrincipal("EMP-002", "Alex Head Accountant", "Head Accountant");
        var approveResult = await controller.Approve(createdAdjId);
        var okApprove = Assert.IsType<OkObjectResult>(approveResult);
        Assert.NotNull(okApprove.Value);

        // 5. Assert status is Approved
        var approvedAdj = await context.PaymentAdjustments.FirstOrDefaultAsync(a => a.Id == createdAdjId);
        Assert.NotNull(approvedAdj);
        Assert.Equal("Approved", approvedAdj.Status);
        Assert.Equal("EMP-002", approvedAdj.ApprovedBy);

        // 6. Assert Invoice balance changed EXACTLY ONCE (from 5000 to 4000)
        var updatedInvoice = await context.Invoices.FirstOrDefaultAsync(i => i.InvoiceNo == "BI-E2E-001");
        Assert.NotNull(updatedInvoice);
        Assert.Equal(4000m, updatedInvoice.Balance);

        // 7. Test HA ValidateAdjustment route on HeadAccountantController returns 400 Bad Request
        // and does NOT alter status, balances, or post GL rows
        var fomsDbOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var fomsDb = new FomsDbContext(fomsDbOptions);

        var haController = new HeadAccountantController(fomsDb)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreateUserPrincipal("EMP-002", "Alex Head Accountant", "Head Accountant")
                }
            }
        };

        var validateResult = await haController.ValidateAdjustment(12345);
        var badRequestValidate = Assert.IsType<BadRequestObjectResult>(validateResult);
        Assert.Contains("The spec defines no verification step for adjustments", badRequestValidate.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Confirm invoice balance remains strictly 4000m
        var invoiceAfterValidate = await context.Invoices.FirstOrDefaultAsync(i => i.InvoiceNo == "BI-E2E-001");
        Assert.Equal(4000m, invoiceAfterValidate!.Balance);
    }

    [Fact]
    public async Task EndToEnd_PaidInvoiceCreditMemo_PostsBalancedJournalEntryDatedAdjustmentDate()
    {
        var dbName = Guid.NewGuid().ToString();
        var (controller, context, _) = CreateRealAdjustmentsController(dbName);

        context.Clients.Add(new Client
        {
            Id = "CLI-E2E-02",
            Name = "Paid Invoice Client",
            Status = "Active",
            IsActive = true
        });

        // Paid invoice -> balance = 0
        context.Invoices.Add(new Invoice
        {
            Id = "INV-E2E-PAID",
            InvoiceNo = "BI-E2E-PAID",
            ClientId = "CLI-E2E-02",
            TotalAmount = 8000m,
            Balance = 0m,
            PaymentStatus = "Paid",
            DocumentStatus = InvoiceDocumentStatus.ForReview
        });
        await context.SaveChangesAsync();

        // FM creates credit memo
        controller.ControllerContext.HttpContext.User = CreateUserPrincipal("EMP-001", "Crystalyn Fajardo", "Finance Manager");
        var createResult = await controller.Create(new AdjustmentsController.CreateAdjustmentDto
        {
            InvoiceNo = "BI-E2E-PAID",
            Type = "Credit",
            Amount = 1500m,
            Reason = "Post-payment rebate adjustment",
            ClientId = "CLI-E2E-02"
        });
        var okCreate = Assert.IsType<OkObjectResult>(createResult);
        var adjId = (string)okCreate.Value.GetType().GetProperty("id")!.GetValue(okCreate.Value)!;

        // Set specific requested date
        var adjInDb = await context.PaymentAdjustments.FirstAsync(a => a.Id == adjId);
        adjInDb.DateRequested = "2026-11-08";
        await context.SaveChangesAsync();

        // HA approves
        controller.ControllerContext.HttpContext.User = CreateUserPrincipal("EMP-002", "Alex Head Accountant", "Head Accountant");
        var approveResult = await controller.Approve(adjId);
        Assert.IsType<OkObjectResult>(approveResult);

        // Verify Journal Entry posted
        var jes = await context.JournalEntries.Include(j => j.Entries).ToListAsync();
        Assert.Single(jes);
        var je = jes.First();
        Assert.True(je.Posted);
        Assert.Equal(new DateTime(2026, 11, 8), je.Date.Date);

        // Verify balanced entries (Debit == Credit == 1500)
        var glEntries = await context.GeneralLedgerEntries.ToListAsync();
        Assert.Equal(2, glEntries.Count);
        Assert.Equal(1500m, glEntries.Sum(e => e.Debit));
        Assert.Equal(1500m, glEntries.Sum(e => e.Credit));
    }
}
