using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Application.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IApplicationDbContext _context;
    private readonly IBudgetService _budgetService;

    public DepartmentService(IApplicationDbContext context, IBudgetService budgetService)
    {
        _context = context;
        _budgetService = budgetService;
    }

    private static bool IsFinanceManager(string role)
    {
        return string.Equals(role, "Finance Manager", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Get Selectable Departments
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<List<DepartmentDto>> GetSelectableDepartmentsAsync(string userRole, CancellationToken ct = default)
    {
        bool isFm = IsFinanceManager(userRole);

        var query = _context.Departments
            .AsNoTracking()
            .Where(d => d.IsActive && !d.IsLegacy);

        if (!isFm)
        {
            query = query.Where(d => !d.IsOverhead && d.DepartmentCode != FomsDepartments.Overhead);
        }

        var departments = await query
            .OrderBy(d => d.DepartmentId)
            .ToListAsync(ct);

        return departments.Select(d => new DepartmentDto
        {
            DepartmentId = d.DepartmentId,
            DepartmentCode = d.DepartmentCode,
            DepartmentName = d.DepartmentName,
            IsActive = d.IsActive,
            IsOverhead = d.IsOverhead,
            IsLegacy = d.IsLegacy
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Option A Suggestion Chip (Requester's own last department)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<DepartmentSuggestionDto> GetDepartmentSuggestionAsync(string userId, string userRole, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new DepartmentSuggestionDto { HasSuggestion = false };
        }

        bool isFm = IsFinanceManager(userRole);

        // Check latest CashAdvance
        var lastCa = await _context.CashAdvances
            .AsNoTracking()
            .Where(ca => ca.CourierName == userId || ca.AdvanceId.Contains(userId))
            .OrderByDescending(ca => ca.CreatedAt)
            .Select(ca => new { Dept = ca.DepartmentCode, Date = ca.CreatedAt ?? DateTime.MinValue })
            .FirstOrDefaultAsync(ct);

        // Check latest Reimbursement
        var lastReim = await _context.Reimbursements
            .AsNoTracking()
            .Where(r => r.EmployeeId == userId)
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new { Dept = r.DepartmentCode, Date = r.SubmittedAt })
            .FirstOrDefaultAsync(ct);

        // Check latest DirectDisbursement
        var lastDd = await _context.DirectDisbursements
            .AsNoTracking()
            .Where(dd => dd.RequestedBy == userId)
            .OrderByDescending(dd => dd.CreatedAt)
            .Select(dd => new { Dept = dd.DepartmentCode, Date = dd.CreatedAt })
            .FirstOrDefaultAsync(ct);

        // Find the most recent non-empty department
        var candidates = new[] { lastCa, lastReim, lastDd }
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Dept))
            .OrderByDescending(c => c!.Date)
            .ToList();

        if (candidates.Count == 0)
        {
            return new DepartmentSuggestionDto { HasSuggestion = false };
        }

        string rawDept = candidates[0]!.Dept;

        // Try normalizing to canonical code
        string normalizedDept;
        try
        {
            normalizedDept = FomsDepartments.Normalize(rawDept, userRole, isNewRequest: false);
        }
        catch
        {
            return new DepartmentSuggestionDto { HasSuggestion = false };
        }

        // Never suggest Overhead to non-FM
        if (normalizedDept == FomsDepartments.Overhead && !isFm)
        {
            return new DepartmentSuggestionDto { HasSuggestion = false };
        }

        // If legacy DEP-OPS was used in the past, do not suggest legacy code for new request
        if (normalizedDept == FomsDepartments.LegacyOperations)
        {
            return new DepartmentSuggestionDto { HasSuggestion = false };
        }

        string deptName = FomsDepartments.DepartmentNames.TryGetValue(normalizedDept, out var name)
            ? name
            : normalizedDept;

        return new DepartmentSuggestionDto
        {
            DepartmentCode = normalizedDept,
            DepartmentName = deptName,
            HasSuggestion = true
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Validate and Normalize Department
    // ─────────────────────────────────────────────────────────────────────────
    public Task<string> ValidateAndNormalizeDepartmentAsync(string? departmentCode, string userRole, bool isNewRequest = true, CancellationToken ct = default)
    {
        string normalized = FomsDepartments.Normalize(departmentCode, userRole, isNewRequest);
        return Task.FromResult(normalized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. FM Changes Department Pre-Approval (R5.11 Item 5)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<DepartmentChangeLogDto> ChangeRequestDepartmentAsync(
        ChangeDepartmentRequest request, string changedByEmployeeId, string userRole, CancellationToken ct = default)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only the Finance Manager can modify request departments.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 3)
        {
            throw new ArgumentException("A detailed reason for the department change is strictly required.");
        }

        string newDept = FomsDepartments.Normalize(request.NewDepartmentCode, userRole, isNewRequest: true);
        string oldDept = string.Empty;
        string requestTypeNormalized = request.RequestType.Trim();

        if (requestTypeNormalized.Equals("CashAdvance", StringComparison.OrdinalIgnoreCase))
        {
            var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == request.RequestId, ct);
            if (advance == null)
            {
                throw new KeyNotFoundException($"Cash advance request '{request.RequestId}' not found.");
            }

            if (advance.IsDepartmentLocked || advance.Status == "Approved" || advance.Status == "Disbursed" || advance.Status == "Liquidated" || advance.Status == "Settled")
            {
                throw new InvalidOperationException($"Department cannot be modified after request approval. Cash advance '{advance.AdvanceId}' is locked (Status: {advance.Status}); fixes require a logged correction entry.");
            }

            oldDept = advance.DepartmentCode;
            if (string.Equals(oldDept, newDept, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"New department '{newDept}' is identical to the current department.");
            }

            // Re-run budget check on new department
            var availability = await _budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
            {
                FiscalYear = advance.FiscalYear,
                DepartmentCode = newDept,
                Category = advance.Category,
                Month = advance.Month,
                RequestedAmount = advance.AmountGiven
            });

            // Adjust committed reservations if currently committed
            if (advance.Status == "Submitted" || advance.Status == "Pending Finance Review")
            {
                var oldLine = await _context.MonthlyBudgetLines.FirstOrDefaultAsync(l =>
                    l.FiscalYear == advance.FiscalYear &&
                    l.DepartmentCode == oldDept &&
                    l.Category == advance.Category &&
                    l.Month == advance.Month, ct);

                if (oldLine != null && oldLine.CommittedAmount >= advance.AmountGiven)
                {
                    oldLine.CommittedAmount -= advance.AmountGiven;
                    oldLine.UpdatedAt = DateTime.UtcNow;
                }

                var newLine = await _context.MonthlyBudgetLines.FirstOrDefaultAsync(l =>
                    l.FiscalYear == advance.FiscalYear &&
                    l.DepartmentCode == newDept &&
                    l.Category == advance.Category &&
                    l.Month == advance.Month, ct);

                if (newLine != null)
                {
                    newLine.CommittedAmount += advance.AmountGiven;
                    newLine.UpdatedAt = DateTime.UtcNow;
                }
            }

            advance.DepartmentCode = newDept;
            advance.DepartmentChangeReason = request.Reason.Trim();
        }
        else if (requestTypeNormalized.Equals("DirectDisbursement", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(request.RequestId, out int ddId))
            {
                throw new ArgumentException("RequestId must be integer for DirectDisbursement.");
            }

            var dd = await _context.DirectDisbursements.FirstOrDefaultAsync(d => d.DisbursementId == ddId, ct);
            if (dd == null)
            {
                throw new KeyNotFoundException($"Direct disbursement request '{request.RequestId}' not found.");
            }

            if (dd.IsDepartmentLocked || dd.Status == "Approved" || dd.Status == "Released" || dd.Status == "Paid")
            {
                throw new InvalidOperationException($"Department cannot be modified after request approval. Direct disbursement '{dd.DisbursementId}' is locked (Status: {dd.Status}); fixes require a logged correction entry.");
            }

            oldDept = dd.DepartmentCode;
            if (string.Equals(oldDept, newDept, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"New department '{newDept}' is identical to the current department.");
            }

            var availability = await _budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
            {
                FiscalYear = dd.FiscalYear,
                DepartmentCode = newDept,
                Category = dd.Category,
                Month = dd.Month,
                RequestedAmount = dd.Amount
            });

            dd.DepartmentCode = newDept;
            dd.DepartmentChangeReason = request.Reason.Trim();
        }
        else if (requestTypeNormalized.Equals("Reimbursement", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(request.RequestId, out int reimId))
            {
                throw new ArgumentException("RequestId must be integer for Reimbursement.");
            }

            var reim = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == reimId, ct);
            if (reim == null)
            {
                throw new KeyNotFoundException($"Reimbursement request '{request.RequestId}' not found.");
            }

            if (reim.IsDepartmentLocked || reim.Status == "Approved" || reim.Status == "Paid")
            {
                throw new InvalidOperationException($"Department cannot be modified after request approval. Reimbursement '{reim.ReimbursementId}' is locked (Status: {reim.Status}); fixes require a logged correction entry.");
            }

            oldDept = reim.DepartmentCode;
            if (string.Equals(oldDept, newDept, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"New department '{newDept}' is identical to the current department.");
            }

            var availability = await _budgetService.CheckAvailabilityAsync(new CheckBudgetAvailabilityRequest
            {
                FiscalYear = reim.FiscalYear,
                DepartmentCode = newDept,
                Category = reim.Category,
                Month = reim.Month,
                RequestedAmount = reim.Amount
            });

            reim.DepartmentCode = newDept;
            reim.DepartmentChangeReason = request.Reason.Trim();
        }
        else
        {
            throw new ArgumentException($"Invalid RequestType '{request.RequestType}'. Allowed types: CashAdvance, DirectDisbursement, Reimbursement.");
        }

        // Insert immutable change log
        var log = new DepartmentChangeLog
        {
            RequestType = requestTypeNormalized,
            RequestId = request.RequestId,
            OldDepartmentCode = oldDept,
            NewDepartmentCode = newDept,
            Reason = request.Reason.Trim(),
            ChangedBy = changedByEmployeeId,
            ChangedAt = DateTime.UtcNow
        };

        _context.DepartmentChangeLogs.Add(log);
        await _context.SaveChangesAsync(ct);

        string oldName = FomsDepartments.DepartmentNames.TryGetValue(oldDept, out var oName) ? oName : oldDept;
        string newName = FomsDepartments.DepartmentNames.TryGetValue(newDept, out var nName) ? nName : newDept;

        return new DepartmentChangeLogDto
        {
            LogId = log.LogId,
            RequestType = log.RequestType,
            RequestId = log.RequestId,
            OldDepartmentCode = log.OldDepartmentCode,
            OldDepartmentName = oldName,
            NewDepartmentCode = log.NewDepartmentCode,
            NewDepartmentName = newName,
            Reason = log.Reason,
            ChangedBy = log.ChangedBy,
            ChangedAt = log.ChangedAt
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Get Department Change Logs
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<List<DepartmentChangeLogDto>> GetDepartmentChangeLogsAsync(string requestType, string requestId, CancellationToken ct = default)
    {
        var logs = await _context.DepartmentChangeLogs
            .AsNoTracking()
            .Where(l => l.RequestType == requestType && l.RequestId == requestId)
            .OrderByDescending(l => l.ChangedAt)
            .ToListAsync(ct);

        return logs.Select(l => new DepartmentChangeLogDto
        {
            LogId = l.LogId,
            RequestType = l.RequestType,
            RequestId = l.RequestId,
            OldDepartmentCode = l.OldDepartmentCode,
            OldDepartmentName = FomsDepartments.DepartmentNames.TryGetValue(l.OldDepartmentCode, out var o) ? o : l.OldDepartmentCode,
            NewDepartmentCode = l.NewDepartmentCode,
            NewDepartmentName = FomsDepartments.DepartmentNames.TryGetValue(l.NewDepartmentCode, out var n) ? n : l.NewDepartmentCode,
            Reason = l.Reason,
            ChangedBy = l.ChangedBy,
            ChangedAt = l.ChangedAt
        }).ToList();
    }
}
