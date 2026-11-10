using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Domain.Constants;

namespace FOMS.Application.Services;

public class BudgetService : IBudgetService
{
    private readonly IApplicationDbContext _context;
    private readonly ICeoApprovalGateService _gateService;

    public BudgetService(IApplicationDbContext context) : this(context, new CeoApprovalGateService(context))
    {
    }

    public BudgetService(IApplicationDbContext context, ICeoApprovalGateService gateService)
    {
        _context = context;
        _gateService = gateService;
    }

    private void EnsureNotHeadAccountant(string role)
    {
        if (string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Head Accountant is not in the budget chain.");
        }
    }

    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    private bool IsCeoOrPresident(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.ToLowerInvariant();
        return r.Contains("ceo") || r.Contains("president") || r.Contains("executive") || r.Contains("general manager");
    }

    private bool IsFinanceManager(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.ToLowerInvariant();
        return r.Contains("finance manager") || r.Contains("admin") || IsCeoOrPresident(role);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Shared Departments with DMS
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<List<DepartmentDto>> GetSharedDepartmentsAsync()
    {
        var depts = await _context.Departments
            .OrderBy(d => d.DepartmentCode)
            .ToListAsync();

        return depts.Select(d => new DepartmentDto
        {
            DepartmentId = d.DepartmentId,
            DepartmentCode = d.DepartmentCode,
            DepartmentName = d.DepartmentName,
            IsActive = d.IsActive
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Annual Budget Query (Category x Department x Month rolling to Annual Total)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AnnualBudgetDto?> GetAnnualBudgetAsync(int fiscalYear)
    {
        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.FiscalYear == fiscalYear);

        if (budget == null) return null;

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Draft Budget Creation / Update (Department estimate per category per month)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AnnualBudgetDto> CreateOrUpdateDraftBudgetAsync(
        CreateOrUpdateDraftBudgetRequest request, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        // Normalize department codes and enforce DEP-OVH exclusivity to Finance Manager
        if (request.Lines != null && request.Lines.Count > 0)
        {
            foreach (var lineReq in request.Lines)
            {
                lineReq.DepartmentCode = FomsDepartments.Normalize(lineReq.DepartmentCode, userRole, isNewRequest: false);
                if (string.IsNullOrWhiteSpace(lineReq.DepartmentName) && FomsDepartments.DepartmentNames.TryGetValue(lineReq.DepartmentCode, out var defaultName))
                {
                    lineReq.DepartmentName = defaultName;
                }
            }
        }

        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.FiscalYear == request.FiscalYear);

        if (budget != null && budget.Status != "Draft" && budget.Status != "Rejected")
        {
            throw new InvalidOperationException($"Cannot modify budget for FY {request.FiscalYear} in status '{budget.Status}'. Only Draft or Rejected budgets can be edited.");
        }

        if (budget == null)
        {
            budget = new AnnualBudget
            {
                FiscalYear = request.FiscalYear,
                Status = "Draft",
                PreparedBy = userId,
                WarningAlertThresholdPercent = request.WarningAlertThresholdPercent > 0 ? request.WarningAlertThresholdPercent : 80.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.AnnualBudgets.Add(budget);
            await _context.SaveChangesAsync(default);
        }
        else
        {
            budget.Status = "Draft";
            budget.RejectionReason = null;
            budget.WarningAlertThresholdPercent = request.WarningAlertThresholdPercent > 0 ? request.WarningAlertThresholdPercent : 80.00m;
            budget.UpdatedAt = DateTime.UtcNow;
        }

        // Synchronize Lines (category x department x month)
        if (request.Lines != null && request.Lines.Count > 0)
        {
            foreach (var lineReq in request.Lines)
            {
                var existingLine = budget.Lines.FirstOrDefault(l =>
                    l.DepartmentCode == lineReq.DepartmentCode &&
                    l.Category == lineReq.Category &&
                    l.Month == lineReq.Month);

                if (existingLine != null)
                {
                    existingLine.DepartmentName = lineReq.DepartmentName;
                    existingLine.BudgetAmount = lineReq.BudgetAmount;
                    existingLine.HistoricalActual = lineReq.HistoricalActual;
                    existingLine.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var newLine = new MonthlyBudgetLine
                    {
                        AnnualBudgetId = budget.AnnualBudgetId,
                        FiscalYear = budget.FiscalYear,
                        DepartmentCode = lineReq.DepartmentCode,
                        DepartmentName = lineReq.DepartmentName,
                        Category = lineReq.Category,
                        Month = lineReq.Month,
                        BudgetAmount = lineReq.BudgetAmount,
                        HistoricalActual = lineReq.HistoricalActual,
                        CommittedAmount = 0m,
                        ActualAmount = 0m,
                        OverrideCount = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.MonthlyBudgetLines.Add(newLine);
                }
            }
        }

        await _context.SaveChangesAsync(default);

        // Reload lines cleanly
        budget.Lines = await _context.MonthlyBudgetLines
            .Where(l => l.AnnualBudgetId == budget.AnnualBudgetId)
            .ToListAsync();

        // Rolling annual total
        budget.TotalBudget = budget.Lines.Sum(l => l.BudgetAmount);
        await _context.SaveChangesAsync(default);

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Submit Budget (Finance Manager reviews and submits to CEO/President)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AnnualBudgetDto> SubmitBudgetAsync(int annualBudgetId, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can review and submit the annual budget.");
        }

        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.AnnualBudgetId == annualBudgetId);

        if (budget == null)
            throw new KeyNotFoundException($"Annual budget {annualBudgetId} not found.");

        if (budget.Status != "Draft" && budget.Status != "Rejected")
        {
            throw new InvalidOperationException($"Cannot submit budget in status '{budget.Status}'.");
        }

        budget.Status = "Submitted";
        budget.ReviewedBy = userId;
        budget.ReviewedAt = DateTime.UtcNow;
        budget.SubmittedAt = DateTime.UtcNow;
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Approve Annual Budget (Strictly CEO / President)
    // ─────────────────────────────────────────────────────────────────────────
    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    public async Task<AnnualBudgetDto> ApproveAnnualBudgetAsync(int annualBudgetId, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.AnnualBudgetId == annualBudgetId);

        if (budget == null)
            throw new KeyNotFoundException($"Annual budget {annualBudgetId} not found.");

        if (!IsCeoOrPresident(userRole))
        {
            if (!IsFinanceManager(userRole))
            {
                throw new UnauthorizedAccessException("Only Finance Manager (with valid CEO in-person approval record) can approve the annual budget.");
            }

            await _gateService.EnsureGateSatisfiedAsync(
                CeoApprovalItemTypes.AnnualBudget,
                budget.FiscalYear.ToString(),
                budget.TotalBudget,
                budget.CreatedAt);
        }

        if (budget == null)
            throw new KeyNotFoundException($"Annual budget {annualBudgetId} not found.");

        if (budget.Status != "Submitted")
        {
            throw new InvalidOperationException($"Cannot approve budget in status '{budget.Status}'. Must be 'Submitted'.");
        }

        budget.Status = "Approved";
        budget.ApprovedBy = userId;
        budget.ApprovedAt = DateTime.UtcNow;
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Reject Annual Budget (Strictly CEO / President with required reason)
    // ─────────────────────────────────────────────────────────────────────────
    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    public async Task<AnnualBudgetDto> RejectAnnualBudgetAsync(int annualBudgetId, string reason, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (!IsCeoOrPresident(userRole))
        {
            throw new UnauthorizedAccessException("Only CEO/President can reject the annual budget.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is strictly required.");
        }

        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.AnnualBudgetId == annualBudgetId);

        if (budget == null)
            throw new KeyNotFoundException($"Annual budget {annualBudgetId} not found.");

        if (budget.Status != "Submitted")
        {
            throw new InvalidOperationException($"Cannot reject budget in status '{budget.Status}'. Must be 'Submitted'.");
        }

        budget.Status = "Rejected";
        budget.RejectionReason = reason.Trim();
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Allocate Budget by Department & Category (Finance Manager after Approved)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AnnualBudgetDto> AllocateBudgetAsync(AllocateBudgetRequest request, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can allocate the approved budget.");
        }

        var budget = await _context.AnnualBudgets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.AnnualBudgetId == request.AnnualBudgetId);

        if (budget == null)
            throw new KeyNotFoundException($"Annual budget {request.AnnualBudgetId} not found.");

        if (budget.Status != "Approved")
        {
            throw new InvalidOperationException("Budget can only be allocated once it is Approved by the CEO/President.");
        }

        foreach (var alloc in request.Allocations)
        {
            var line = budget.Lines.FirstOrDefault(l =>
                l.BudgetLineId == alloc.BudgetLineId ||
                (l.DepartmentCode == alloc.DepartmentCode && l.Category == alloc.Category && l.Month == alloc.Month));

            if (line != null)
            {
                line.BudgetAmount = alloc.AllocatedAmount;
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        budget.TotalBudget = budget.Lines.Sum(l => l.BudgetAmount);
        budget.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToAnnualBudgetDto(budget);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Check Availability & Soft Block Controls
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<BudgetAvailabilityResponse> CheckAvailabilityAsync(CheckBudgetAvailabilityRequest request)
    {
        var line = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == request.FiscalYear &&
                l.DepartmentCode == request.DepartmentCode &&
                l.Category == request.Category &&
                l.Month == request.Month);

        // Fetch active Setting (B) threshold limit (fail-safe to Limit A if unset)
        var ceoSetting = await _gateService.GetCurrentThresholdAsync(DateTime.UtcNow);
        decimal threshold = ceoSetting.EffectiveCeoLimit;

        if (line == null)
        {
            // No budget allocated for this category/month: entire amount is over-budget
            return new BudgetAvailabilityResponse
            {
                IsAvailable = false,
                AvailableAmount = 0m,
                RequestedAmount = request.RequestedAmount,
                OverBudgetAmount = request.RequestedAmount,
                IsOverBudget = true,
                RequiresCeoApproval = request.RequestedAmount > threshold,
                ThresholdLimit = threshold,
                Message = $"No budget line allocated for {request.DepartmentCode} - {request.Category} (Month {request.Month}). Override required."
            };
        }

        decimal available = line.Available; // Budget - Committed - Actual

        if (request.RequestedAmount <= available)
        {
            return new BudgetAvailabilityResponse
            {
                IsAvailable = true,
                AvailableAmount = available,
                RequestedAmount = request.RequestedAmount,
                OverBudgetAmount = 0m,
                IsOverBudget = false,
                RequiresCeoApproval = false,
                ThresholdLimit = threshold,
                Message = "Budget available."
            };
        }
        else
        {
            decimal overBudgetAmount = request.RequestedAmount - available;
            bool requiresCeo = overBudgetAmount > threshold;

            return new BudgetAvailabilityResponse
            {
                IsAvailable = false,
                AvailableAmount = available,
                RequestedAmount = request.RequestedAmount,
                OverBudgetAmount = overBudgetAmount,
                IsOverBudget = true,
                RequiresCeoApproval = requiresCeo,
                ThresholdLimit = threshold,
                Message = $"Soft block: Request exceeds available budget by ₱{overBudgetAmount:N2}. Override required by {(requiresCeo ? "CEO/President" : "Finance Manager")}."
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Request Budget Override (Soft Block Resolution)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<BudgetOverride> RequestBudgetOverrideAsync(
        RequestBudgetOverrideRequest request, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Reason is strictly required for budget override.");
        }

        var line = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == request.FiscalYear &&
                l.DepartmentCode == request.DepartmentCode &&
                l.Category == request.Category &&
                l.Month == request.Month);

        if (line == null)
        {
            throw new KeyNotFoundException($"Budget line not found for {request.DepartmentCode} - {request.Category} (FY {request.FiscalYear}, M {request.Month}).");
        }

        decimal available = line.Available;
        decimal overBudgetAmount = request.RequestedAmount > available ? request.RequestedAmount - available : 0m;

        // Fetch active Setting (B) threshold limit (fail-safe to Limit A if unset)
        var ceoSetting = await _gateService.GetCurrentThresholdAsync(DateTime.UtcNow);
        decimal threshold = ceoSetting.EffectiveCeoLimit;

        bool requiresCeo = overBudgetAmount > threshold;

        var budgetOverride = new BudgetOverride
        {
            BudgetLineId = line.BudgetLineId,
            RequestedAmount = request.RequestedAmount,
            OverBudgetAmount = overBudgetAmount,
            Reason = request.Reason.Trim(),
            Status = "Pending",
            RequestedBy = userId,
            RequestedAt = DateTime.UtcNow,
            ThresholdLimit = threshold,
            RequiresCeoApproval = requiresCeo
        };

        _context.BudgetOverrides.Add(budgetOverride);
        await _context.SaveChangesAsync(default);

        return budgetOverride;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. Approve Budget Override (FM <= Threshold, CEO > Threshold)
    // ─────────────────────────────────────────────────────────────────────────
    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    public async Task<BudgetOverride> ApproveBudgetOverrideAsync(int overrideId, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        var budgetOverride = await _context.BudgetOverrides
            .FirstOrDefaultAsync(o => o.OverrideId == overrideId);

        if (budgetOverride == null)
            throw new KeyNotFoundException($"Budget override {overrideId} not found.");

        if (budgetOverride.Status != "Pending")
            throw new InvalidOperationException($"Cannot approve override with status '{budgetOverride.Status}'.");

        // Role vs CSH-09 Threshold check:
        // Finance Manager approves at or below CSH-09 threshold; CEO/president above it.
        if (budgetOverride.RequiresCeoApproval)
        {
            if (!IsCeoOrPresident(userRole))
            {
                await _gateService.EnsureGateSatisfiedAsync(
                    CeoApprovalItemTypes.BudgetOverride,
                    overrideId.ToString(),
                    budgetOverride.OverBudgetAmount,
                    budgetOverride.RequestedAt);
            }
        }
        else
        {
            if (!IsFinanceManager(userRole))
            {
                throw new UnauthorizedAccessException("Only Finance Manager or CEO/President can approve this budget override.");
            }
        }

        budgetOverride.Status = "Approved";
        budgetOverride.ApprovedBy = userId;
        budgetOverride.ApprovedRole = userRole;
        budgetOverride.ApprovedAt = DateTime.UtcNow;

        // Increment override count and update committed amount on the budget line
        var line = await _context.MonthlyBudgetLines.FirstOrDefaultAsync(l => l.BudgetLineId == budgetOverride.BudgetLineId);
        if (line != null)
        {
            line.OverrideCount += 1;
            line.CommittedAmount += budgetOverride.RequestedAmount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(default);
        return budgetOverride;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Reject Budget Override
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<BudgetOverride> RejectBudgetOverrideAsync(int overrideId, string reason, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is strictly required.");
        }

        var budgetOverride = await _context.BudgetOverrides
            .FirstOrDefaultAsync(o => o.OverrideId == overrideId);

        if (budgetOverride == null)
            throw new KeyNotFoundException($"Budget override {overrideId} not found.");

        if (budgetOverride.Status != "Pending")
            throw new InvalidOperationException($"Cannot reject override with status '{budgetOverride.Status}'.");

        budgetOverride.Status = "Rejected";
        budgetOverride.RejectionReason = reason.Trim();
        budgetOverride.ApprovedBy = userId;
        budgetOverride.ApprovedRole = userRole;
        budgetOverride.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);
        return budgetOverride;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. Request Budget Transfer / Carry-Over (Finance Manager requests)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<BudgetTransfer> RequestBudgetTransferAsync(
        RequestBudgetTransferRequest request, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can request budget transfers or carry-overs.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Transfer reason is strictly required.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Transfer amount must be greater than zero.");
        }

        // Validate source budget line has enough available budget
        var fromLine = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == request.FiscalYear &&
                l.DepartmentCode == request.FromDepartmentCode &&
                l.Category == request.FromCategory &&
                l.Month == request.FromMonth);

        if (fromLine == null)
        {
            throw new KeyNotFoundException($"Source budget line not found for {request.FromDepartmentCode} - {request.FromCategory} (Month {request.FromMonth}).");
        }

        if (fromLine.Available < request.Amount)
        {
            throw new InvalidOperationException(
                $"Insufficient available budget on source line. Available: ₱{fromLine.Available:N2}, Requested: ₱{request.Amount:N2}.");
        }

        // Destination line check
        var toLine = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == request.FiscalYear &&
                l.DepartmentCode == request.ToDepartmentCode &&
                l.Category == request.ToCategory &&
                l.Month == request.ToMonth);

        if (toLine == null)
        {
            throw new KeyNotFoundException($"Destination budget line not found for {request.ToDepartmentCode} - {request.ToCategory} (Month {request.ToMonth}).");
        }

        var transfer = new BudgetTransfer
        {
            FiscalYear = request.FiscalYear,
            TransferType = request.TransferType ?? "Transfer",
            FromDepartmentCode = request.FromDepartmentCode,
            FromCategory = request.FromCategory,
            FromMonth = request.FromMonth,
            ToDepartmentCode = request.ToDepartmentCode,
            ToCategory = request.ToCategory,
            ToMonth = request.ToMonth,
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            Status = "Pending",
            RequestedBy = userId,
            RequestedAt = DateTime.UtcNow
        };

        _context.BudgetTransfers.Add(transfer);
        await _context.SaveChangesAsync(default);

        return transfer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13. Approve Budget Transfer / Carry-Over (Strictly CEO / President approves)
    // ─────────────────────────────────────────────────────────────────────────
    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    public async Task<BudgetTransfer> ApproveBudgetTransferAsync(int transferId, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        var transfer = await _context.BudgetTransfers
            .FirstOrDefaultAsync(t => t.TransferId == transferId);

        if (transfer == null)
            throw new KeyNotFoundException($"Budget transfer {transferId} not found.");

        if (!IsCeoOrPresident(userRole))
        {
            if (!IsFinanceManager(userRole))
            {
                throw new UnauthorizedAccessException("Only Finance Manager can approve budget transfers.");
            }

            string gateItemType = string.Equals(transfer.TransferType, "CarryOver", StringComparison.OrdinalIgnoreCase)
                ? CeoApprovalItemTypes.BudgetCarryOver
                : CeoApprovalItemTypes.BudgetTransfer;

            await _gateService.EnsureGateSatisfiedAsync(
                gateItemType,
                transferId.ToString(),
                transfer.Amount,
                transfer.RequestedAt);
        }

        if (transfer.Status != "Pending")
            throw new InvalidOperationException($"Cannot approve transfer in status '{transfer.Status}'.");

        var fromLine = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == transfer.FiscalYear &&
                l.DepartmentCode == transfer.FromDepartmentCode &&
                l.Category == transfer.FromCategory &&
                l.Month == transfer.FromMonth);

        var toLine = await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == transfer.FiscalYear &&
                l.DepartmentCode == transfer.ToDepartmentCode &&
                l.Category == transfer.ToCategory &&
                l.Month == transfer.ToMonth);

        if (fromLine == null || toLine == null)
        {
            throw new InvalidOperationException("Source or destination budget line no longer exists.");
        }

        if (fromLine.Available < transfer.Amount)
        {
            throw new InvalidOperationException(
                $"Source line available budget (₱{fromLine.Available:N2}) is now less than transfer amount (₱{transfer.Amount:N2}).");
        }

        // Deduct from source and add to destination
        // Company total remains STRICTLY UNCHANGED: (+Amount) + (-Amount) = 0
        fromLine.BudgetAmount -= transfer.Amount;
        fromLine.UpdatedAt = DateTime.UtcNow;

        toLine.BudgetAmount += transfer.Amount;
        toLine.UpdatedAt = DateTime.UtcNow;

        transfer.Status = "Approved";
        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;

        // Log transfer in AuditLogs
        var audit = new FOMS.Domain.Entities.AuditLog
        {
            LoggedAt = DateTime.UtcNow,
            UserId = userId,
            EntityName = "BudgetTransfer",
            EntityId = transfer.TransferId.ToString(),
            Action = "Budget Transfer Approved",
            Details = $"Approved {transfer.TransferType} of ₱{transfer.Amount:N2} from {transfer.FromDepartmentCode}/{transfer.FromCategory} (M{transfer.FromMonth}) to {transfer.ToDepartmentCode}/{transfer.ToCategory} (M{transfer.ToMonth}). Reason: {transfer.Reason}. Company total budget unchanged."
        };
        _context.AuditLogs.Add(audit);

        await _context.SaveChangesAsync(default);

        return transfer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 14. Reject Budget Transfer
    // ─────────────────────────────────────────────────────────────────────────
    [Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]
    public async Task<BudgetTransfer> RejectBudgetTransferAsync(int transferId, string reason, string userId, string userRole)
    {
        EnsureNotHeadAccountant(userRole);

        if (!IsCeoOrPresident(userRole))
        {
            throw new UnauthorizedAccessException("Only CEO/President can reject budget transfers.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is strictly required.");
        }

        var transfer = await _context.BudgetTransfers
            .FirstOrDefaultAsync(t => t.TransferId == transferId);

        if (transfer == null)
            throw new KeyNotFoundException($"Budget transfer {transferId} not found.");

        if (transfer.Status != "Pending")
            throw new InvalidOperationException($"Cannot reject transfer in status '{transfer.Status}'.");

        transfer.Status = "Rejected";
        transfer.RejectionReason = reason.Trim();
        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return transfer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 15. Weekly Report (Actual vs Monthly Budget per department and category)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<WeeklyBudgetReportDto> GetWeeklyBudgetReportAsync(int fiscalYear, int month)
    {
        var lines = await _context.MonthlyBudgetLines
            .Where(l => l.FiscalYear == fiscalYear && l.Month == month)
            .ToListAsync();

        var annualBudget = await _context.AnnualBudgets
            .FirstOrDefaultAsync(b => b.FiscalYear == fiscalYear);

        decimal warningThreshold = annualBudget?.WarningAlertThresholdPercent ?? 80.00m;

        var report = new WeeklyBudgetReportDto
        {
            FiscalYear = fiscalYear,
            Month = month,
            GeneratedAt = DateTime.UtcNow,
            TotalMonthlyBudget = lines.Sum(l => l.BudgetAmount),
            TotalActualSpend = lines.Sum(l => l.ActualAmount),
            TotalCommitted = lines.Sum(l => l.CommittedAmount),
            TotalAvailable = lines.Sum(l => l.Available),
            TotalRemaining = lines.Sum(l => l.Remaining)
        };

        report.OverallUtilizationPercent = report.TotalMonthlyBudget > 0
            ? Math.Round((report.TotalActualSpend / report.TotalMonthlyBudget) * 100m, 2)
            : 0m;

        var groupedByDept = lines.GroupBy(l => l.DepartmentCode);

        foreach (var group in groupedByDept)
        {
            var deptCode = group.Key;
            var deptName = group.FirstOrDefault()?.DepartmentName ?? deptCode;

            decimal deptBudget = group.Sum(l => l.BudgetAmount);
            decimal deptActual = group.Sum(l => l.ActualAmount);
            decimal deptCommitted = group.Sum(l => l.CommittedAmount);
            decimal deptAvailable = group.Sum(l => l.Available);
            decimal deptRemaining = group.Sum(l => l.Remaining);
            decimal deptUtil = deptBudget > 0 ? Math.Round((deptActual / deptBudget) * 100m, 2) : 0m;

            string deptAlert = "Normal";
            if (deptUtil >= 100.00m) deptAlert = "Exhausted";
            else if (deptUtil >= warningThreshold) deptAlert = "Warning";

            var categoryLines = group.Select(l =>
            {
                var lineDto = MapToLineDto(l, warningThreshold);
                return lineDto;
            }).ToList();

            report.Departments.Add(new DepartmentBudgetSummaryDto
            {
                DepartmentCode = deptCode,
                DepartmentName = deptName,
                MonthlyBudget = deptBudget,
                ActualSpend = deptActual,
                Committed = deptCommitted,
                Available = deptAvailable,
                Remaining = deptRemaining,
                UtilizationPercent = deptUtil,
                AlertStatus = deptAlert,
                CategoryLines = categoryLines
            });
        }

        return report;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Mappings
    // ─────────────────────────────────────────────────────────────────────────
    private AnnualBudgetDto MapToAnnualBudgetDto(AnnualBudget budget)
    {
        var dto = new AnnualBudgetDto
        {
            AnnualBudgetId = budget.AnnualBudgetId,
            FiscalYear = budget.FiscalYear,
            TotalBudget = budget.TotalBudget,
            Status = budget.Status,
            PreparedBy = budget.PreparedBy,
            SubmittedAt = budget.SubmittedAt,
            ReviewedBy = budget.ReviewedBy,
            ReviewedAt = budget.ReviewedAt,
            ApprovedBy = budget.ApprovedBy,
            ApprovedAt = budget.ApprovedAt,
            RejectionReason = budget.RejectionReason,
            WarningAlertThresholdPercent = budget.WarningAlertThresholdPercent,
            TotalCommitted = budget.Lines.Sum(l => l.CommittedAmount),
            TotalActual = budget.Lines.Sum(l => l.ActualAmount),
            TotalAvailable = budget.Lines.Sum(l => l.Available),
            TotalRemaining = budget.Lines.Sum(l => l.Remaining),
            Lines = budget.Lines.Select(l => MapToLineDto(l, budget.WarningAlertThresholdPercent)).ToList()
        };

        return dto;
    }

    private MonthlyBudgetLineDto MapToLineDto(MonthlyBudgetLine line, decimal warningThreshold)
    {
        string alert = "Normal";
        if (line.UtilizationPercent >= 100.00m)
            alert = "Exhausted";
        else if (line.UtilizationPercent >= warningThreshold)
            alert = "Warning";

        return new MonthlyBudgetLineDto
        {
            BudgetLineId = line.BudgetLineId,
            AnnualBudgetId = line.AnnualBudgetId,
            FiscalYear = line.FiscalYear,
            DepartmentCode = line.DepartmentCode,
            DepartmentName = line.DepartmentName,
            Category = line.Category,
            Month = line.Month,
            BudgetAmount = line.BudgetAmount,
            CommittedAmount = line.CommittedAmount,
            ActualAmount = line.ActualAmount,
            HistoricalActual = line.HistoricalActual,
            Available = line.Available,
            Remaining = line.Remaining,
            UtilizationPercent = line.UtilizationPercent,
            OverrideCount = line.OverrideCount,
            AlertStatus = alert
        };
    }
}
