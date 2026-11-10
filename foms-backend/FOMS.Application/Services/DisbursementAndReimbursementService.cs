using FOMS.Application.Services;
using FOMS.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Services;

public class DisbursementAndReimbursementService : IDisbursementAndReimbursementService
{
    private readonly IApplicationDbContext _context;
    private readonly ICeoApprovalGateService _gateService;

    public DisbursementAndReimbursementService(IApplicationDbContext context) : this(context, new CeoApprovalGateService(context))
    {
    }

    public DisbursementAndReimbursementService(IApplicationDbContext context, ICeoApprovalGateService gateService)
    {
        _context = context;
        _gateService = gateService;
    }

    private void EnsureValidCategory(string category)
    {
        if (!DisbursementCategories.IsValidCategory(category))
        {
            throw new ArgumentException(
                $"Category '{category}' is invalid. Allowed categories: Fuel, Toll Fees, Parking, Food Allowance, Vulcanizing, Repairs and Maintenance, plus Hotel, Branch Visits, Office Supplies and Other.");
        }
    }

    private bool IsFinanceManager(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        if (FomsRoles.IsFinanceManager(role)) return true;
        var r = role.ToLowerInvariant();
        if (r.Contains("assistant")) return false;
        return r.Contains("finance manager") || r.Contains("admin");
    }

    private bool IsAssistantFinanceManager(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        if (FomsRoles.IsAssistantFinanceManager(role)) return true;
        var r = role.ToLowerInvariant();
        return r.Contains("assistant") || IsFinanceManager(role);
    }

    private async Task<decimal> GetCsh09ThresholdAsync(DateTime? targetDate = null)
    {
        var setting = await _gateService.GetCurrentThresholdAsync(targetDate ?? DateTime.UtcNow);
        return setting.EffectiveCeoLimit;
    }

    private async Task<MonthlyBudgetLine?> GetBudgetLineAsync(int fiscalYear, string departmentCode, string category, int month)
    {
        return await _context.MonthlyBudgetLines
            .FirstOrDefaultAsync(l =>
                l.FiscalYear == fiscalYear &&
                l.DepartmentCode == departmentCode &&
                l.Category == category &&
                l.Month == month);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CASH ADVANCE LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<CashAdvanceResponseDto> RequestCashAdvanceAsync(RequestCashAdvanceDto dto, string userId, string userRole)
    {
        EnsureValidCategory(dto.Category);
        dto.DepartmentCode = FomsDepartments.Normalize(dto.DepartmentCode, userRole, isNewRequest: false);

        // Budget check
        var line = await GetBudgetLineAsync(dto.FiscalYear, dto.DepartmentCode, dto.Category, dto.Month);
        if (line != null && line.Available < dto.Amount)
        {
            throw new InvalidOperationException(
                $"Soft block: Requested amount of ₱{dto.Amount:N2} exceeds available budget of ₱{line.Available:N2} for {dto.DepartmentCode}/{dto.Category}. Budget override required.");
        }

        // Calculate Due Date based on LocationType
        // NCR or Luzon: due next day. Out-of-Area: on return date.
        var reqDate = DateTime.UtcNow;
        DateTime dueDate;
        if (string.Equals(dto.LocationType, "Out-of-Area", StringComparison.OrdinalIgnoreCase))
        {
            dueDate = dto.ReturnDate ?? reqDate.AddDays(3);
        }
        else
        {
            dueDate = reqDate.AddDays(1);
        }

        decimal threshold = await GetCsh09ThresholdAsync();
        bool requiresCeo = dto.Amount > threshold;

        var advanceId = $"CA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var advance = new CashAdvance
        {
            AdvanceId = advanceId,
            CourierName = dto.CourierName,
            TripReference = dto.TripReference,
            AmountGiven = dto.Amount,
            DepartmentCode = dto.DepartmentCode,
            Category = dto.Category,
            FiscalYear = dto.FiscalYear,
            Month = dto.Month,
            LocationType = dto.LocationType ?? "NCR",
            DueDate = dueDate,
            ReturnDate = dto.ReturnDate,
            RequiresCeoApproval = requiresCeo,
            Notes = dto.Notes,
            Status = "Requested",
            CreatedAt = reqDate,
            SubmittedByEmployeeId = userId
        };

        _context.CashAdvances.Add(advance);
        await _context.SaveChangesAsync(default);

        return MapToAdvanceDto(advance);
    }

    public async Task<CashAdvanceResponseDto> ApproveCashAdvanceAsync(string advanceId, string userId, string userRole)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can approve cash advances.");
        }

        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == advanceId);
        if (advance == null) throw new KeyNotFoundException($"Cash advance {advanceId} not found.");

        if (advance.Status != "Requested")
        {
            throw new InvalidOperationException($"Cannot approve advance in status '{advance.Status}'. Must be 'Requested'.");
        }

        if (!string.IsNullOrEmpty(advance.SubmittedByEmployeeId) && FomsRoles.IsSameEmployeeId(advance.SubmittedByEmployeeId, userId))
        {
            throw new UnauthorizedAccessException("Separation of duties violation: Requester cannot approve their own cash advance.");
        }

        // Commitment: budget committed amount increases
        var line = await GetBudgetLineAsync(advance.FiscalYear, advance.DepartmentCode, advance.Category, advance.Month);
        if (line != null)
        {
            line.CommittedAmount += advance.AmountGiven;
            line.UpdatedAt = DateTime.UtcNow;
        }

        advance.Status = "Approved";
        advance.ApprovedBy = userId;
        advance.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);
        return MapToAdvanceDto(advance);
    }

    public async Task<CashAdvanceResponseDto> ReleaseCashAdvanceAsync(string advanceId, string userId, string userRole)
    {
        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == advanceId);
        if (advance == null) throw new KeyNotFoundException($"Cash advance {advanceId} not found.");

        if (advance.Status != "Approved")
        {
            throw new InvalidOperationException($"Cannot release advance in status '{advance.Status}'. Must be 'Approved'.");
        }

        // Gate check for CashOutflow above CSH-09 threshold (Decision E: unset threshold mandates CEO approval for all outflows)
        await _gateService.EnsureGateSatisfiedAsync(
            CeoApprovalItemTypes.CashOutflow,
            advanceId,
            advance.AmountGiven,
            advance.CreatedAt);

        advance.Status = "Released";
        advance.ReleasedBy = userId;
        advance.ReleaseDate = DateTime.UtcNow;

        // Record Cash Outflow
        var outflow = new CashOutflow
        {
            Amount = advance.AmountGiven,
            Category = advance.Category,
            SourceReference = advance.AdvanceId,
            Description = $"Cash advance for {advance.CourierName} ({advance.TripReference})",
            Status = "Released",
            OutflowDate = DateTime.UtcNow,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            RecordedBy = userId
        };
        _context.CashOutflows.Add(outflow);

        await _context.SaveChangesAsync(default);
        return MapToAdvanceDto(advance);
    }

    public async Task<CashAdvanceResponseDto> RejectCashAdvanceAsync(string advanceId, string reason, string userId, string userRole)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Rejection reason is required.");

        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == advanceId);
        if (advance == null) throw new KeyNotFoundException($"Cash advance {advanceId} not found.");

        // If previously approved/committed, release commitment
        if (advance.Status == "Approved")
        {
            var line = await GetBudgetLineAsync(advance.FiscalYear, advance.DepartmentCode, advance.Category, advance.Month);
            if (line != null)
            {
                line.CommittedAmount = Math.Max(0m, line.CommittedAmount - advance.AmountGiven);
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        advance.Status = "Rejected";
        advance.RejectionReason = reason.Trim();
        await _context.SaveChangesAsync(default);
        return MapToAdvanceDto(advance);
    }

    public async Task<CashAdvanceResponseDto> CancelCashAdvanceAsync(string advanceId, string reason, string userId, string userRole)
    {
        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == advanceId);
        if (advance == null) throw new KeyNotFoundException($"Cash advance {advanceId} not found.");

        if (advance.Status == "Liquidated")
        {
            throw new InvalidOperationException("Cannot cancel an already liquidated cash advance.");
        }

        // Release commitment if committed
        if (advance.Status == "Approved" || advance.Status == "Released")
        {
            var line = await GetBudgetLineAsync(advance.FiscalYear, advance.DepartmentCode, advance.Category, advance.Month);
            if (line != null)
            {
                line.CommittedAmount = Math.Max(0m, line.CommittedAmount - advance.AmountGiven);
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        advance.Status = "Cancelled";
        advance.RejectionReason = reason;
        await _context.SaveChangesAsync(default);
        return MapToAdvanceDto(advance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DIRECT DISBURSEMENT (CSH-12: Actual at release, no liquidation)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<DirectDisbursementResponseDto> RequestDirectDisbursementAsync(RequestDirectDisbursementDto dto, string userId, string userRole)
    {
        EnsureValidCategory(dto.Category);
        dto.DepartmentCode = FomsDepartments.Normalize(dto.DepartmentCode, userRole, isNewRequest: false);

        var line = await GetBudgetLineAsync(dto.FiscalYear, dto.DepartmentCode, dto.Category, dto.Month);
        if (line != null && line.Available < dto.Amount)
        {
            throw new InvalidOperationException(
                $"Soft block: Disbursement amount of ₱{dto.Amount:N2} exceeds available budget of ₱{line.Available:N2}. Override required.");
        }

        var refNo = $"DD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var disb = new DirectDisbursement
        {
            ReferenceNumber = refNo,
            DepartmentCode = dto.DepartmentCode,
            Category = dto.Category,
            Amount = dto.Amount,
            Purpose = dto.Purpose,
            Status = "Requested",
            RequestedBy = userId,
            RequestedAt = DateTime.UtcNow,
            FiscalYear = dto.FiscalYear,
            Month = dto.Month
        };

        _context.DirectDisbursements.Add(disb);
        await _context.SaveChangesAsync(default);

        return MapToDisbursementDto(disb);
    }

    public async Task<DirectDisbursementResponseDto> ApproveDirectDisbursementAsync(int disbursementId, string userId, string userRole)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can approve direct disbursements.");
        }

        var disb = await _context.DirectDisbursements.FirstOrDefaultAsync(d => d.DisbursementId == disbursementId);
        if (disb == null) throw new KeyNotFoundException($"Direct disbursement {disbursementId} not found.");

        if (disb.Status != "Requested")
        {
            throw new InvalidOperationException($"Cannot approve disbursement in status '{disb.Status}'.");
        }

        if (!string.IsNullOrEmpty(disb.RequestedBy) && FomsRoles.IsSameEmployeeId(disb.RequestedBy, userId))
        {
            throw new UnauthorizedAccessException("Separation of duties violation: Requester cannot approve their own direct disbursement.");
        }

        // Commit budget amount
        var line = await GetBudgetLineAsync(disb.FiscalYear, disb.DepartmentCode, disb.Category, disb.Month);
        if (line != null)
        {
            line.CommittedAmount += disb.Amount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        disb.Status = "Approved";
        disb.ApprovedBy = userId;
        disb.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);
        return MapToDisbursementDto(disb);
    }

    public async Task<DirectDisbursementResponseDto> ReleaseDirectDisbursementAsync(int disbursementId, string userId, string userRole)
    {
        var disb = await _context.DirectDisbursements.FirstOrDefaultAsync(d => d.DisbursementId == disbursementId);
        if (disb == null) throw new KeyNotFoundException($"Direct disbursement {disbursementId} not found.");

        if (disb.Status != "Approved")
        {
            throw new InvalidOperationException($"Cannot release disbursement in status '{disb.Status}'. Must be 'Approved'.");
        }

        // Gate check for CashOutflow above CSH-09 threshold (Decision E: unset threshold mandates CEO approval for all outflows)
        await _gateService.EnsureGateSatisfiedAsync(
            CeoApprovalItemTypes.CashOutflow,
            disbursementId.ToString(),
            disb.Amount,
            disb.CreatedAt);

        // CSH-12: Actual at release, NO liquidation!
        // Move from Committed -> Actual
        var line = await GetBudgetLineAsync(disb.FiscalYear, disb.DepartmentCode, disb.Category, disb.Month);
        if (line != null)
        {
            line.CommittedAmount = Math.Max(0m, line.CommittedAmount - disb.Amount);
            line.ActualAmount += disb.Amount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        disb.Status = "Released";
        disb.ReleasedBy = userId;
        disb.ReleasedAt = DateTime.UtcNow;

        // Cash outflow
        var outflow = new CashOutflow
        {
            Amount = disb.Amount,
            Category = disb.Category,
            SourceReference = disb.ReferenceNumber,
            Description = $"Direct disbursement: {disb.Purpose}",
            Status = "Released",
            OutflowDate = DateTime.UtcNow,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            RecordedBy = userId
        };
        _context.CashOutflows.Add(outflow);

        await _context.SaveChangesAsync(default);
        return MapToDisbursementDto(disb);
    }

    public async Task<DirectDisbursementResponseDto> RejectDirectDisbursementAsync(int disbursementId, string reason, string userId, string userRole)
    {
        var disb = await _context.DirectDisbursements.FirstOrDefaultAsync(d => d.DisbursementId == disbursementId);
        if (disb == null) throw new KeyNotFoundException($"Direct disbursement {disbursementId} not found.");

        if (disb.Status == "Approved")
        {
            var line = await GetBudgetLineAsync(disb.FiscalYear, disb.DepartmentCode, disb.Category, disb.Month);
            if (line != null)
            {
                line.CommittedAmount = Math.Max(0m, line.CommittedAmount - disb.Amount);
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        disb.Status = "Rejected";
        disb.RejectionReason = reason;
        await _context.SaveChangesAsync(default);
        return MapToDisbursementDto(disb);
    }

    public async Task<DirectDisbursementResponseDto> CancelDirectDisbursementAsync(int disbursementId, string reason, string userId, string userRole)
    {
        var disb = await _context.DirectDisbursements.FirstOrDefaultAsync(d => d.DisbursementId == disbursementId);
        if (disb == null) throw new KeyNotFoundException($"Direct disbursement {disbursementId} not found.");

        if (disb.Status == "Approved")
        {
            var line = await GetBudgetLineAsync(disb.FiscalYear, disb.DepartmentCode, disb.Category, disb.Month);
            if (line != null)
            {
                line.CommittedAmount = Math.Max(0m, line.CommittedAmount - disb.Amount);
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        disb.Status = "Cancelled";
        disb.RejectionReason = reason;
        await _context.SaveChangesAsync(default);
        return MapToDisbursementDto(disb);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. REIMBURSEMENT LIFECYCLE (Submitted -> Validated -> Approved -> Paid)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<ReimbursementResponseDto> SubmitReimbursementAsync(SubmitReimbursementDto dto, string userId, string userRole)
    {
        EnsureValidCategory(dto.Category);
        dto.DepartmentCode = FomsDepartments.Normalize(dto.DepartmentCode, userRole, isNewRequest: false);

        var refNo = $"REIM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var reim = new Reimbursement
        {
            ReferenceNumber = refNo,
            EmployeeId = !string.IsNullOrWhiteSpace(dto.EmployeeId) ? dto.EmployeeId : userId,
            EmployeeName = dto.EmployeeName,
            DepartmentCode = dto.DepartmentCode,
            Category = dto.Category,
            Amount = dto.Amount,
            Purpose = dto.Purpose,
            ReceiptUrls = dto.ReceiptUrls,
            Status = "Submitted",
            SubmittedAt = DateTime.UtcNow,
            FiscalYear = dto.FiscalYear,
            Month = dto.Month
        };

        _context.Reimbursements.Add(reim);
        await _context.SaveChangesAsync(default);

        return MapToReimbursementDto(reim);
    }

    public async Task<ReimbursementResponseDto> ValidateReimbursementAsync(int reimbursementId, string userId, string userRole)
    {
        // Assistant of Finance Manager validates
        if (!IsAssistantFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Reimbursement must be validated by Assistant of Finance Manager.");
        }

        var reim = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == reimbursementId);
        if (reim == null) throw new KeyNotFoundException($"Reimbursement {reimbursementId} not found.");

        if (reim.Status != "Submitted")
        {
            throw new InvalidOperationException($"Cannot validate reimbursement in status '{reim.Status}'. Must be 'Submitted'.");
        }

        if (!string.IsNullOrEmpty(reim.EmployeeId) && FomsRoles.IsSameEmployeeId(reim.EmployeeId, userId))
        {
            throw new UnauthorizedAccessException("Separation of duties violation: Submitter cannot validate their own reimbursement.");
        }

        reim.Status = "Validated";
        reim.ValidatedBy = userId;
        reim.ValidatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);
        return MapToReimbursementDto(reim);
    }

    public async Task<ReimbursementResponseDto> ApproveReimbursementAsync(int reimbursementId, string userId, string userRole)
    {
        // Finance Manager approves = COMMITTED
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only Finance Manager can approve reimbursements.");
        }

        var reim = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == reimbursementId);
        if (reim == null) throw new KeyNotFoundException($"Reimbursement {reimbursementId} not found.");

        if (reim.Status != "Validated")
        {
            throw new InvalidOperationException($"Cannot approve reimbursement in status '{reim.Status}'. Must be 'Validated'.");
        }

        if (!string.IsNullOrEmpty(reim.EmployeeId) && FomsRoles.IsSameEmployeeId(reim.EmployeeId, userId))
        {
            throw new UnauthorizedAccessException("Separation of duties violation: Submitter cannot approve their own reimbursement.");
        }

        // Gate check for Reimbursement approval above CSH-09 threshold (Decision E: unset threshold mandates CEO approval)
        await _gateService.EnsureGateSatisfiedAsync(
            CeoApprovalItemTypes.Reimbursement,
            reimbursementId.ToString(),
            reim.Amount,
            reim.SubmittedAt);

        // Commit budget amount
        var line = await GetBudgetLineAsync(reim.FiscalYear, reim.DepartmentCode, reim.Category, reim.Month);
        if (line != null)
        {
            line.CommittedAmount += reim.Amount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        reim.Status = "Approved";
        reim.ApprovedBy = userId;
        reim.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);
        return MapToReimbursementDto(reim);
    }

    public async Task<ReimbursementResponseDto> PayReimbursementAsync(int reimbursementId, string userId, string userRole)
    {
        // Paid = ACTUAL
        var reim = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == reimbursementId);
        if (reim == null) throw new KeyNotFoundException($"Reimbursement {reimbursementId} not found.");

        if (reim.Status != "Approved")
        {
            throw new InvalidOperationException($"Cannot pay reimbursement in status '{reim.Status}'. Must be 'Approved'.");
        }

        // Gate check for Reimbursement payment outflow above CSH-09 threshold (Decision E: unset threshold mandates CEO approval)
        await _gateService.EnsureGateSatisfiedAsync(
            CeoApprovalItemTypes.CashOutflow,
            reim.ReferenceNumber,
            reim.Amount,
            reim.SubmittedAt);

        // Convert Committed -> Actual
        var line = await GetBudgetLineAsync(reim.FiscalYear, reim.DepartmentCode, reim.Category, reim.Month);
        if (line != null)
        {
            line.CommittedAmount = Math.Max(0m, line.CommittedAmount - reim.Amount);
            line.ActualAmount += reim.Amount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        reim.Status = "Paid";
        reim.PaidBy = userId;
        reim.PaidAt = DateTime.UtcNow;

        // Outflow
        var outflow = new CashOutflow
        {
            Amount = reim.Amount,
            Category = reim.Category,
            SourceReference = reim.ReferenceNumber,
            Description = $"Reimbursement for {reim.EmployeeName}: {reim.Purpose}",
            Status = "Completed",
            OutflowDate = DateTime.UtcNow,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            RecordedBy = userId
        };
        _context.CashOutflows.Add(outflow);

        await _context.SaveChangesAsync(default);
        return MapToReimbursementDto(reim);
    }

    public async Task<ReimbursementResponseDto> RejectReimbursementAsync(int reimbursementId, string reason, string userId, string userRole)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Rejection reason is required.");

        var reim = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == reimbursementId);
        if (reim == null) throw new KeyNotFoundException($"Reimbursement {reimbursementId} not found.");

        // If it was already Approved (Committed), release the commitment
        if (reim.Status == "Approved")
        {
            var line = await GetBudgetLineAsync(reim.FiscalYear, reim.DepartmentCode, reim.Category, reim.Month);
            if (line != null)
            {
                line.CommittedAmount = Math.Max(0m, line.CommittedAmount - reim.Amount);
                line.UpdatedAt = DateTime.UtcNow;
            }
        }

        reim.Status = "Rejected";
        reim.RejectionReason = reason.Trim();
        await _context.SaveChangesAsync(default);
        return MapToReimbursementDto(reim);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. LIQUIDATION LIFECYCLE (Pending / Validated / Rejected / For Correction)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<LiquidationResponseDto> SubmitLiquidationAsync(SubmitLiquidationDto dto, string userId, string userRole)
    {
        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == dto.AdvanceId);
        if (advance == null) throw new KeyNotFoundException($"Cash advance {dto.AdvanceId} not found.");

        if (advance.Status != "Released")
        {
            throw new InvalidOperationException($"Cannot liquidate advance in status '{advance.Status}'. Advance must be 'Released'.");
        }

        // Validate line items have receipts attached & valid category
        foreach (var item in dto.LineItems)
        {
            EnsureValidCategory(item.Category);
            if (!item.ReceiptAttached)
            {
                throw new ArgumentException($"Line item '{item.Description}' must have an original receipt attached.");
            }
        }

        decimal totalExpenses = dto.LineItems.Sum(i => i.Amount);
        decimal leftoverCash = 0m;
        decimal reimbursementAmount = 0m;

        if (advance.AmountGiven >= totalExpenses)
        {
            leftoverCash = advance.AmountGiven - totalExpenses;
        }
        else
        {
            reimbursementAmount = totalExpenses - advance.AmountGiven;
        }

        var refNo = $"LIQ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var liq = new Liquidation
        {
            ReferenceNumber = refNo,
            SubmittedByName = dto.SubmittedByName,
            SubmittedByEmployeeId = (!string.IsNullOrWhiteSpace(dto.SubmittedByName) && dto.SubmittedByName.Trim().StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
                ? dto.SubmittedByName.Trim()
                : ((advance.CourierName ?? "").StartsWith("EMP-", StringComparison.OrdinalIgnoreCase) ? advance.CourierName : null),
            TotalAmount = totalExpenses,
            LeftoverCash = leftoverCash,
            ReimbursementAmount = reimbursementAmount,
            Status = "Pending",
            DateSubmitted = DateTime.UtcNow,
            DueDate = advance.DueDate,
            SupervisorPreApproved = dto.SupervisorPreApproved,
            DepartmentCode = advance.DepartmentCode,
            Category = advance.Category,
            FiscalYear = advance.FiscalYear,
            Month = advance.Month,
            Remarks = dto.Remarks,
            CreatedAt = DateTime.UtcNow
        };

        _context.Liquidations.Add(liq);
        await _context.SaveChangesAsync(default);

        foreach (var item in dto.LineItems)
        {
            var lineItem = new LiquidationLineItem
            {
                LiquidationId = liq.LiquidationId,
                Category = item.Category,
                Description = item.Description,
                Amount = item.Amount,
                ReceiptAttached = item.ReceiptAttached,
                ReceiptDate = item.ReceiptDate ?? DateTime.UtcNow,
                ReceiptNumber = item.ReceiptNumber,
                IsManualFuelReceipt = item.IsManualFuelReceipt,
                CreatedAt = DateTime.UtcNow
            };
            _context.LiquidationLineItems.Add(lineItem);
            liq.LineItems.Add(lineItem);
        }

        await _context.SaveChangesAsync(default);

        return MapToLiquidationDto(liq, advance.AmountGiven);
    }

    public async Task<LiquidationResponseDto> ValidateLiquidationAsync(ValidateLiquidationDto dto, string userId, string userRole)
    {
        var liq = await _context.Liquidations
            .Include(l => l.LineItems)
            .FirstOrDefaultAsync(l => l.LiquidationId == dto.LiquidationId);

        if (liq == null) throw new KeyNotFoundException($"Liquidation {dto.LiquidationId} not found.");

        if (liq.Status != "Pending" && liq.Status != "For Correction")
        {
            throw new InvalidOperationException($"Cannot validate liquidation in status '{liq.Status}'. Must be 'Pending' or 'For Correction'.");
        }

        // SoD check: Submitter cannot validate their own liquidation
        if ((!string.IsNullOrEmpty(liq.SubmittedByEmployeeId) && FomsRoles.IsSameEmployeeId(liq.SubmittedByEmployeeId, userId)) ||
            (!string.IsNullOrEmpty(liq.SubmittedByName) && (string.Equals(liq.SubmittedByName, userId, StringComparison.OrdinalIgnoreCase) || string.Equals(liq.SubmittedByName, "EMP-" + userId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UnauthorizedAccessException("Separation of duties violation: Submitter cannot validate their own liquidation.");
        }

        // Assistant of Finance Manager validates alone at or below threshold. Above threshold, Finance Manager also signs off.
        // Validated by Assistant of Finance Manager (or Finance Manager)
        if (!IsAssistantFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Liquidation must be validated by Assistant of Finance Manager.");
        }

        // Check for manual fuel receipt rejection requirement
        if (liq.LineItems.Any(i => i.IsManualFuelReceipt))
        {
            throw new InvalidOperationException("Liquidation contains manual fuel receipt which is disallowed. Reject or mark for correction.");
        }

        // Find associated cash advance
        var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.TripReference == liq.ReferenceNumber || a.AdvanceId == liq.ReferenceNumber || a.DepartmentCode == liq.DepartmentCode);
        if (advance == null)
        {
            advance = await _context.CashAdvances.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        }

        decimal advanceAmount = advance?.AmountGiven ?? liq.TotalAmount;

        // Overspend check: Overspend reimbursed ONLY with supervisor pre-approval and original receipt
        if (liq.ReimbursementAmount > 0 && !liq.SupervisorPreApproved)
        {
            throw new InvalidOperationException(
                $"Overspend of ₱{liq.ReimbursementAmount:N2} cannot be validated without recorded supervisor pre-approval and original receipts.");
        }

        // 1. Convert Committed to Actual and release unused commitment
        var line = await GetBudgetLineAsync(liq.FiscalYear, liq.DepartmentCode, liq.Category, liq.Month);
        if (line != null)
        {
            // Release advance commitment
            line.CommittedAmount = Math.Max(0m, line.CommittedAmount - advanceAmount);
            // Add validated actual expense
            line.ActualAmount += liq.TotalAmount;
            line.UpdatedAt = DateTime.UtcNow;
        }

        // 2. Leftover cash returned = Cash Inflow
        if (liq.LeftoverCash > 0)
        {
            var inflow = new CashInflow
            {
                Amount = liq.LeftoverCash.Value,
                Source = $"Liquidation {liq.ReferenceNumber} Cash Return",
                ReferenceNumber = liq.ReferenceNumber,
                Status = "Completed",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _context.CashInflows.Add(inflow);
        }

        liq.Status = "Validated";
        liq.ValidatedBy = 4; // Assistant of Finance Manager ID
        liq.ValidatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Remarks))
        {
            liq.Remarks = dto.Remarks;
        }

        if (advance != null)
        {
            advance.Status = "Liquidated";
        }

        await _context.SaveChangesAsync(default);

        return MapToLiquidationDto(liq, advanceAmount);
    }

    public async Task<LiquidationResponseDto> RejectLiquidationAsync(RejectLiquidationDto dto, string userId, string userRole)
    {
        if (!IsAssistantFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Liquidation rejection must be performed by Assistant of Finance Manager (or FM).");
        }

        if (string.IsNullOrWhiteSpace(dto.Reason) || !LiquidationRejectionReasons.IsValid(dto.Reason))
        {
            throw new ArgumentException("Rejection reason is required. Allowed options: wrong receipt date or amount, manual fuel receipt, late submission, other with remarks.");
        }

        if (dto.Reason.Trim().ToLowerInvariant().Contains("other") && string.IsNullOrWhiteSpace(dto.Remarks))
        {
            throw new ArgumentException("Remarks are required when rejection reason is 'other with remarks'.");
        }

        var liq = await _context.Liquidations
            .Include(l => l.LineItems)
            .FirstOrDefaultAsync(l => l.LiquidationId == dto.LiquidationId);

        if (liq == null) throw new KeyNotFoundException($"Liquidation {dto.LiquidationId} not found.");

        liq.Status = !string.IsNullOrWhiteSpace(dto.Status) ? dto.Status : "Rejected";
        liq.RejectionReason = dto.Reason.Trim();
        liq.Remarks = dto.Remarks;
        liq.ValidatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToLiquidationDto(liq, liq.TotalAmount);
    }

    public async Task<LiquidationResponseDto> MarkForCorrectionLiquidationAsync(int liquidationId, string remarks, string userId, string userRole)
    {
        if (!IsAssistantFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Marking for correction must be performed by Assistant of Finance Manager.");
        }

        var liq = await _context.Liquidations
            .Include(l => l.LineItems)
            .FirstOrDefaultAsync(l => l.LiquidationId == liquidationId);

        if (liq == null) throw new KeyNotFoundException($"Liquidation {liquidationId} not found.");

        liq.Status = "For Correction";
        liq.Remarks = remarks;
        liq.ValidatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(default);

        return MapToLiquidationDto(liq, liq.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Mappings
    // ─────────────────────────────────────────────────────────────────────────
    private CashAdvanceResponseDto MapToAdvanceDto(CashAdvance a)
    {
        return new CashAdvanceResponseDto
        {
            AdvanceId = a.AdvanceId,
            CourierName = a.CourierName ?? string.Empty,
            TripReference = a.TripReference ?? string.Empty,
            AmountGiven = a.AmountGiven,
            DepartmentCode = a.DepartmentCode,
            Category = a.Category,
            LocationType = a.LocationType,
            DueDate = a.DueDate,
            ReturnDate = a.ReturnDate,
            IsLate = a.IsLate,
            Status = a.Status,
            RequiresCeoApproval = a.RequiresCeoApproval,
            ApprovedBy = a.ApprovedBy,
            ApprovedAt = a.ApprovedAt,
            ReleasedBy = a.ReleasedBy,
            ReleaseDate = a.ReleaseDate,
            RejectionReason = a.RejectionReason,
            Notes = a.Notes,
            CreatedAt = a.CreatedAt
        };
    }

    private DirectDisbursementResponseDto MapToDisbursementDto(DirectDisbursement d)
    {
        return new DirectDisbursementResponseDto
        {
            DisbursementId = d.DisbursementId,
            ReferenceNumber = d.ReferenceNumber,
            DepartmentCode = d.DepartmentCode,
            Category = d.Category,
            Amount = d.Amount,
            Purpose = d.Purpose,
            Status = d.Status,
            RequestedBy = d.RequestedBy,
            RequestedAt = d.RequestedAt,
            ApprovedBy = d.ApprovedBy,
            ApprovedAt = d.ApprovedAt,
            ReleasedBy = d.ReleasedBy,
            ReleasedAt = d.ReleasedAt,
            RejectionReason = d.RejectionReason,
            FiscalYear = d.FiscalYear,
            Month = d.Month
        };
    }

    private ReimbursementResponseDto MapToReimbursementDto(Reimbursement r)
    {
        return new ReimbursementResponseDto
        {
            ReimbursementId = r.ReimbursementId,
            ReferenceNumber = r.ReferenceNumber,
            EmployeeId = r.EmployeeId,
            EmployeeName = r.EmployeeName,
            DepartmentCode = r.DepartmentCode,
            Category = r.Category,
            Amount = r.Amount,
            Purpose = r.Purpose,
            Status = r.Status,
            SubmittedAt = r.SubmittedAt,
            ValidatedBy = r.ValidatedBy,
            ValidatedAt = r.ValidatedAt,
            ApprovedBy = r.ApprovedBy,
            ApprovedAt = r.ApprovedAt,
            PaidBy = r.PaidBy,
            PaidAt = r.PaidAt,
            RejectionReason = r.RejectionReason
        };
    }

    private LiquidationResponseDto MapToLiquidationDto(Liquidation l, decimal advanceAmount)
    {
        return new LiquidationResponseDto
        {
            LiquidationId = l.LiquidationId,
            ReferenceNumber = l.ReferenceNumber ?? string.Empty,
            CashAdvanceId = l.CashAdvanceId?.ToString() ?? string.Empty,
            SubmittedByName = l.SubmittedByName ?? string.Empty,
            TotalAmount = l.TotalAmount,
            AdvanceAmount = advanceAmount,
            LeftoverCash = l.LeftoverCash ?? 0m,
            ReimbursementAmount = l.ReimbursementAmount ?? 0m,
            Status = l.Status ?? "Pending",
            DateSubmitted = l.DateSubmitted,
            DueDate = l.DueDate,
            IsLate = l.IsLate,
            ValidatedBy = l.ValidatedBy?.ToString(),
            ValidatedAt = l.ValidatedAt,
            RejectionReason = l.RejectionReason,
            Remarks = l.Remarks,
            SupervisorPreApproved = l.SupervisorPreApproved,
            LineItems = l.LineItems.Select(i => new LiquidationLineItemDto
            {
                LineItemId = i.LineItemId,
                Category = i.Category,
                Description = i.Description,
                Amount = i.Amount,
                ReceiptAttached = i.ReceiptAttached,
                ReceiptDate = i.ReceiptDate,
                ReceiptNumber = i.ReceiptNumber,
                IsManualFuelReceipt = i.IsManualFuelReceipt
            }).ToList()
        };
    }
}
