using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class Phase15DepartmentsAndBudgetControlTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed canonical employees
        context.Employees.AddRange(
            new Employee
            {
                Id = "EMP-001",
                Name = "Crystalyn Joyce C. Fajardo",
                Role = "Finance Manager",
                Position = "Finance Manager",
                Email = "crystalyn@foms.local",
                Username = "crystalyn",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new Employee
            {
                Id = "EMP-002",
                Name = "Misty",
                Role = "Head Accountant",
                Position = "Head Accountant",
                Email = "misty@foms.local",
                Username = "misty",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new Employee
            {
                Id = "EMP-003",
                Name = "Maria Mariel Jane Anonuevo",
                Role = "Accountant",
                Position = "Accountant",
                Email = "maria@foms.local",
                Username = "maria",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new Employee
            {
                Id = "EMP-004",
                Name = "Joana Marie Ogaya",
                Role = "Assistant of Finance Manager",
                Position = "Assistant of Finance Manager",
                Email = "joana@foms.local",
                Username = "joana",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            }
        );

        // Seed canonical departments
        context.Departments.AddRange(
            new Department { DepartmentId = 1, DepartmentCode = "DEP-FIN", DepartmentName = "Finance & Accounting", IsActive = true, IsOverhead = false, IsLegacy = false },
            new Department { DepartmentId = 2, DepartmentCode = "DEP-CCS", DepartmentName = "Coordinator & Customer Service Team", IsActive = true, IsOverhead = false, IsLegacy = false },
            new Department { DepartmentId = 3, DepartmentCode = "DEP-DSP", DepartmentName = "Dispatch Team", IsActive = true, IsOverhead = false, IsLegacy = false },
            new Department { DepartmentId = 4, DepartmentCode = "DEP-FWD", DepartmentName = "Forwarding Team", IsActive = true, IsOverhead = false, IsLegacy = false },
            new Department { DepartmentId = 5, DepartmentCode = "DEP-OVH", DepartmentName = "Company-wide (Overhead)", IsActive = true, IsOverhead = true, IsLegacy = false },
            new Department { DepartmentId = 6, DepartmentCode = "DEP-OPS", DepartmentName = "Operations", IsActive = true, IsOverhead = false, IsLegacy = true }
        );

        // Seed Limit A threshold
        context.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            ThresholdId = 1,
            TransactionType = "CashOutflow",
            RoleName = "Assistant of Finance Manager",
            MaximumAmount = 10000.00m,
            Currency = "PHP",
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        });

        context.SaveChanges();
        return context;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Department List: Selectable Departments & Non-FM Overhead Restriction
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetSelectableDepartments_NonFmSeesFourDepartments_FmSeesFiveIncludingOverhead()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);
        var deptService = new DepartmentService(context, budgetService);

        // Non-FM (e.g. Assistant FM or Accountant)
        var nonFmDepts = await deptService.GetSelectableDepartmentsAsync("Assistant of Finance Manager");
        Assert.Equal(4, nonFmDepts.Count);
        Assert.DoesNotContain(nonFmDepts, d => d.DepartmentCode == FomsDepartments.Overhead);
        Assert.DoesNotContain(nonFmDepts, d => d.DepartmentCode == FomsDepartments.LegacyOperations);
        Assert.Contains(nonFmDepts, d => d.DepartmentCode == FomsDepartments.Finance);
        Assert.Contains(nonFmDepts, d => d.DepartmentCode == FomsDepartments.Coordinator);
        Assert.Contains(nonFmDepts, d => d.DepartmentCode == FomsDepartments.Dispatch);
        Assert.Contains(nonFmDepts, d => d.DepartmentCode == FomsDepartments.Forwarding);

        // Finance Manager sees all 5 canonical departments including Overhead
        var fmDepts = await deptService.GetSelectableDepartmentsAsync("Finance Manager");
        Assert.Equal(5, fmDepts.Count);
        Assert.Contains(fmDepts, d => d.DepartmentCode == FomsDepartments.Overhead && d.IsOverhead);
        Assert.DoesNotContain(fmDepts, d => d.DepartmentCode == FomsDepartments.LegacyOperations);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Department Validation & Normalization Rules
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("FINANCE", "DEP-FIN")]
    [InlineData("DEP-FIN", "DEP-FIN")]
    [InlineData("Finance & Accounting", "DEP-FIN")]
    [InlineData("COORDINATOR", "DEP-CCS")]
    [InlineData("DEP-CCS", "DEP-CCS")]
    [InlineData("Coordinator & Customer Service Team", "DEP-CCS")]
    [InlineData("DISPATCH", "DEP-DSP")]
    [InlineData("DEP-DSP", "DEP-DSP")]
    [InlineData("Dispatch Team", "DEP-DSP")]
    [InlineData("FORWARDING", "DEP-FWD")]
    [InlineData("DEP-FWD", "DEP-FWD")]
    [InlineData("Forwarding Team", "DEP-FWD")]
    public void Normalize_ValidAliases_NormalizeToCanonicalCode(string input, string expected)
    {
        var result = FomsDepartments.Normalize(input, "Accountant", isNewRequest: true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_EmptyOrNullDepartment_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FomsDepartments.Normalize(null, "Accountant"));
        Assert.Throws<ArgumentException>(() => FomsDepartments.Normalize("", "Accountant"));
        Assert.Throws<ArgumentException>(() => FomsDepartments.Normalize("   ", "Accountant"));
    }

    [Fact]
    public void Normalize_UnknownDepartment_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => FomsDepartments.Normalize("UNKNOWN_DEPT", "Finance Manager"));
        Assert.Contains("Unknown department", ex.Message);
        Assert.Contains("DEP-FIN", ex.Message);
    }

    [Fact]
    public void Normalize_LegacyOperations_RejectedForNewRequest()
    {
        var ex = Assert.Throws<ArgumentException>(() => FomsDepartments.Normalize("DEP-OPS", "Accountant", isNewRequest: true));
        Assert.Contains("legacy department", ex.Message);
        Assert.Contains("cannot be selected for new requests", ex.Message);

        // When reading legacy data (isNewRequest = false), returns legacy code without error
        var legacyResult = FomsDepartments.Normalize("DEP-OPS", "Accountant", isNewRequest: false);
        Assert.Equal("DEP-OPS", legacyResult);
    }

    [Fact]
    public void Normalize_OverheadDepartment_RestrictedToFinanceManager()
    {
        // Non-FM throws UnauthorizedAccessException
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            FomsDepartments.Normalize("DEP-OVH", "Accountant", isNewRequest: true));
        Assert.Contains("only be selected by the Finance Manager", ex.Message);

        // Finance Manager succeeds
        var fmResult = FomsDepartments.Normalize("DEP-OVH", "Finance Manager", isNewRequest: true);
        Assert.Equal("DEP-OVH", fmResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Option A Suggestion Chip (Requester's own last department)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DepartmentSuggestion_ReturnsRequestersOwnLastDepartment_OptionA()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);
        var deptService = new DepartmentService(context, budgetService);

        // 1. User with no previous requests has no suggestion
        var noSuggestion = await deptService.GetDepartmentSuggestionAsync("EMP-003", "Accountant");
        Assert.False(noSuggestion.HasSuggestion);
        Assert.Null(noSuggestion.DepartmentCode);

        // 2. Add previous Reimbursement for EMP-003 with DEP-DSP
        context.Reimbursements.Add(new Reimbursement
        {
            ReimbursementId = 501,
            ReferenceNumber = "REIM-2026-0501",
            EmployeeId = "EMP-003",
            EmployeeName = "Maria",
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            Amount = 3000m,
            Purpose = "Field visit fuel",
            Status = "Validated",
            SubmittedAt = DateTime.UtcNow.AddDays(-2)
        });
        await context.SaveChangesAsync();

        // Suggestion now returns DEP-DSP
        var suggestion = await deptService.GetDepartmentSuggestionAsync("EMP-003", "Accountant");
        Assert.True(suggestion.HasSuggestion);
        Assert.Equal("DEP-DSP", suggestion.DepartmentCode);
        Assert.Equal("Dispatch Team", suggestion.DepartmentName);

        // 3. If previous request used Overhead (DEP-OVH), non-FM is NEVER suggested Overhead
        context.Reimbursements.Add(new Reimbursement
        {
            ReimbursementId = 502,
            ReferenceNumber = "REIM-2026-0502",
            EmployeeId = "EMP-003",
            EmployeeName = "Maria",
            DepartmentCode = "DEP-OVH",
            Category = "Office Supplies",
            Amount = 1500m,
            Purpose = "Corporate stationery",
            Status = "Validated",
            SubmittedAt = DateTime.UtcNow // Most recent
        });
        await context.SaveChangesAsync();

        var nonFmSuggestion = await deptService.GetDepartmentSuggestionAsync("EMP-003", "Accountant");
        Assert.False(nonFmSuggestion.HasSuggestion); // Overhead filtered out for non-FM
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. FM Department Change Pre-Approval (R5.11 Item 5)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ChangeDepartment_FinanceManagerCanChangePreApproval_WithRequiredReason()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);
        var deptService = new DepartmentService(context, budgetService);

        // Seed annual budget and lines for DEP-DSP and DEP-FWD
        var annualBudget = new AnnualBudget
        {
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            Status = "Approved",
            TotalBudget = 200000m,
            PreparedBy = "EMP-001",
            CreatedAt = DateTime.UtcNow
        };
        context.AnnualBudgets.Add(annualBudget);

        var dspLine = new MonthlyBudgetLine
        {
            BudgetLineId = 1,
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            DepartmentCode = "DEP-DSP",
            DepartmentName = "Dispatch Team",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 50000m,
            CommittedAmount = 12000m,
            ActualAmount = 0m
        };
        var fwdLine = new MonthlyBudgetLine
        {
            BudgetLineId = 2,
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FWD",
            DepartmentName = "Forwarding Team",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 50000m,
            CommittedAmount = 0m,
            ActualAmount = 0m
        };
        context.MonthlyBudgetLines.AddRange(dspLine, fwdLine);

        // Seed unapproved CashAdvance in DEP-DSP
        var advance = new CashAdvance
        {
            AdvanceId = "CA-2026-DSP-01",
            CourierName = "Juan Dela Cruz",
            TripReference = "TRIP-PROV-01",
            AmountGiven = 12000m,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            FiscalYear = 2026,
            Month = 1,
            Status = "Pending Finance Review",
            IsDepartmentLocked = false,
            CreatedAt = DateTime.UtcNow
        };
        context.CashAdvances.Add(advance);
        await context.SaveChangesAsync();

        // 1. Non-FM attempt to change department throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            deptService.ChangeRequestDepartmentAsync(new ChangeDepartmentRequest
            {
                RequestType = "CashAdvance",
                RequestId = "CA-2026-DSP-01",
                NewDepartmentCode = "DEP-FWD",
                Reason = "Re-allocating to province forwarding trip"
            }, "EMP-004", "Assistant of Finance Manager"));

        // 2. FM without reason throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            deptService.ChangeRequestDepartmentAsync(new ChangeDepartmentRequest
            {
                RequestType = "CashAdvance",
                RequestId = "CA-2026-DSP-01",
                NewDepartmentCode = "DEP-FWD",
                Reason = "" // Missing reason
            }, "EMP-001", "Finance Manager"));

        // 3. FM with reason changes department successfully
        var log = await deptService.ChangeRequestDepartmentAsync(new ChangeDepartmentRequest
        {
            RequestType = "CashAdvance",
            RequestId = "CA-2026-DSP-01",
            NewDepartmentCode = "DEP-FWD",
            Reason = "Trip routes modified by logistics dispatch coordinator to regional forwarding"
        }, "EMP-001", "Finance Manager");

        Assert.Equal("DEP-DSP", log.OldDepartmentCode);
        Assert.Equal("DEP-FWD", log.NewDepartmentCode);
        Assert.Equal("EMP-001", log.ChangedBy);

        // Verify advance updated
        var updatedAdvance = await context.CashAdvances.FirstAsync(a => a.AdvanceId == "CA-2026-DSP-01");
        Assert.Equal("DEP-FWD", updatedAdvance.DepartmentCode);
        Assert.Equal("Trip routes modified by logistics dispatch coordinator to regional forwarding", updatedAdvance.DepartmentChangeReason);

        // Verify committed amount moved from DEP-DSP to DEP-FWD
        var updatedDspLine = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        var updatedFwdLine = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 2);
        Assert.Equal(0m, updatedDspLine.CommittedAmount);
        Assert.Equal(12000m, updatedFwdLine.CommittedAmount);

        // Verify audit change logs query
        var history = await deptService.GetDepartmentChangeLogsAsync("CashAdvance", "CA-2026-DSP-01");
        Assert.Single(history);
        Assert.Equal("DEP-FWD", history[0].NewDepartmentCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Locked After Approval (Cannot change department once approved)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ChangeDepartment_AfterApproval_ThrowsInvalidOperationException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);
        var deptService = new DepartmentService(context, budgetService);

        // Seed Approved (locked) CashAdvance
        var approvedAdvance = new CashAdvance
        {
            AdvanceId = "CA-2026-APP-01",
            CourierName = "Juan Dela Cruz",
            TripReference = "TRIP-001",
            AmountGiven = 8000m,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            FiscalYear = 2026,
            Month = 1,
            Status = "Approved",
            IsDepartmentLocked = true,
            CreatedAt = DateTime.UtcNow
        };
        context.CashAdvances.Add(approvedAdvance);
        await context.SaveChangesAsync();

        // Attempt to modify department after approval throws
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deptService.ChangeRequestDepartmentAsync(new ChangeDepartmentRequest
            {
                RequestType = "CashAdvance",
                RequestId = "CA-2026-APP-01",
                NewDepartmentCode = "DEP-FIN",
                Reason = "Post-approval mistake correction attempt"
            }, "EMP-001", "Finance Manager"));

        Assert.Contains("cannot be modified after request approval", ex.Message);
        Assert.Contains("locked", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Overhead Budget Line & Allocation Governance (FIN-01, R5.11 Item 6)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OverheadBudgetLine_AllocatedExclusivelyByFinanceManager()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        // 1. Non-FM attempting to create draft budget with Overhead throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.CreateOrUpdateDraftBudgetAsync(new CreateOrUpdateDraftBudgetRequest
            {
                FiscalYear = 2026,
                Lines = new List<BudgetLineEstimateDto>
                {
                    new()
                    {
                        DepartmentCode = FomsDepartments.Overhead,
                        DepartmentName = "Company-wide (Overhead)",
                        Category = "Supplies",
                        Month = 1,
                        BudgetAmount = 20000m
                    }
                }
            }, "EMP-003", "Accountant"));

        // 2. Finance Manager can draft budget with Overhead line
        var draft = await budgetService.CreateOrUpdateDraftBudgetAsync(new CreateOrUpdateDraftBudgetRequest
        {
            FiscalYear = 2026,
            Lines = new List<BudgetLineEstimateDto>
            {
                new()
                {
                    DepartmentCode = FomsDepartments.Overhead,
                    DepartmentName = "Company-wide (Overhead)",
                    Category = "Supplies",
                    Month = 1,
                    BudgetAmount = 20000m
                },
                new()
                {
                    DepartmentCode = FomsDepartments.Dispatch,
                    DepartmentName = "Dispatch Team",
                    Category = "Fuel",
                    Month = 1,
                    BudgetAmount = 50000m
                }
            }
        }, "EMP-001", "Finance Manager");

        Assert.Equal(70000m, draft.TotalBudget);
        Assert.Contains(draft.Lines, l => l.DepartmentCode == FomsDepartments.Overhead);

        // 3. Submit and Approve budget (with Phase 14 CEO tick)
        await budgetService.SubmitBudgetAsync(draft.AnnualBudgetId, "EMP-001", "Finance Manager");
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");
        var approvedBudget = await budgetService.ApproveAnnualBudgetAsync(draft.AnnualBudgetId, "EMP-001", "Finance Manager");

        // 4. Non-FM cannot allocate budget
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.AllocateBudgetAsync(new AllocateBudgetRequest
            {
                AnnualBudgetId = approvedBudget.AnnualBudgetId,
                Allocations = new List<AllocateLineDto>()
            }, "EMP-003", "Accountant"));

        // 5. Finance Manager allocates Overhead and department lines
        var allocated = await budgetService.AllocateBudgetAsync(new AllocateBudgetRequest
        {
            AnnualBudgetId = approvedBudget.AnnualBudgetId,
            Allocations = new List<AllocateLineDto>
            {
                new()
                {
                    DepartmentCode = FomsDepartments.Overhead,
                    Category = "Supplies",
                    Month = 1,
                    AllocatedAmount = 25000m
                },
                new()
                {
                    DepartmentCode = FomsDepartments.Dispatch,
                    Category = "Fuel",
                    Month = 1,
                    AllocatedAmount = 55000m
                }
            }
        }, "EMP-001", "Finance Manager");

        Assert.Equal(80000m, allocated.TotalBudget);
        var ovhLine = allocated.Lines.First(l => l.DepartmentCode == FomsDepartments.Overhead);
        Assert.Equal(25000m, ovhLine.BudgetAmount);
        Assert.Equal(25000m, ovhLine.Available);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Budget Arithmetic & Available Calculations (Available = Budget - Committed - Actual)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void BudgetArithmetic_AvailableAndRemainingCalculatedCorrectly()
    {
        var line = new MonthlyBudgetLine
        {
            BudgetAmount = 100000m,
            CommittedAmount = 25000m,
            ActualAmount = 40000m
        };

        // Available = Budget - Committed - Actual
        Assert.Equal(35000m, line.Available);

        // Remaining = Budget - Actual (Committed is not yet actual spend)
        Assert.Equal(60000m, line.Remaining);

        // UtilizationPercent = (Actual / Budget) * 100
        Assert.Equal(40.00m, line.UtilizationPercent);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Soft Block & Budget Overrides (Boundary & Threshold Evaluation)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CheckBudgetAvailability_SoftBlockTriggered_WhenExceedingAvailable()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        // Line with 50,000 budget, 10,000 committed, 20,000 actual -> Available = 20,000
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 10,
            FiscalYear = 2026,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 50000m,
            CommittedAmount = 10000m,
            ActualAmount = 20000m
        });
        await context.SaveChangesAsync();

        // 1. Within available (15,000 <= 20,000) -> Available = true, no soft block
        var res1 = await budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            Month = 1,
            RequestedAmount = 15000m
        });
        Assert.True(res1.IsAvailable);
        Assert.False(res1.IsOverBudget);
        Assert.Equal(0m, res1.OverBudgetAmount);

        // 2. Exceeds available by 5,000 (Requested 25,000, Available 20,000, Over = 5,000)
        // Over <= Threshold (10,000 Limit A fail-safe) -> FM override
        var res2 = await budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            Month = 1,
            RequestedAmount = 25000m
        });
        Assert.False(res2.IsAvailable);
        Assert.True(res2.IsOverBudget);
        Assert.Equal(5000m, res2.OverBudgetAmount);
        Assert.False(res2.RequiresCeoApproval); // 5,000 <= 10,000 threshold

        // 3. Exceeds available by 15,000 (Requested 35,000, Available 20,000, Over = 15,000)
        // Over > Threshold (10,000) -> CEO approval record required
        var res3 = await budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
        {
            FiscalYear = 2026,
            DepartmentCode = "DEP-DSP",
            Category = "Fuel",
            Month = 1,
            RequestedAmount = 35000m
        });
        Assert.False(res3.IsAvailable);
        Assert.True(res3.IsOverBudget);
        Assert.Equal(15000m, res3.OverBudgetAmount);
        Assert.True(res3.RequiresCeoApproval); // 15,000 > 10,000 threshold
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Budget Transfer Preserves Company Total
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BudgetTransfer_PreservesCompanyTotalBudget()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        context.AnnualBudgets.Add(new AnnualBudget
        {
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            Status = "Approved",
            TotalBudget = 100000m,
            PreparedBy = "EMP-001"
        });

        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine
            {
                BudgetLineId = 1,
                AnnualBudgetId = 1,
                FiscalYear = 2026,
                DepartmentCode = "DEP-DSP",
                Category = "Fuel",
                Month = 1,
                BudgetAmount = 60000m
            },
            new MonthlyBudgetLine
            {
                BudgetLineId = 2,
                AnnualBudgetId = 1,
                FiscalYear = 2026,
                DepartmentCode = "DEP-FWD",
                Category = "Fuel",
                Month = 1,
                BudgetAmount = 40000m
            }
        );
        await context.SaveChangesAsync();

        // FM requests transfer of 15,000 from DSP to FWD
        var transfer = await budgetService.RequestBudgetTransferAsync(new RequestBudgetTransferRequest
        {
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-DSP",
            FromCategory = "Fuel",
            FromMonth = 1,
            ToDepartmentCode = "DEP-FWD",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 15000m,
            Reason = "Forwarding fleet expansion re-allocation"
        }, "EMP-001", "Finance Manager");

        // Record CEO tick and Head Accountant maker-checker confirmation
        var record = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = transfer.TransferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        await gateService.ConfirmRecordAsync(record.RecordId, "Recording", "EMP-002", "Head Accountant");

        // Approve transfer
        await budgetService.ApproveBudgetTransferAsync(transfer.TransferId, "EMP-001", "Finance Manager");

        // Verify line balances: DSP 60k - 15k = 45k, FWD 40k + 15k = 55k
        var dsp = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        var fwd = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 2);
        Assert.Equal(45000m, dsp.BudgetAmount);
        Assert.Equal(55000m, fwd.BudgetAmount);

        // Verify company total remains strictly 100,000m
        var total = await context.MonthlyBudgetLines.SumAsync(l => l.BudgetAmount);
        Assert.Equal(100000m, total);
    }
}
