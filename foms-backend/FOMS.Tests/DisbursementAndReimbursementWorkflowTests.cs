using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class DisbursementAndReimbursementWorkflowTests
{
    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed CSH-09 threshold limit of ₱10,000
        context.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            TransactionType = "CashOutflow",
            RoleName = "Assistant of Finance Manager",
            MaximumAmount = 10000.00m,
            IsActive = true
        });

        // Seed budget line for DEP-OPS / Fuel: Budget = 100,000, Actual = 20,000, Committed = 0, Available = 80,000
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            DepartmentName = "Operations",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 100000m,
            ActualAmount = 20000m,
            CommittedAmount = 0m
        });

        // Seed budget line for DEP-OPS / Repairs and Maintenance: Budget = 50,000, Actual = 10,000, Committed = 0, Available = 40,000
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            DepartmentName = "Operations",
            Category = "Repairs and Maintenance",
            Month = 1,
            BudgetAmount = 50000m,
            ActualAmount = 10000m,
            CommittedAmount = 0m
        });

        // Seed budget line for DEP-OPS / Food allowance: Budget = 30,000, Actual = 5,000, Committed = 0, Available = 25,000
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            DepartmentName = "Operations",
            Category = "Food allowance",
            Month = 1,
            BudgetAmount = 30000m,
            ActualAmount = 5000m,
            CommittedAmount = 0m
        });

        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task AllowedCategories_Validation_SucceedsForStandardAndBranchVisitItems()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        // Standard category: Fuel
        var caReq1 = new RequestCashAdvanceDto
        {
            CourierName = "Juan Dela Cruz",
            TripReference = "TRIP-001",
            Amount = 5000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS",
            LocationType = "NCR"
        };
        var res1 = await service.RequestCashAdvanceAsync(caReq1, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", res1.Status);

        // Supervisor branch visit category: Lodging
        var caReq2 = new RequestCashAdvanceDto
        {
            CourierName = "Supervisor Maria",
            TripReference = "TRIP-VISIT-002",
            Amount = 6000m,
            Category = "Lodging",
            DepartmentCode = "DEP-OPS",
            LocationType = "Out-of-Area",
            ReturnDate = DateTime.UtcNow.AddDays(4)
        };
        var res2 = await service.RequestCashAdvanceAsync(caReq2, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", res2.Status);

        // Invalid category throws ArgumentException
        var invalidReq = new RequestCashAdvanceDto
        {
            CourierName = "Juan Dela Cruz",
            TripReference = "TRIP-INV",
            Amount = 1000m,
            Category = "Luxury Goods",
            DepartmentCode = "DEP-OPS"
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestCashAdvanceAsync(invalidReq, "EMP-004", "Assistant of Finance Manager"));
    }

    [Fact]
    public async Task CashAdvance_FullLifecycle_Order_Commitment_ReleaseThreshold_Liquidation()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        // 1. Request Cash Advance (NCR -> Due date is next day)
        var req = new RequestCashAdvanceDto
        {
            CourierName = "Pedro Santos",
            TripReference = "TRIP-NCR-100",
            Amount = 8000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS",
            LocationType = "NCR",
            FiscalYear = 2026,
            Month = 1
        };

        var ca = await service.RequestCashAdvanceAsync(req, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", ca.Status);
        Assert.False(ca.RequiresCeoApproval); // 8,000 <= 10,000 threshold
        Assert.NotNull(ca.DueDate);
        Assert.False(ca.IsLate);

        // 2. Budget Check -> Approval (Committed)
        var approvedCa = await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedCa.Status);

        // Verify budget line commitment added
        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(8000m, budgetLine.CommittedAmount);
        Assert.Equal(72000m, budgetLine.Available); // 100,000 - 8,000 - 20,000 = 72,000

        // 3. Release (Outflow created)
        var releasedCa = await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", releasedCa.Status);

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == ca.AdvanceId);
        Assert.NotNull(outflow);
        Assert.Equal(8000m, outflow.Amount);

        // 4. Submit Liquidation: 7,000 actual expenses -> 1,000 leftover cash
        var submitLiq = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Pedro Santos",
            Remarks = "Fuel receipts complete",
            LineItems = new List<LiquidationLineItemDto>
            {
                new LiquidationLineItemDto
                {
                    Category = "Fuel",
                    Description = "Shell Gas Station Diesel",
                    Amount = 7000m,
                    ReceiptAttached = true,
                    ReceiptNumber = "OR-SHELL-9876",
                    IsManualFuelReceipt = false
                }
            }
        };

        var liq = await service.SubmitLiquidationAsync(submitLiq, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Pending", liq.Status);
        Assert.Equal(7000m, liq.TotalAmount);
        Assert.Equal(1000m, liq.LeftoverCash);
        Assert.False(liq.IsLate);

        // 5. Validation by Assistant of Finance Manager
        // Non-assistant role cannot validate
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ValidateLiquidationAsync(new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-003", "Accountant"));

        // Assistant of Finance Manager validates
        var validatedLiq = await service.ValidateLiquidationAsync(
            new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-004", "Assistant of Finance Manager");

        Assert.Equal("Validated", validatedLiq.Status);

        // Cash Advance status updated to Liquidated
        var refreshedCa = await context.CashAdvances.FindAsync(ca.AdvanceId);
        Assert.Equal("Liquidated", refreshedCa!.Status);

        // Validation converts Committed to Actual and releases unused commitment:
        // Advance Committed: was 8,000 -> released to 0
        // Actual expenses: 20,000 + 7,000 = 27,000
        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(27000m, budgetLine.ActualAmount);

        // Leftover cash returned (1,000) posted as Cash Inflow
        var inflow = await context.CashInflows.FirstOrDefaultAsync(i => i.Source.Contains(liq.ReferenceNumber));
        Assert.NotNull(inflow);
        Assert.Equal(1000m, inflow.Amount);
    }

    [Fact]
    public async Task CashAdvance_AboveCsh09Threshold_RequiresCeoPresidentBeforeRelease()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        // Advance for 15,000 (> 10,000 CSH-09 threshold)
        var req = new RequestCashAdvanceDto
        {
            CourierName = "Long Haul Driver",
            TripReference = "TRIP-VISAYAS-500",
            Amount = 15000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS",
            LocationType = "Out-of-Area",
            ReturnDate = DateTime.UtcNow.AddDays(5)
        };

        var ca = await service.RequestCashAdvanceAsync(req, "EMP-004", "Assistant of Finance Manager");
        Assert.True(ca.RequiresCeoApproval);

        // Approval by FM
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        // Release attempted by FM without CEO approval must fail
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager"));

        // Release with in-person CEO/President approval record entered by FM (IAM-07 to IAM-10)
        var gateService = new CeoApprovalGateService(context);
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = ca.AdvanceId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        var released = await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", released.Status);
    }

    [Fact]
    public async Task CashAdvance_RejectedOrCancelled_ReleasesCommitment()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        var req = new RequestCashAdvanceDto
        {
            CourierName = "Test Courier",
            Amount = 5000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };

        var ca = await service.RequestCashAdvanceAsync(req, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(5000m, budgetLine.CommittedAmount);

        // Cancel releases commitment
        var cancelled = await service.CancelCashAdvanceAsync(ca.AdvanceId, "Trip cancelled by client", "EMP-001", "Finance Manager");
        Assert.Equal("Cancelled", cancelled.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Fuel");
        Assert.Equal(0m, budgetLine.CommittedAmount);
    }

    [Fact]
    public async Task DirectDisbursement_CSH12_ActualAtRelease_NoLiquidation()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        var req = new RequestDirectDisbursementDto
        {
            DepartmentCode = "DEP-OPS",
            Category = "Repairs and Maintenance",
            Amount = 12000m,
            Purpose = "Brake pad replacement by third-party auto shop",
            FiscalYear = 2026,
            Month = 1
        };

        // 1. Requested
        var disb = await service.RequestDirectDisbursementAsync(req, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Requested", disb.Status);

        // 2. Approved (Committed)
        var approved = await service.ApproveDirectDisbursementAsync(disb.DisbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Repairs and Maintenance");
        Assert.Equal(12000m, budgetLine.CommittedAmount);

        // 3. Record CEO approval for disbursement > CSH-09 threshold, then release
        var gateService = new CeoApprovalGateService(context);
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = disb.DisbursementId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        // Released: CSH-12 converts Committed -> Actual immediately without any liquidation!
        var released = await service.ReleaseDirectDisbursementAsync(disb.DisbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Released", released.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Repairs and Maintenance");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(22000m, budgetLine.ActualAmount); // 10,000 initial + 12,000

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == disb.ReferenceNumber);
        Assert.NotNull(outflow);
        Assert.Equal(12000m, outflow.Amount);
    }

    [Fact]
    public async Task Reimbursement_Submitted_Validated_Approved_Paid_Lifecycle()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        var req = new SubmitReimbursementDto
        {
            EmployeeId = "EMP-003",
            EmployeeName = "Maria Mariel Jane Anonuevo",
            DepartmentCode = "DEP-OPS",
            Category = "Food allowance",
            Amount = 3000m,
            Purpose = "Overtime meal allowance for month-end reconciliation",
            FiscalYear = 2026,
            Month = 1
        };

        // 1. Submitted
        var reim = await service.SubmitReimbursementAsync(req, "EMP-003", "Accountant");
        Assert.Equal("Submitted", reim.Status);

        // 2. Validated by Assistant of Finance Manager
        var validated = await service.ValidateReimbursementAsync(reim.ReimbursementId, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validated.Status);

        // 3. Approved by Finance Manager = COMMITTED
        var approved = await service.ApproveReimbursementAsync(reim.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);

        var budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Food allowance");
        Assert.Equal(3000m, budgetLine.CommittedAmount);

        // 4. Paid = ACTUAL
        var paid = await service.PayReimbursementAsync(reim.ReimbursementId, "EMP-001", "Finance Manager");
        Assert.Equal("Paid", paid.Status);

        budgetLine = await context.MonthlyBudgetLines.FirstAsync(l => l.Category == "Food allowance");
        Assert.Equal(0m, budgetLine.CommittedAmount);
        Assert.Equal(8000m, budgetLine.ActualAmount); // 5,000 initial + 3,000

        var outflow = await context.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == reim.ReferenceNumber);
        Assert.NotNull(outflow);
        Assert.Equal(3000m, outflow.Amount);
    }

    [Fact]
    public async Task Liquidation_Overspend_RequiresSupervisorPreApproval()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        // Create advance for 5,000
        var caReq = new RequestCashAdvanceDto
        {
            CourierName = "Driver Overspend Test",
            Amount = 5000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var ca = await service.RequestCashAdvanceAsync(caReq, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        // Submit liquidation with overspend: 6,500 total (1,500 overspend) without supervisor pre-approval
        var liqReqNoApproval = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Driver Overspend Test",
            SupervisorPreApproved = false,
            LineItems = new List<LiquidationLineItemDto>
            {
                new LiquidationLineItemDto { Category = "Fuel", Amount = 6500m, ReceiptAttached = true }
            }
        };

        var liq = await service.SubmitLiquidationAsync(liqReqNoApproval, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal(1500m, liq.ReimbursementAmount);

        // Validation without supervisor pre-approval must be blocked
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateLiquidationAsync(new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-004", "Assistant of Finance Manager"));
        Assert.Contains("cannot be validated without recorded supervisor pre-approval", ex.Message);

        // Mark for Correction
        var corrected = await service.MarkForCorrectionLiquidationAsync(liq.LiquidationId, "Attach supervisor pre-approval", "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("For Correction", corrected.Status);

        // Now supervisor pre-approved is granted
        var liqEntity = await context.Liquidations.FindAsync(liq.LiquidationId);
        liqEntity!.SupervisorPreApproved = true;
        await context.SaveChangesAsync();

        // Validation now succeeds
        var validated = await service.ValidateLiquidationAsync(new ValidateLiquidationDto { LiquidationId = liq.LiquidationId }, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Validated", validated.Status);
    }

    [Fact]
    public async Task Liquidation_RejectionReasons_Dropdown_Validation()
    {
        using var context = CreateContext();
        var service = new DisbursementAndReimbursementService(context);

        var caReq = new RequestCashAdvanceDto
        {
            CourierName = "Receipt Check Driver",
            Amount = 4000m,
            Category = "Fuel",
            DepartmentCode = "DEP-OPS"
        };
        var ca = await service.RequestCashAdvanceAsync(caReq, "EMP-004", "Assistant of Finance Manager");
        await service.ApproveCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");
        await service.ReleaseCashAdvanceAsync(ca.AdvanceId, "EMP-001", "Finance Manager");

        // Submit with manual fuel receipt
        var liqReq = new SubmitLiquidationDto
        {
            AdvanceId = ca.AdvanceId,
            SubmittedByName = "Receipt Check Driver",
            LineItems = new List<LiquidationLineItemDto>
            {
                new LiquidationLineItemDto
                {
                    Category = "Fuel",
                    Amount = 4000m,
                    ReceiptAttached = true,
                    IsManualFuelReceipt = true // Manual fuel receipt disallowed
                }
            }
        };

        var liq = await service.SubmitLiquidationAsync(liqReq, "EMP-004", "Assistant of Finance Manager");

        // Reject with reason dropdown option: "manual fuel receipt"
        var rejectDto = new RejectLiquidationDto
        {
            LiquidationId = liq.LiquidationId,
            Reason = LiquidationRejectionReasons.ManualFuelReceipt,
            Remarks = "Manual fuel receipts without pump machine printout are strictly prohibited."
        };

        var rejected = await service.RejectLiquidationAsync(rejectDto, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(LiquidationRejectionReasons.ManualFuelReceipt, rejected.RejectionReason);
    }
}
