using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class HistoricalThresholdDatingRealServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CashAdvance_Release_EvaluatedAgainstCreationDateThreshold_SucceedsPreChange_FailsPostChange()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var disbService = new DisbursementAndReimbursementService(context, gateService);

        var oct1 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var oct15 = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        // Threshold on Oct 1 is 50,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000m,
            EffectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Reason = "Initial high threshold"
        }, "EMP-001", "Finance Manager");

        // Threshold lowered to 10,000 on Oct 15
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 10000m,
            EffectiveFrom = oct15,
            Reason = "Lowered threshold"
        }, "EMP-001", "Finance Manager");

        // Budget line
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 3001,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FIN",
            Category = "Fuel",
            Month = 10,
            BudgetAmount = 100000m,
            CommittedAmount = 0m,
            ActualAmount = 0m
        });

        // Advance 1: created Oct 1 for 25,000
        var ca1 = new CashAdvance
        {
            AdvanceId = "CA-HIST-01",
            CourierName = "Courier 1",
            TripReference = "TRIP-01",
            AmountGiven = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Fuel",
            FiscalYear = 2026,
            Month = 10,
            Status = "Approved",
            CreatedAt = oct1
        };

        // Advance 2: created Oct 20 for 25,000
        var ca2 = new CashAdvance
        {
            AdvanceId = "CA-HIST-02",
            CourierName = "Courier 2",
            TripReference = "TRIP-02",
            AmountGiven = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Fuel",
            FiscalYear = 2026,
            Month = 10,
            Status = "Approved",
            CreatedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
        };

        context.CashAdvances.AddRange(ca1, ca2);
        await context.SaveChangesAsync();

        // 1. Release CA-HIST-01: Created Oct 1 (threshold 50k). 25k <= 50k -> Release succeeds without CEO approval!
        var rel1 = await disbService.ReleaseCashAdvanceAsync("CA-HIST-01", "EMP-001", "Finance Manager");
        Assert.NotNull(rel1);
        Assert.Equal("Released", rel1.Status);

        // 2. Release CA-HIST-02: Created Oct 20 (threshold 10k). 25k > 10k -> Release BLOCKED without CEO approval!
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            disbService.ReleaseCashAdvanceAsync("CA-HIST-02", "EMP-001", "Finance Manager"));
        Assert.Contains("requires CEO/President approval record", ex.Message);

        // Record approval for CA-HIST-02 -> Release now succeeds!
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-HIST-02",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var rel2 = await disbService.ReleaseCashAdvanceAsync("CA-HIST-02", "EMP-001", "Finance Manager");
        Assert.NotNull(rel2);
        Assert.Equal("Released", rel2.Status);
    }

    [Fact]
    public async Task DirectDisbursement_Release_EvaluatedAgainstCreationDateThreshold()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var disbService = new DisbursementAndReimbursementService(context, gateService);

        var oct1 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var oct15 = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000m,
            EffectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Reason = "Initial high threshold"
        }, "EMP-001", "Finance Manager");

        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 10000m,
            EffectiveFrom = oct15,
            Reason = "Lowered threshold"
        }, "EMP-001", "Finance Manager");

        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 3002,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FIN",
            Category = "Office Supplies",
            Month = 10,
            BudgetAmount = 100000m,
            CommittedAmount = 0m,
            ActualAmount = 0m
        });

        // Disb 1: created Oct 1 for 25,000
        var d1 = new DirectDisbursement
        {
            DisbursementId = 4001,
            Purpose = "Office supplies purchase",
            Amount = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Office Supplies",
            FiscalYear = 2026,
            Month = 10,
            Status = "Approved",
            CreatedAt = oct1
        };

        // Disb 2: created Oct 20 for 25,000
        var d2 = new DirectDisbursement
        {
            DisbursementId = 4002,
            Purpose = "Office supplies purchase",
            Amount = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Office Supplies",
            FiscalYear = 2026,
            Month = 10,
            Status = "Approved",
            CreatedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
        };

        context.DirectDisbursements.AddRange(d1, d2);
        await context.SaveChangesAsync();

        // Release D1: Created Oct 1 (threshold 50k) -> succeeds!
        var rel1 = await disbService.ReleaseDirectDisbursementAsync(4001, "EMP-001", "Finance Manager");
        Assert.NotNull(rel1);
        Assert.Equal("Released", rel1.Status);

        // Release D2: Created Oct 20 (threshold 10k) -> fails!
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            disbService.ReleaseDirectDisbursementAsync(4002, "EMP-001", "Finance Manager"));
        Assert.Contains("requires CEO/President approval record", ex.Message);
    }
}
