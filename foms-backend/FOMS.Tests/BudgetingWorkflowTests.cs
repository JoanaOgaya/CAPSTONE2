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

public class BudgetingWorkflowTests
{
    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed shared DMS departments
        context.Departments.AddRange(
            new Department { DepartmentCode = "DEP-FIN", DepartmentName = "Finance", IsActive = true },
            new Department { DepartmentCode = "DEP-OPS", DepartmentName = "Operations", IsActive = true },
            new Department { DepartmentCode = "DEP-FLT", DepartmentName = "Fleet", IsActive = true },
            new Department { DepartmentCode = "DEP-IT", DepartmentName = "IT & Tech", IsActive = true }
        );

        // Seed CSH-09 threshold (₱10,000)
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

        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task GetSharedDepartments_ReturnsSharedListMatchingDms()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        var depts = await service.GetSharedDepartmentsAsync();

        Assert.NotNull(depts);
        Assert.Equal(4, depts.Count);
        Assert.Contains(depts, d => d.DepartmentCode == "DEP-FIN" && d.DepartmentName == "Finance");
        Assert.Contains(depts, d => d.DepartmentCode == "DEP-OPS" && d.DepartmentName == "Operations");
    }

    [Fact]
    public async Task HeadAccountant_IsExcludedFromBudgetChain_ThrowsUnauthorized()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        var req = new CreateOrUpdateDraftBudgetRequest
        {
            FiscalYear = 2026,
            Lines = new List<BudgetLineEstimateDto>
            {
                new BudgetLineEstimateDto
                {
                    DepartmentCode = "DEP-FIN",
                    Category = "Software",
                    Month = 1,
                    BudgetAmount = 50000m
                }
            }
        };

        // Head Accountant must be blocked from creating, submitting, approving, allocating, etc.
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateOrUpdateDraftBudgetAsync(req, "EMP-002", "Head Accountant"));

        Assert.Contains("Head Accountant is not in the budget chain", ex.Message);
    }

    [Fact]
    public async Task AnnualBudgetLifecycle_Draft_Submitted_Approved_Rejected_Workflow()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        // 1. Department estimate per category per month with historical actuals shown
        var draftReq = new CreateOrUpdateDraftBudgetRequest
        {
            FiscalYear = 2026,
            WarningAlertThresholdPercent = 80m,
            Lines = new List<BudgetLineEstimateDto>
            {
                new BudgetLineEstimateDto
                {
                    DepartmentCode = "DEP-OPS",
                    DepartmentName = "Operations",
                    Category = "Fuel",
                    Month = 1,
                    BudgetAmount = 100000m,
                    HistoricalActual = 85000m
                },
                new BudgetLineEstimateDto
                {
                    DepartmentCode = "DEP-OPS",
                    DepartmentName = "Operations",
                    Category = "Maintenance",
                    Month = 1,
                    BudgetAmount = 50000m,
                    HistoricalActual = 42000m
                },
                new BudgetLineEstimateDto
                {
                    DepartmentCode = "DEP-FIN",
                    DepartmentName = "Finance",
                    Category = "Software",
                    Month = 1,
                    BudgetAmount = 30000m,
                    HistoricalActual = 28000m
                }
            }
        };

        var draft = await service.CreateOrUpdateDraftBudgetAsync(draftReq, "EMP-001", "Finance Manager");
        Assert.Equal("Draft", draft.Status);
        Assert.Equal(180000m, draft.TotalBudget); // Rolling annual total
        Assert.Equal(3, draft.Lines.Count);
        Assert.Equal(85000m, draft.Lines.First(l => l.Category == "Fuel").HistoricalActual);

        // 2. Finance Manager reviews and submits annual budget
        var submitted = await service.SubmitBudgetAsync(draft.AnnualBudgetId, "EMP-001", "Finance Manager");
        Assert.Equal("Submitted", submitted.Status);
        Assert.Equal("EMP-001", submitted.ReviewedBy);
        Assert.NotNull(submitted.SubmittedAt);

        // 3. Non-approved attempt fails without recorded CEO approval
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAnnualBudgetAsync(submitted.AnnualBudgetId, "EMP-001", "Finance Manager"));

        // 4. FM rejects with required reason -> returns to Rejected/Draft
        var rejected = await service.RejectAnnualBudgetAsync(submitted.AnnualBudgetId, "Please optimize fuel estimates by 10%", "EMP-001", "Finance Manager");
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Please optimize fuel estimates by 10%", rejected.RejectionReason);

        // Re-submit
        await service.SubmitBudgetAsync(rejected.AnnualBudgetId, "EMP-001", "Finance Manager");

        // 5. In-person CEO/President approval record entered by FM, then approved
        var gateService = new CeoApprovalGateService(context);
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = draft.FiscalYear.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        var approved = await service.ApproveAnnualBudgetAsync(draft.AnnualBudgetId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("EMP-001", approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAt);

        // 6. Finance Manager allocates approved budget by department and category
        var allocReq = new AllocateBudgetRequest
        {
            AnnualBudgetId = approved.AnnualBudgetId,
            Allocations = new List<AllocateLineDto>
            {
                new AllocateLineDto { DepartmentCode = "DEP-OPS", Category = "Fuel", Month = 1, AllocatedAmount = 90000m }
            }
        };

        var allocated = await service.AllocateBudgetAsync(allocReq, "EMP-001", "Finance Manager");
        var fuelLine = allocated.Lines.First(l => l.Category == "Fuel");
        Assert.Equal(90000m, fuelLine.BudgetAmount);
        Assert.Equal(170000m, allocated.TotalBudget);
    }

    [Fact]
    public async Task BudgetFormulas_Available_And_Remaining_DisplayCorrectly()
    {
        using var context = CreateContext();

        var budget = new AnnualBudget
        {
            FiscalYear = 2026,
            TotalBudget = 100000m,
            Status = "Approved",
            PreparedBy = "EMP-001"
        };
        context.AnnualBudgets.Add(budget);
        await context.SaveChangesAsync();

        var line = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            DepartmentName = "Operations",
            Category = "Tires",
            Month = 1,
            BudgetAmount = 100000m,
            CommittedAmount = 20000m,
            ActualAmount = 50000m
        };
        context.MonthlyBudgetLines.Add(line);
        await context.SaveChangesAsync();

        // Formula check:
        // Available = Budget - Committed - Actual = 100,000 - 20,000 - 50,000 = 30,000
        Assert.Equal(30000m, line.Available);

        // Remaining = Budget - Actual = 100,000 - 50,000 = 50,000 (No automatic carryover, shows Remaining!)
        Assert.Equal(50000m, line.Remaining);

        // Utilization = (50,000 / 100,000) * 100 = 50%
        Assert.Equal(50.00m, line.UtilizationPercent);
    }

    [Fact]
    public async Task SoftBlock_OverBudget_RequiresOverrideAndThresholdApproval()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        var budget = new AnnualBudget { FiscalYear = 2026, TotalBudget = 50000m, Status = "Approved", PreparedBy = "EMP-001" };
        context.AnnualBudgets.Add(budget);
        await context.SaveChangesAsync();

        // Line with Budget = 20,000, Actual = 15,000, Available = 5,000
        var line = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Repairs",
            Month = 2,
            BudgetAmount = 20000m,
            ActualAmount = 15000m,
            CommittedAmount = 0m
        };
        context.MonthlyBudgetLines.Add(line);
        await context.SaveChangesAsync();

        // 1. Check spending 12,000 against Available (5,000) -> Soft block!
        // Over-budget amount = 12,000 - 5,000 = 7,000 (<= 10,000 CSH-09 threshold -> FM can approve)
        var check1 = await service.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Repairs",
            Month = 2,
            RequestedAmount = 12000m
        });

        Assert.False(check1.IsAvailable);
        Assert.True(check1.IsOverBudget);
        Assert.Equal(7000m, check1.OverBudgetAmount);
        Assert.False(check1.RequiresCeoApproval); // 7,000 <= 10,000 threshold

        // 2. Request override for 12,000
        var overrideReq1 = new RequestBudgetOverrideRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Repairs",
            Month = 2,
            RequestedAmount = 12000m,
            Reason = "Emergency radiator fix"
        };
        var createdOverride1 = await service.RequestBudgetOverrideAsync(overrideReq1, "EMP-004", "Assistant of Finance Manager");
        Assert.Equal("Pending", createdOverride1.Status);
        Assert.False(createdOverride1.RequiresCeoApproval);

        // FM can approve override <= threshold
        var approvedOverride1 = await service.ApproveBudgetOverrideAsync(createdOverride1.OverrideId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedOverride1.Status);

        // Verify override count and commitment incremented
        var refreshedLine = await context.MonthlyBudgetLines.FindAsync(line.BudgetLineId);
        Assert.Equal(1, refreshedLine!.OverrideCount);
        Assert.Equal(12000m, refreshedLine.CommittedAmount);

        // 3. Large override request > threshold (e.g. 25,000 on remaining 0 available -> excess 25,000 > 10,000)
        var overrideReq2 = new RequestBudgetOverrideRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Repairs",
            Month = 2,
            RequestedAmount = 25000m,
            Reason = "Engine overhaul"
        };
        var createdOverride2 = await service.RequestBudgetOverrideAsync(overrideReq2, "EMP-004", "Assistant of Finance Manager");
        Assert.True(createdOverride2.RequiresCeoApproval);

        // FM attempting to approve > threshold without CEO record must fail
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveBudgetOverrideAsync(createdOverride2.OverrideId, "EMP-001", "Finance Manager"));
        Assert.Contains("requires CEO/President approval", ex.Message);

        // FM records in-person CEO approval record, then approves override (IAM-07 to IAM-10)
        var gateService = new CeoApprovalGateService(context);
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetOverride,
            ItemId = createdOverride2.OverrideId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        var approvedOverride2 = await service.ApproveBudgetOverrideAsync(createdOverride2.OverrideId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedOverride2.Status);
        Assert.Equal("EMP-001", approvedOverride2.ApprovedBy);
    }

    [Fact]
    public async Task BudgetTransfersAndCarryOvers_KeepsCompanyTotalUnchanged_AndRequiresCeoApproval()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        var budget = new AnnualBudget { FiscalYear = 2026, TotalBudget = 100000m, Status = "Approved", PreparedBy = "EMP-001" };
        context.AnnualBudgets.Add(budget);
        await context.SaveChangesAsync();

        var lineOps = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 60000m,
            ActualAmount = 20000m // Available = 40,000
        };

        var lineFlt = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FLT",
            Category = "Maintenance",
            Month = 1,
            BudgetAmount = 40000m,
            ActualAmount = 35000m // Available = 5,000
        };

        context.MonthlyBudgetLines.AddRange(lineOps, lineFlt);
        await context.SaveChangesAsync();

        decimal initialCompanyTotal = lineOps.BudgetAmount + lineFlt.BudgetAmount; // 100,000

        // 1. FM requests transfer of 15,000 from OPS to FLT
        var transferReq = new RequestBudgetTransferRequest
        {
            FiscalYear = 2026,
            TransferType = "Transfer",
            FromDepartmentCode = "DEP-OPS",
            FromCategory = "Fuel",
            FromMonth = 1,
            ToDepartmentCode = "DEP-FLT",
            ToCategory = "Maintenance",
            ToMonth = 1,
            Amount = 15000m,
            Reason = "Fleet unexpected tire replacements"
        };

        var requestedTransfer = await service.RequestBudgetTransferAsync(transferReq, "EMP-001", "Finance Manager");
        Assert.Equal("Pending", requestedTransfer.Status);

        // 2. FM cannot approve transfer without in-person CEO approval record
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveBudgetTransferAsync(requestedTransfer.TransferId, "EMP-001", "Finance Manager"));

        // 3. FM records CEO approval record and HA confirms recording (IAM-07 to IAM-10, R5.11 item 1)
        var gateService = new CeoApprovalGateService(context);
        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = requestedTransfer.TransferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant");

        var approvedTransfer = await service.ApproveBudgetTransferAsync(requestedTransfer.TransferId, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approvedTransfer.Status);

        // 4. Verify balances:
        // OPS: 60,000 - 15,000 = 45,000
        // FLT: 40,000 + 15,000 = 55,000
        var refreshedOps = await context.MonthlyBudgetLines.FindAsync(lineOps.BudgetLineId);
        var refreshedFlt = await context.MonthlyBudgetLines.FindAsync(lineFlt.BudgetLineId);

        Assert.Equal(45000m, refreshedOps!.BudgetAmount);
        Assert.Equal(55000m, refreshedFlt!.BudgetAmount);

        // Company total budget MUST REMAIN EXACTLY UNCHANGED (sum delta = 0)
        decimal newCompanyTotal = refreshedOps.BudgetAmount + refreshedFlt.BudgetAmount;
        Assert.Equal(initialCompanyTotal, newCompanyTotal);

        // Audit log created
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "Budget Transfer Approved");
        Assert.NotNull(audit);
        Assert.Contains("Company total budget unchanged", audit.Details);
    }

    [Fact]
    public async Task BudgetAlerts_WarningAtConfiguredPercent_AndCriticalAt100Percent()
    {
        using var context = CreateContext();
        var service = new BudgetService(context);

        var budget = new AnnualBudget
        {
            FiscalYear = 2026,
            TotalBudget = 300000m,
            Status = "Approved",
            WarningAlertThresholdPercent = 80.00m
        };
        context.AnnualBudgets.Add(budget);
        await context.SaveChangesAsync();

        // Line 1: 50% utilization -> Normal
        var lineNormal = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Cleaning",
            Month = 3,
            BudgetAmount = 100000m,
            ActualAmount = 50000m
        };

        // Line 2: 85% utilization (>= 80% configured) -> Warning
        var lineWarning = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-OPS",
            Category = "Fuel",
            Month = 3,
            BudgetAmount = 100000m,
            ActualAmount = 85000m
        };

        // Line 3: 100% utilization (>= 100%) -> Exhausted
        var lineExhausted = new MonthlyBudgetLine
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FLT",
            Category = "Repairs",
            Month = 3,
            BudgetAmount = 100000m,
            ActualAmount = 100000m
        };

        context.MonthlyBudgetLines.AddRange(lineNormal, lineWarning, lineExhausted);
        await context.SaveChangesAsync();

        var report = await service.GetWeeklyBudgetReportAsync(2026, 3);

        Assert.NotNull(report);
        Assert.Equal(300000m, report.TotalMonthlyBudget);
        Assert.Equal(235000m, report.TotalActualSpend);

        var opsDept = report.Departments.First(d => d.DepartmentCode == "DEP-OPS");
        var normalLine = opsDept.CategoryLines.First(l => l.Category == "Cleaning");
        var warningLine = opsDept.CategoryLines.First(l => l.Category == "Fuel");

        Assert.Equal("Normal", normalLine.AlertStatus);
        Assert.Equal("Warning", warningLine.AlertStatus);

        var fltDept = report.Departments.First(d => d.DepartmentCode == "DEP-FLT");
        var exhaustedLine = fltDept.CategoryLines.First(l => l.Category == "Repairs");
        Assert.Equal("Exhausted", exhaustedLine.AlertStatus);
    }
}
