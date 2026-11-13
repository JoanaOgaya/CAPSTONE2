using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class Phase18DisbursementsLiquidationsAndNetCashTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed CSH-09 threshold limit (provisional Limit A = 10,000)
        context.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            TransactionType = "CashOutflow",
            RoleName = "Assistant of Finance Manager",
            MaximumAmount = 10000.00m,
            IsActive = true
        });

        context.CeoApprovalThresholdSettings.Add(new CeoApprovalThresholdSetting
        {
            SettingId = 1,
            SettingKey = "CSH-09_CEO_THRESHOLD",
            ThresholdAmount = 10000.00m,
            Currency = "PHP",
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            IsActive = true,
            UpdatedBy = "EMP-001",
            UpdatedAt = DateTime.UtcNow.AddYears(-1)
        });

        // Seed monthly budget lines for 2026 / Month 1
        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine
            {
                FiscalYear = 2026,
                DepartmentCode = "DEP-OPS",
                DepartmentName = "Operations",
                Category = "Fuel",
                Month = 1,
                BudgetAmount = 100000m,
                ActualAmount = 20000m,
                CommittedAmount = 0m
            },
            new MonthlyBudgetLine
            {
                FiscalYear = 2026,
                DepartmentCode = "DEP-OPS",
                DepartmentName = "Operations",
                Category = "Repairs and Maintenance",
                Month = 1,
                BudgetAmount = 50000m,
                ActualAmount = 10000m,
                CommittedAmount = 0m
            },
            new MonthlyBudgetLine
            {
                FiscalYear = 2026,
                DepartmentCode = "DEP-OPS",
                DepartmentName = "Operations",
                Category = "Food Allowance",
                Month = 1,
                BudgetAmount = 30000m,
                ActualAmount = 5000m,
                CommittedAmount = 0m
            },
            new MonthlyBudgetLine
            {
                FiscalYear = 2026,
                DepartmentCode = "DEP-FIN",
                DepartmentName = "Finance & Accounting",
                Category = "Office Supplies",
                Month = 1,
                BudgetAmount = 25000m,
                ActualAmount = 2000m,
                CommittedAmount = 0m
            }
        );

        context.SaveChanges();
        return context;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CASH ADVANCE LIFECYCLE & BUDGET EFFECTS (Requested -> Approved -> Released -> Liquidated)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CashAdvance_StatusTransitions_AndBudgetEffects()
    {
        using var context = CreateContext(nameof(CashAdvance_StatusTransitions_AndBudgetEffects));
        var gateService = new CeoApprovalGateService(context);
        var service = new DisbursementAndReimbursementService(context, gateService);

        // A. Requested: Due next day for NCR, budget uncommitted
        var req = new RequestCashAdvanceDto
        {
            CourierName = "Juan Courier",
            TripReference = "TRIP-NCR-001",
            Amount = 6000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS",
            LocationType = "NCR",
            FiscalYear = 2026,
            Month = 1
        };

        var ca = await service.RequestCashAdvanceAsync(req, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", ca.Status);
        Assert.NotNull(ca.DueDate);
        Assert.False(ca.IsLate);

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(80000m, budgetLine.Available); // 100k - 20k actual

        // B. Approved: Reserves Committed (Available drops to 74,000)
        var approved = await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(6000m, budgetLine.CommittedAmount);
        Assert.Equal(74000m, budgetLine.Available);

        // C. Released: Posts cash outflow, advances to Released
        var released = await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", released.Status);

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == ca.AdvanceId);
        Assert.NotNull(outflow);
        Assert.Equal(6000m, outflow.Amount);

        // D. Liquidated: 5,000 actual expenses -> 1,000 leftover cash return
        var liqReq = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Juan Courier",
            LineItems = new List<LiquidationLineItemDto>
            {
                new()
                {
                    Category = "Fuel",
                    Description = "Official Station Diesel",
                    Amount = 5000m,
                    ReceiptAttached = true,
                    ReceiptNumber = "OR-DIESEL-01"
                }
            }
        };

        var liq = await service.SubmitLiquidationAsync(liqReq, "EMP-COURIER-01", "Courier");
        Assert.Equal("Pending", liq.Status);
        Assert.Equal(1000m, liq.LeftoverCash);

        // Assistant FM validates alone (5,000 <= 10,000 threshold)
        var validatedLiq = await service.ValidateLiquidationAsync(
            new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validatedLiq.Status);

        // Advance converted to Liquidated
        var caEntity = await context.CashAdvances.FindAsync(ca.AdvanceId);
        Assert.Equal("Liquidated", caEntity!.Status);

        // Budget committed is released, actual increases by 5,000, and unused commitment is released
        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(25000m, budgetLine.ActualAmount); // 20k + 5k
        Assert.Equal(75000m, budgetLine.Available); // 100k allocated - 25k actual - 0 committed (1,000 unused reservation released)

        // Leftover cash (1,000) posted as Inflow
        var inflow = await context.CashInflows.FirstOrDefaultAsync(i => i.Source.Contains(liq.ReferenceNumber));
        Assert.NotNull(inflow);
        Assert.Equal(1000m, inflow.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. REJECTED / CANCELLED CASH ADVANCE RELEASES COMMITTED BUDGET
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CashAdvance_RejectedAndCancelled_ReleasesCommittedBudget()
    {
        using var context = CreateContext(nameof(CashAdvance_RejectedAndCancelled_ReleasesCommittedBudget));
        var service = new DisbursementAndReimbursementService(context);

        var req = new RequestCashAdvanceDto
        {
            CourierName = "Test Courier",
            Amount = 4000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var ca = await service.RequestCashAdvanceAsync(req, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(4000m, budgetLine.CommittedAmount);

        // Cancel releases committed reservation
        var cancelled = await service.CancelCashAdvanceAsync(ca.AdvanceId, "Trip cancelled", "EMP-001", "Finance Manager");
        Assert.Equal("Cancelled", cancelled.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);

        // Reject also releases committed reservation
        var ca2Req = new RequestCashAdvanceDto
        {
            CourierName = "Test Courier 2",
            Amount = 3500m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var ca2 = await service.RequestCashAdvanceAsync(ca2Req, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca2.AdvanceId, "EMP-001", "Finance Manager");

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(3500m, budgetLine.CommittedAmount);

        var rejected = await service.RejectCashAdvanceAsync(ca2.AdvanceId, "Route suspended", "EMP-001", "Finance Manager");
        Assert.Equal("Rejected", rejected.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. THRESHOLD BOUNDARY & CEO APPROVAL TICK (Below, At, Above)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CashAdvance_ThresholdBoundary_AndCeoApprovalGate()
    {
        using var context = CreateContext(nameof(CashAdvance_ThresholdBoundary_AndCeoApprovalGate));
        var gateService = new CeoApprovalGateService(context);
        var service = new DisbursementAndReimbursementService(context, gateService);

        // A. At threshold (10,000.00): No CEO tick required
        var caAtReq = new RequestCashAdvanceDto
        {
            CourierName = "Boundary Driver",
            TripReference = "TRIP-BOUND-01",
            Amount = 10000.00m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var caAt = await service.RequestCashAdvanceAsync(caAtReq, "EMP-004", "Assistant of Finance Manager");
        Assert.False(caAt.RequiresCeoApproval);

        var approvedAt = await service.ApproveCashAdvanceAsync(caAt.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedAt.Status);

        // B. Above threshold (10,000.01): CEO approval tick required before approval and release
        var caAboveReq = new RequestCashAdvanceDto
        {
            CourierName = "Long Haul Driver",
            TripReference = "TRIP-ABOVE-01",
            Amount = 10000.01m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var caAbove = await service.RequestCashAdvanceAsync(caAboveReq, "EMP-004", "Assistant of Finance Manager");
        Assert.True(caAbove.RequiresCeoApproval);

        // FM approves cash advance (commits budget)
        var approvedAbove = await service.ApproveCashAdvanceAsync(caAbove.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedAbove.Status);

        // Release blocked until CEO tick is recorded for CashOutflow above threshold
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReleaseCashAdvanceAsync(caAbove.AdvanceId, "EMP-001", "Finance Manager"));

        // Record CEO approval record (Item 6: CashOutflow)
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = caAbove.AdvanceId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            Notes = "CEO authorized long haul fuel advance"
        }, "EMP-001", "Finance Manager");

        // Release now succeeds and creates cash outflow
        var releasedAbove = await service.ReleaseCashAdvanceAsync(caAbove.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", releasedAbove.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. SEPARATION OF DUTIES: SELF-APPROVAL BLOCKED ACROSS ALL ENTITIES
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SeparationOfDuties_RequesterCannotApproveSelf()
    {
        using var context = CreateContext(nameof(SeparationOfDuties_RequesterCannotApproveSelf));
        var service = new DisbursementAndReimbursementService(context);

        // A. Cash Advance: FM requesting advance cannot approve own advance
        var caReq = new RequestCashAdvanceDto
        {
            CourierName = "Self Request Driver",
            Amount = 3000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var ca = await service.RequestCashAdvanceAsync(caReq, "EMP-001", "Finance Manager");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager"));

        // B. Reimbursement: Submitter cannot validate or approve own reimbursement
        var reimReq = new SubmitReimbursementDto
        {
            EmployeeId = "EMP-004",
            EmployeeName = "Assistant FM Submitter",
            DepartmentCode = "DEP-OPS",
            Category = "Food Allowance",
            Amount = 1500m,
            Purpose = "Team meal"
        };
        var reim = await service.SubmitReimbursementAsync(reimReq, "EMP-004", "Assistant of Finance Manager");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ValidateReimbursementAsync(reim.ReimbursementId, "EMP-004", "Assistant of Finance Manager"));

        // C. Liquidation: Submitter cannot validate own liquidation
        var caForLiqReq = new RequestCashAdvanceDto
        {
            CourierName = "Courier For Liq",
            Amount = 3000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var caForLiq = await service.RequestCashAdvanceAsync(caForLiqReq, "EMP-002", "Head Accountant");
        await service.ApproveCashAdvanceAsync(caForLiq.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(caForLiq.AdvanceId, "EMP-001", "Finance Manager");

        var liqSubmit = new SubmitLiquidationDto
        {
            AdvanceId = caForLiq.AdvanceId,
            SubmittedByName = "EMP-004",
            LineItems = new List<LiquidationLineItemDto>
            {
                new() { Category = "Fuel", Amount = 3000m, ReceiptAttached = true }
            }
        };
        var liq = await service.SubmitLiquidationAsync(liqSubmit, "EMP-004", "Assistant of Finance Manager");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ValidateLiquidationAsync(new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-004", "Assistant of Finance Manager"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. DIRECT DISBURSEMENT (CSH-12: Actual at release, no liquidation)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DirectDisbursement_CSH12_ActualAtRelease_NoLiquidation()
    {
        using var context = CreateContext(nameof(DirectDisbursement_CSH12_ActualAtRelease_NoLiquidation));
        var gateService = new CeoApprovalGateService(context);
        var service = new DisbursementAndReimbursementService(context, gateService);

        var req = new RequestDirectDisbursementDto
        {
            DepartmentCode = "DEP-OPS",
            Category = "Repairs and Maintenance",
            Amount = 8500m,
            Purpose = "Fleet truck transmission oil servicing",
            FiscalYear = 2026,
            Month = 1
        };

        // 1. Requested
        var disb = await service.RequestDirectDisbursementAsync(req, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", disb.Status);

        // 2. Approved = Committed
        var approved = await service.ApproveDirectDisbursementAsync(disb.DisbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Repairs and Maintenance");
        Assert.Equal(8500m, budgetLine.CommittedAmount);

        // 3. Released = ACTUAL immediately without liquidation
        var released = await service.ReleaseDirectDisbursementAsync(disb.DisbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", released.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Repairs and Maintenance");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(18500m, budgetLine.ActualAmount); // 10k initial + 8.5k

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == disb.ReferenceNumber);
        Assert.NotNull(outflow);
        Assert.Equal(8500m, outflow.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. REIMBURSEMENT WORKFLOW: Assistant Validates -> FM Approves -> Paid
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reimbursement_StrictOrder_AssistantValidates_FmApproves_ThenPaid()
    {
        using var context = CreateContext(nameof(Reimbursement_StrictOrder_AssistantValidates_FmApproves_ThenPaid));
        var service = new DisbursementAndReimbursementService(context);

        var req = new SubmitReimbursementDto
        {
            EmployeeId = "EMP-003",
            EmployeeName = "Accountant Staff",
            DepartmentCode = "DEP-OPS",
            Category = "Food Allowance",
            Amount = 2500m,
            Purpose = "Closing audit dinner",
            FiscalYear = 2026,
            Month = 1
        };

        // 1. Submitted
        var reim = await service.SubmitReimbursementAsync(req, "EMP-003", "Accountant");
        Assert.Equal("Submitted", reim.Status);

        // Skipped Tier Guard: FM cannot approve before Assistant FM validation
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveReimbursementAsync(reim.ReimbursementId, "EMP-001", "Finance Manager"));

        // 2. Validated by Assistant of Finance Manager
        var validated = await service.ValidateReimbursementAsync(reim.ReimbursementId, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validated.Status);

        // 3. Approved by Finance Manager = COMMITTED
        var approved = await service.ApproveReimbursementAsync(reim.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Food Allowance");
        Assert.Equal(2500m, budgetLine.CommittedAmount);

        // 4. Paid = ACTUAL
        var paid = await service.PayReimbursementAsync(reim.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Paid", paid.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Food Allowance");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(7500m, budgetLine.ActualAmount); // 5k initial + 2.5k

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == reim.ReferenceNumber);
        Assert.NotNull(outflow);
        Assert.Equal(2500m, outflow.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. LIQUIDATION ROUTING: Assistant Validates Alone At/Below CSH-09 Threshold
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Liquidation_AssistantValidatesAloneAtOrBelowThreshold_FMAboveThreshold()
    {
        using var context = CreateContext(nameof(Liquidation_AssistantValidatesAloneAtOrBelowThreshold_FMAboveThreshold));
        var gateService = new CeoApprovalGateService(context);
        var service = new DisbursementAndReimbursementService(context, gateService);

        // Advance 1: 8,000 (<= 10,000 threshold)
        var caReq1 = new RequestCashAdvanceDto { CourierName = "Courier 1", Amount = 8000m, Category = "Fuel", DepartmentCode = "DEP-OPS" };
        var ca1 = await service.RequestCashAdvanceAsync(caReq1, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca1.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(ca1.AdvanceId, "EMP-001", "Finance Manager");

        var liqReq1 = new SubmitLiquidationDto
        {
            AdvanceId = ca1.AdvanceId,
            SubmittedByName = "Courier 1",
            LineItems = new List<LiquidationLineItemDto>
            {
                new() { Category = "Fuel", Amount = 8000m, ReceiptAttached = true }
            }
        };
        var liq1 = await service.SubmitLiquidationAsync(liqReq1, "EMP-COURIER-01", "Courier");

        // Assistant FM validates alone at or below threshold without requiring FM!
        var validatedLiq1 = await service.ValidateLiquidationAsync(
            new ValidateLiquidationDto { LiquidationId = liq1.LiquidationId }, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validatedLiq1.Status);

        // Advance 2: 15,000 (> 10,000 threshold)
        var caReq2 = new RequestCashAdvanceDto { CourierName = "Courier 2", Amount = 15000m, Category = "Fuel", DepartmentCode = "DEP-OPS" };
        var ca2 = await service.RequestCashAdvanceAsync(caReq2, "EMP-004", "Assistant of Finance Manager");

        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = ca2.AdvanceId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        await service.ApproveCashAdvanceAsync(ca2.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(ca2.AdvanceId, "EMP-001", "Finance Manager");

        var liqReq2 = new SubmitLiquidationDto
        {
            AdvanceId = ca2.AdvanceId,
            SubmittedByName = "Courier 2",
            LineItems = new List<LiquidationLineItemDto>
            {
                new() { Category = "Fuel", Amount = 15000m, ReceiptAttached = true }
            }
        };
        var liq2 = await service.SubmitLiquidationAsync(liqReq2, "EMP-COURIER-02", "Courier");

        // Non-assistant role cannot validate liquidations
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ValidateLiquidationAsync(new ValidateLiquidationDto { LiquidationId = liq2.LiquidationId }, "EMP-003", "Accountant"));

        // Assistant FM validates liquidation
        var validatedLiq2 = await service.ValidateLiquidationAsync(
            new ValidateLiquidationDto { LiquidationId = liq2.LiquidationId }, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validatedLiq2.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. LIQUIDATION REJECTION REASONS & FOR CORRECTION STATUS
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Liquidation_RejectionReasons_Dropdown_ReturnsForCorrection()
    {
        using var context = CreateContext(nameof(Liquidation_RejectionReasons_Dropdown_ReturnsForCorrection));
        var service = new DisbursementAndReimbursementService(context);

        var caReq = new RequestCashAdvanceDto { CourierName = "Receipt Check", Amount = 3000m, Category = "Fuel", DepartmentCode = "DEP-OPS" };
        var ca = await service.RequestCashAdvanceAsync(caReq, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        var liqReq = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Receipt Check",
            LineItems = new List<LiquidationLineItemDto>
            {
                new() { Category = "Fuel", Amount = 3000m, ReceiptAttached = true, IsManualFuelReceipt = true }
            }
        };
        var liq = await service.SubmitLiquidationAsync(liqReq, "EMP-COURIER-03", "Courier");

        // Fixed rejection reason options: wrong receipt date or amount, manual fuel receipt, late submission, other with remarks
        Assert.True(LiquidationRejectionReasons.IsValid("wrong receipt date or amount"));
        Assert.True(LiquidationRejectionReasons.IsValid("manual fuel receipt"));
        Assert.True(LiquidationRejectionReasons.IsValid("late submission"));
        Assert.True(LiquidationRejectionReasons.IsValid("other with remarks"));
        Assert.False(LiquidationRejectionReasons.IsValid("invented reason"));

        // Rejection with "other with remarks" requires remarks
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RejectLiquidationAsync(new RejectLiquidationDto
            {
                LiquidationId = liq.LiquidationId,
                Reason = "other with remarks",
                Remarks = ""
            }, "EMP-004", "Assistant of Finance Manager"));

        // Reject liquidation: sets For Correction and leaves no ledger entry
        var rejected = await service.RejectLiquidationAsync(new RejectLiquidationDto
        {
            LiquidationId = liq.LiquidationId,
            Status = "For Correction",
            Reason = "manual fuel receipt",
            Remarks = "Manual handwriting on fuel receipt is prohibited"
        }, "EMP-004", "Assistant of Finance Manager");

        Assert.Equal("For Correction", rejected.Status);
        Assert.Equal("manual fuel receipt", rejected.RejectionReason);

        // No ledger entry created on rejection
        var initialGlCount = await context.GeneralLedgerEntries.CountAsync();
        Assert.Equal(0, initialGlCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. EXPENSE CATEGORIES & DEPARTMENT INTEGRATION (FIN-26, Phase 15 & 18)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Fuel")]
    [InlineData("Toll Fees")]
    [InlineData("Parking")]
    [InlineData("Food Allowance")]
    [InlineData("Vulcanizing")]
    [InlineData("Repairs and Maintenance")]
    [InlineData("Hotel")]
    [InlineData("Branch Visits")]
    [InlineData("Office Supplies")]
    [InlineData("Other")]
    public void ExpenseCategories_AllCanonicalCategories_AreValid(string category)
    {
        Assert.True(DisbursementCategories.IsValidCategory(category));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. NET CASH FORMULA (Direct Method: Beginning + Validated Inflows - Outflows)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task NetCash_DirectMethodCalculation_InflowsAndOutflows()
    {
        using var context = CreateContext(nameof(NetCash_DirectMethodCalculation_InflowsAndOutflows));

        // 1. Beginning balance: 50,000.00
        context.GoLiveOpeningBalances.Add(new GoLiveOpeningBalance
        {
            AccountCode = "1010",
            AccountName = "Cash on Hand",
            Debit = 50000m,
            Credit = 0m
        });

        // 2. Validated client payments: 100,000.00
        context.Payments.Add(new Domain.Entities.Payment
        {
            Id = "PAY-NET-01",
            Amount = 100000m,
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 1, 10)
        });

        // 3. Returned unused advance (inflow): 5,000.00
        context.CashInflows.Add(new CashInflow
        {
            Amount = 5000m,
            Source = "Liquidation LIQ-001 Cash Return",
            Status = "Completed",
            TransactionDate = new DateTime(2026, 1, 15)
        });

        // 4. Cash advance released (outflow): 20,000.00
        context.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-NET-01",
            AmountGiven = 20000m,
            Status = "Released",
            FiscalYear = 2026,
            Month = 1,
            ReleaseDate = new DateTime(2026, 1, 12)
        });

        // 5. Direct disbursement released (outflow): 10,000.00
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DD-NET-01",
            Amount = 10000m,
            Status = "Released",
            FiscalYear = 2026,
            Month = 1,
            ReleasedAt = new DateTime(2026, 1, 14)
        });

        // 6. Paid reimbursement (outflow): 5,000.00
        context.Reimbursements.Add(new Reimbursement
        {
            ReferenceNumber = "REIM-NET-01",
            Amount = 5000m,
            Status = "Paid",
            FiscalYear = 2026,
            Month = 1,
            PaidAt = new DateTime(2026, 1, 16)
        });

        await context.SaveChangesAsync();

        var gateService = new CeoApprovalGateService(context);
        var statementService = new FinancialStatementService(context, gateService);

        var cf = await statementService.GenerateCashFlowStatementAsync(2026, "Month", 1);

        // Verification of direct method formula:
        // Beginning: 50,000
        // Inflows: 100,000 (payments) + 5,000 (returned advance) = 105,000
        // Outflows: 20,000 (advance) + 10,000 (direct disb) + 5,000 (reimbursement) = 35,000
        // Net Change: 105,000 - 35,000 = +70,000
        // Ending Net Cash: 50,000 + 70,000 = 120,000
        Assert.Equal(50000m, cf.BeginningCash);
        Assert.Equal(105000m, cf.TotalOperatingInflows);
        Assert.Equal(35000m, cf.TotalOperatingOutflows);
        Assert.Equal(70000m, cf.NetCashFromOperating);
        Assert.Equal(120000m, cf.EndingNetCash);
    }

    [Fact]
    public async Task NetCash_PeriodChaining_BeginningCashMatchesPreviousPeriodEndingCash()
    {
        using var context = CreateContext(nameof(NetCash_PeriodChaining_BeginningCashMatchesPreviousPeriodEndingCash));

        // Opening cash = 40,000 (GoLiveOpeningBalances: Account 1010 + 1030 mapped to 1010)
        context.GoLiveOpeningBalances.Add(new GoLiveOpeningBalance
        {
            AccountCode = "1010",
            AccountName = "Cash on Hand",
            Debit = 30000m,
            Credit = 0m
        });
        context.GoLiveOpeningBalances.Add(new GoLiveOpeningBalance
        {
            AccountCode = "1030", // Petty cash mapped to 1010
            AccountName = "Petty Cash",
            Debit = 10000m,
            Credit = 0m
        });

        // Month 1 (January): Validated payment of 50,000 and released advance of 20,000
        context.Payments.Add(new Domain.Entities.Payment
        {
            Id = "PAY-JAN-01",
            Amount = 50000m,
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 1, 15)
        });
        context.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-JAN-01",
            AmountGiven = 20000m,
            Status = "Released",
            ReleaseDate = new DateTime(2026, 1, 20),
            FiscalYear = 2026,
            Month = 1
        });

        // Month 2 (February): Validated payment of 30,000 and released direct disb of 15,000
        context.Payments.Add(new Domain.Entities.Payment
        {
            Id = "PAY-FEB-01",
            Amount = 30000m,
            PaymentStatus = "Validated",
            ValidatedAt = new DateTime(2026, 2, 10)
        });
        context.DirectDisbursements.Add(new DirectDisbursement
        {
            ReferenceNumber = "DD-FEB-01",
            Amount = 15000m,
            Status = "Released",
            ReleasedAt = new DateTime(2026, 2, 14),
            FiscalYear = 2026,
            Month = 2
        });

        await context.SaveChangesAsync();

        var gateService = new CeoApprovalGateService(context);
        var statementService = new FinancialStatementService(context, gateService);

        // Month 1
        var cfM1 = await statementService.GenerateCashFlowStatementAsync(2026, "Month", 1);
        Assert.Equal(40000m, cfM1.BeginningCash); // 30k + 10k opening
        Assert.Equal(50000m, cfM1.TotalOperatingInflows);
        Assert.Equal(20000m, cfM1.TotalOperatingOutflows);
        Assert.Equal(70000m, cfM1.EndingNetCash); // 40k + 50k - 20k = 70k

        // Month 2: Beginning cash MUST chain from Month 1 Ending Cash (70,000)
        var cfM2 = await statementService.GenerateCashFlowStatementAsync(2026, "Month", 2);
        Assert.Equal(70000m, cfM2.BeginningCash);
        Assert.Equal(30000m, cfM2.TotalOperatingInflows);
        Assert.Equal(15000m, cfM2.TotalOperatingOutflows);
        Assert.Equal(85000m, cfM2.EndingNetCash); // 70k + 30k - 15k = 85k
    }
}
