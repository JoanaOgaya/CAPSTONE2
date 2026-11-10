using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class LiquidationDto
{
    public string? Id { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? SubmittedByName { get; set; }
    public string? SubmittedBy { get; set; }
    public string? SubmittedByEmployeeId { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? Amount { get; set; }
    public decimal? LeftoverCash { get; set; }
    public decimal? ReimbursementAmount { get; set; }
    public string? Status { get; set; }
    public string? Purpose { get; set; }
    public string? Notes { get; set; }
}

[Authorize]
[ApiController]
[Route("api/liquidations")]
[Route("api/v1/liquidations")]
[Route("liquidations")]
public class LiquidationsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public LiquidationsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (bool isAuthenticated, string role, string username, string empId) GetCallerInfo()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return (false, string.Empty, string.Empty, string.Empty);
        }

        var role = FomsRoles.GetClaimRole(User);
        var username = FomsRoles.GetClaimUsername(User);
        var empId = FomsRoles.GetClaimEmployeeId(User);
        return (true, role, username, empId);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AllCanonicalRoles))
            return StatusCode(403, new { message = "Forbidden: Missing or unrecognized role claim." });

        var query = _fomsDb.Liquidations.AsNoTracking().Where(l => l.IsDeleted != true && l.Status != "Deleted");
        var total = await query.CountAsync();
        var liquidations = await query
            .OrderByDescending(l => l.DateSubmitted ?? l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var liqIds = liquidations.Select(l => l.LiquidationId).ToList();
        var lineItems = await _fomsDb.LiquidationLineItems.AsNoTracking()
            .Where(li => liqIds.Contains(li.LiquidationId))
            .ToListAsync();
        var docs = await _fomsDb.LiquidationDocuments.AsNoTracking()
            .Where(d => liqIds.Contains(d.LiquidationId))
            .ToListAsync();

        var items = liquidations.Select(l => new
        {
            id = l.LiquidationId.ToString(),
            liquidationId = l.LiquidationId,
            reference = l.ReferenceNumber ?? $"LIQ-{l.LiquidationId:D4}",
            referenceNumber = l.ReferenceNumber ?? $"LIQ-{l.LiquidationId:D4}",
            submittedBy = l.SubmittedByName ?? $"Employee {l.SubmittedBy}",
            submittedByName = l.SubmittedByName ?? $"Employee {l.SubmittedBy}",
            submittedByEmployeeId = l.SubmittedByEmployeeId,
            submittedAt = (l.DateSubmitted ?? l.CreatedAt ?? DateTime.UtcNow).ToString("o"),
            dateSubmitted = (l.DateSubmitted ?? l.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            amount = l.TotalAmount,
            totalAmount = l.TotalAmount,
            status = l.Status ?? "Pending Validation",
            leftoverCash = l.LeftoverCash ?? 0m,
            reimbursementAmount = l.ReimbursementAmount ?? 0m,
            expenses = lineItems.Where(li => li.LiquidationId == l.LiquidationId).Select(li => new
            {
                id = li.LineItemId.ToString(),
                type = "Expense",
                description = li.Description,
                amount = li.Amount
            }).ToList(),
            documents = docs.Where(d => d.LiquidationId == l.LiquidationId).Select(d => new
            {
                name = Path.GetFileName(d.FilePath ?? "receipt.pdf"),
                url = d.FilePath ?? "#",
                size = "1.2 MB"
            }).ToList()
        }).ToList();

        return Ok(new
        {
            items,
            total,
            page,
            pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AllCanonicalRoles))
            return StatusCode(403, new { message = "Forbidden: Missing or unrecognized role claim." });

        var item = await _fomsDb.Liquidations.AsNoTracking().FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        var lineItems = await _fomsDb.LiquidationLineItems.Where(li => li.LiquidationId == id).AsNoTracking().ToListAsync();
        var docs = await _fomsDb.LiquidationDocuments.Where(d => d.LiquidationId == id).AsNoTracking().ToListAsync();

        return Ok(new
        {
            id = item.LiquidationId.ToString(),
            item.LiquidationId,
            reference = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId:D4}",
            referenceNumber = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId:D4}",
            submittedBy = item.SubmittedByName ?? $"Employee {item.SubmittedBy}",
            submittedByName = item.SubmittedByName ?? $"Employee {item.SubmittedBy}",
            submittedByEmployeeId = item.SubmittedByEmployeeId,
            amount = item.TotalAmount,
            totalAmount = item.TotalAmount,
            leftoverCash = item.LeftoverCash ?? 0m,
            reimbursementAmount = item.ReimbursementAmount ?? 0m,
            status = item.Status ?? "Pending Validation",
            dateSubmitted = (item.DateSubmitted ?? item.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            lineItems,
            expenses = lineItems.Select(li => new
            {
                id = li.LineItemId.ToString(),
                type = "Expense",
                description = li.Description,
                amount = li.Amount
            }).ToList(),
            documents = docs.Select(d => new
            {
                name = Path.GetFileName(d.FilePath ?? "receipt.pdf"),
                url = d.FilePath ?? "#",
                size = "1.2 MB"
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LiquidationDto dto)
    {
        var (isAuthenticated, role, username, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to create liquidation vouchers. Only Assistant of Finance Manager (EMP-004) or Head Accountant (EMP-002) may initiate liquidations." });
        }

        var item = new Liquidation
        {
            ReferenceNumber = dto.ReferenceNumber ?? $"LIQ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            SubmittedByName = dto.SubmittedByName ?? dto.SubmittedBy ?? (!string.IsNullOrWhiteSpace(username) ? username : "Employee"),
            SubmittedByEmployeeId = !string.IsNullOrWhiteSpace(empId) ? empId : dto.SubmittedByEmployeeId,
            TotalAmount = dto.TotalAmount ?? dto.Amount ?? 0m,
            LeftoverCash = dto.LeftoverCash ?? 0m,
            ReimbursementAmount = dto.ReimbursementAmount ?? 0m,
            Status = dto.Status ?? "Pending Validation",
            CreatedAt = DateTime.UtcNow,
            DateSubmitted = DateTime.UtcNow,
            IsDeleted = false
        };

        _fomsDb.Liquidations.Add(item);
        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = "CREATE_LIQUIDATION",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Submitted liquidation {item.ReferenceNumber} for {item.SubmittedByName} (EmpId: {item.SubmittedByEmployeeId}): ₱{item.TotalAmount:N2}",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = item.LiquidationId }, item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] LiquidationDto dto)
    {
        var (isAuthenticated, role, _, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to update or validate liquidations." });
        }

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        if (!string.IsNullOrEmpty(dto.Status) &&
            (dto.Status.Equals("Validated", StringComparison.OrdinalIgnoreCase) ||
             dto.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
             dto.Status.Equals("Endorsed", StringComparison.OrdinalIgnoreCase)))
        {
            var submitterEmpId = !string.IsNullOrWhiteSpace(item.SubmittedByEmployeeId)
                ? item.SubmittedByEmployeeId
                : item.SubmittedByName;
            var (_, currentRole, currentUsername, currentEmpId) = GetCallerInfo();
            if (FomsRoles.IsSameEmployeeId(submitterEmpId, currentEmpId) ||
                (!string.IsNullOrWhiteSpace(item.SubmittedByName) && (item.SubmittedByName.Equals(currentUsername, StringComparison.OrdinalIgnoreCase) || item.SubmittedByName.Equals(currentEmpId, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(item.SubmittedByEmployeeId) && item.SubmittedByEmployeeId.Equals(currentEmpId, StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot validate, endorse, or approve their own liquidation." });
            }
        }

        if (!string.IsNullOrEmpty(dto.Status) &&
            (dto.Status.Equals("Validated", StringComparison.OrdinalIgnoreCase) ||
             dto.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)))
        {
            if (FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager) && !FomsRoles.IsRole(role, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
            {
                return StatusCode(403, new { message = "Forbidden: Assistant of Finance Manager cannot perform final sign-off. Strict two-tier endorsement requires endorsement to Finance Manager." });
            }
            if (FomsRoles.IsRole(role, FomsRoles.FinanceManager) && !string.Equals(item.Status, "Endorsed", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Skipped tier: Liquidation must be endorsed by Assistant of Finance Manager before final Finance Manager sign-off." });
            }
        }

        if (!string.IsNullOrEmpty(dto.Status))
            item.Status = dto.Status;

        if (dto.TotalAmount.HasValue || dto.Amount.HasValue)
            item.TotalAmount = dto.TotalAmount ?? dto.Amount!.Value;

        if (dto.LeftoverCash.HasValue)
            item.LeftoverCash = dto.LeftoverCash.Value;

        if (dto.ReimbursementAmount.HasValue)
            item.ReimbursementAmount = dto.ReimbursementAmount.Value;

        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = "UPDATE_LIQUIDATION",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Updated liquidation {item.ReferenceNumber} -> status: {item.Status}. Remarks: {dto.Notes ?? "Updated"}",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return Ok(item);
    }

    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> Act(int id, string actionName, [FromBody] LiquidationDto? dto)
    {
        var (isAuthenticated, role, _, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to perform liquidation action '{actionName}'." });
        }

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        var normAction = actionName.ToLowerInvariant();
        var (_, userRole, callerUsername, callerEmpId) = GetCallerInfo();

        // Separation of duties / self-endorsement guard
        if (normAction is "approve" or "validate" or "endorse" or "reject" or "sign-off" or "signoff")
        {
            var submitterEmpId = !string.IsNullOrWhiteSpace(item.SubmittedByEmployeeId)
                ? item.SubmittedByEmployeeId
                : item.SubmittedByName;
            if (FomsRoles.IsSameEmployeeId(submitterEmpId, callerEmpId) ||
                (!string.IsNullOrWhiteSpace(item.SubmittedByName) && (item.SubmittedByName.Equals(callerUsername, StringComparison.OrdinalIgnoreCase) || item.SubmittedByName.Equals(callerEmpId, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(item.SubmittedByEmployeeId) && item.SubmittedByEmployeeId.Equals(callerEmpId, StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot validate, endorse, or approve their own liquidation." });
            }
        }

        if (normAction == "endorse")
        {
            if (string.Equals(item.Status, "Endorsed", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = "This liquidation has already been endorsed." });
            }
            if (string.Equals(item.Status, "Validated", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = "This liquidation has already been validated and signed off." });
            }

            item.Status = "Endorsed";

            _fomsDb.Notifications.Add(new Notification
            {
                RecipientRole = "Finance Manager",
                Title = "Liquidation Endorsed for Final Sign-Off",
                Message = $"Liquidation {item.ReferenceNumber} endorsed by Assistant FM.",
                Type = "Liquidation_Approval",
                RelatedRecordType = "Liquidation",
                RelatedRecordId = id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (normAction is "approve" or "validate" or "sign-off" or "signoff")
        {
            // Tier 1 Assistant FM CANNOT perform final sign-off
            if (FomsRoles.IsRole(userRole, FomsRoles.AssistantFinanceManager) && !FomsRoles.IsRole(userRole, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
            {
                return StatusCode(403, new { message = "Forbidden: Assistant of Finance Manager cannot perform final sign-off. Strict two-tier endorsement requires endorsement to Finance Manager." });
            }

            // TC-015 / Idempotency: Duplicate click prevention
            if (string.Equals(item.Status, "Validated", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = "This liquidation has already been signed off and validated." });
            }

            // Skipped Tier: Finance Manager cannot sign off without prior endorsement by Tier 1 Assistant FM
            if (FomsRoles.IsRole(userRole, FomsRoles.FinanceManager) && !string.Equals(item.Status, "Endorsed", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Skipped tier: Liquidation must be endorsed by Assistant of Finance Manager before final Finance Manager sign-off." });
            }

            item.Status = "Validated";
            item.ValidatedAt = DateTime.UtcNow;
            item.ValidatedBy = 1;

            // Cash Inflow recognized only upon final FM sign-off (once only)
            if ((item.LeftoverCash ?? 0m) > 0m)
            {
                var existingInflow = await _fomsDb.CashInflows.FirstOrDefaultAsync(i => i.ReferenceNumber == item.ReferenceNumber);
                if (existingInflow == null)
                {
                    _fomsDb.CashInflows.Add(new CashInflow
                    {
                        Amount = item.LeftoverCash!.Value,
                        Source = $"Liquidation {item.ReferenceNumber} Cash Return",
                        ReferenceNumber = item.ReferenceNumber,
                        Status = "Completed",
                        TransactionDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Cash Outflow recognized only upon final FM sign-off (once only)
            if ((item.ReimbursementAmount ?? 0m) > 0m)
            {
                var existingOutflow = await _fomsDb.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == item.ReferenceNumber);
                if (existingOutflow == null)
                {
                    _fomsDb.CashOutflows.Add(new CashOutflow
                    {
                        CashOutflowId = $"CO-LIQ-{item.LiquidationId}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
                        SourceReference = item.ReferenceNumber,
                        RecordedBy = !string.IsNullOrWhiteSpace(callerUsername) ? callerUsername : "Finance Manager",
                        Category = "Courier Expense Reimbursement",
                        Description = $"Liquidation {item.ReferenceNumber} Reimbursement for {item.SubmittedByName ?? "Courier"}",
                        Amount = item.ReimbursementAmount!.Value,
                        Status = "Completed",
                        ApprovalLevel = "Level 2",
                        AssignedApprover = "Finance Manager",
                        OutflowDate = DateTime.UtcNow,
                        TransactionDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Update matching Cash Advance to Liquidated
            if (item.CashAdvanceId.HasValue)
            {
                var adv = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == item.CashAdvanceId.Value.ToString() || a.AdvanceId == $"CA-{item.CashAdvanceId}");
                if (adv != null)
                {
                    adv.Status = "Liquidated";
                }
            }
            else
            {
                var adv = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.CourierName == item.SubmittedByName && a.Status != "Liquidated");
                if (adv != null)
                {
                    adv.Status = "Liquidated";
                }
            }
        }
        else if (normAction is "reject")
        {
            item.Status = "Rejected";
        }
        else if (normAction is "return" or "for_correction" or "for-correction")
        {
            item.Status = "Returned";
        }
        else if (normAction is "submit")
        {
            item.Status = "Pending Validation";
        }

        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = $"LIQUIDATION_{actionName.ToUpper()}",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Liquidation {item.ReferenceNumber} action {actionName} -> {item.Status}. Leftover: ₱{item.LeftoverCash:N2}, Reimbursement: ₱{item.ReimbursementAmount:N2}. Remarks: {dto?.Notes ?? "Processed per FR-048-053"}",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to delete liquidations." });

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        item.IsDeleted = true;
        item.Status = "Deleted";
        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = "DELETE_LIQUIDATION",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Soft-deleted liquidation {item.ReferenceNumber}.",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return Ok(new { success = true, message = "Liquidation soft-deleted successfully." });
    }
}
