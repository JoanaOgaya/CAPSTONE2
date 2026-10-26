using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class CreateExecutiveApprovalDto
{
    public string ApproverTitle { get; set; } = "CEO"; // 'CEO' or 'President'
    public string ApproverName { get; set; } = string.Empty;
    public string ApprovalDate { get; set; } = string.Empty; // YYYY-MM-DD
    public string Decision { get; set; } = "Approved"; // 'Approved' or 'Rejected'
    public decimal ApprovedAmount { get; set; }
    public decimal TransactionAmount { get; set; } // Must be covered by ApprovedAmount
    public string TransactionType { get; set; } = string.Empty; // 'Discount', 'LargeCashOutflow', 'BudgetOverride', 'BudgetTransfer', 'BudgetCarryOver'
    public string TransactionReference { get; set; } = string.Empty;
    public string ProofAttachmentUrl { get; set; } = string.Empty;
    public bool IsDelegated { get; set; } = false;
    public string? DelegateJustification { get; set; }
    public string? RequestedBy { get; set; }
    public string? ValidatedBy { get; set; }
}

public class ConfigureDelegateDto
{
    public string DelegateUserId { get; set; } = string.Empty;
    public string DelegateName { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public string Reason { get; set; } = string.Empty;
}

[Authorize]
[ApiController]
[Route("api/executive-approvals")]
public class ExecutiveApprovalsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public ExecutiveApprovalsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string userId) GetCurrentUser()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "FM-USER";
        return (role, userId);
    }

    /// <summary>
    /// GET /api/executive-approvals — List executive approvals.
    /// Head Accountant and Financial Manager can view (AUD-03).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? reference, [FromQuery] string? transactionType)
    {
        var (role, _) = GetCurrentUser();

        // Scoped audit visibility: Only Financial Manager and Head Accountant can view executive approvals
        if (!string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "HEAD_ACCOUNTANT", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid("Access denied. Only the Financial Manager and Head Accountant may view executive approvals.");
        }

        var query = _fomsDb.ExecutiveApprovalRecords.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(reference))
        {
            query = query.Where(r => r.TransactionReference == reference);
        }
        if (!string.IsNullOrWhiteSpace(transactionType))
        {
            query = query.Where(r => r.TransactionType == transactionType);
        }

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(list);
    }

    /// <summary>
    /// POST /api/executive-approvals/delegates — Configure a delegate for the Financial Manager.
    /// Only Financial Manager can configure delegates (Item #5).
    /// </summary>
    [HttpPost("delegates")]
    public async Task<IActionResult> ConfigureDelegate([FromBody] ConfigureDelegateDto dto)
    {
        var (role, userId) = GetCurrentUser();
        bool isFm = string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase);

        if (!isFm)
        {
            return Forbid("Only the Financial Manager may configure delegates.");
        }

        if (string.IsNullOrWhiteSpace(dto.DelegateUserId) || string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "DelegateUserId and Reason are mandatory." });
        }

        if (dto.ValidUntil <= dto.ValidFrom)
        {
            return BadRequest(new { message = "ValidUntil must be after ValidFrom." });
        }

        var delegateRecord = new FinanceManagerDelegate
        {
            DelegateUserId = dto.DelegateUserId.Trim(),
            DelegateName = string.IsNullOrWhiteSpace(dto.DelegateName) ? dto.DelegateUserId.Trim() : dto.DelegateName.Trim(),
            AssignedBy = userId,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            Reason = dto.Reason.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _fomsDb.FinanceManagerDelegates.Add(delegateRecord);

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = role,
            action = "Configure FM Delegate",
            module = "ExecutiveApprovals",
            affected_record_type = "FinanceManagerDelegate",
            before_value = null,
            after_value = JsonSerializer.Serialize(new { delegateRecord.DelegateUserId, delegateRecord.DelegateName, delegateRecord.ValidFrom, delegateRecord.ValidUntil, delegateRecord.Reason }),
            remarks = $"Financial Manager '{userId}' delegated authority to '{delegateRecord.DelegateUserId}' ({delegateRecord.DelegateName}) from {delegateRecord.ValidFrom:yyyy-MM-dd} to {delegateRecord.ValidUntil:yyyy-MM-dd}. Reason: {delegateRecord.Reason}.",
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(new { success = true, message = "Delegate configured successfully.", delegateId = delegateRecord.DelegateId });
    }

    /// <summary>
    /// PUT /api/executive-approvals/delegates/{delegateId}/revoke — Revoke a delegate configuration.
    /// Only Financial Manager can revoke delegates (Q9).
    /// </summary>
    [HttpPut("delegates/{delegateId}/revoke")]
    public async Task<IActionResult> RevokeDelegate(int delegateId)
    {
        var (role, userId) = GetCurrentUser();
        bool isFm = string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase);

        if (!isFm)
        {
            return Forbid("Only the Financial Manager may revoke delegates.");
        }

        var delegateRecord = await _fomsDb.FinanceManagerDelegates.FirstOrDefaultAsync(d => d.DelegateId == delegateId);
        if (delegateRecord == null)
        {
            return NotFound(new { message = $"Delegate record {delegateId} not found." });
        }

        var beforeVal = JsonSerializer.Serialize(new { delegateRecord.DelegateId, delegateRecord.DelegateUserId, delegateRecord.IsActive });
        delegateRecord.IsActive = false;

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = role,
            action = "Revoke FM Delegate",
            module = "ExecutiveApprovals",
            affected_record_type = "FinanceManagerDelegate",
            before_value = beforeVal,
            after_value = JsonSerializer.Serialize(new { delegateRecord.DelegateId, delegateRecord.DelegateUserId, delegateRecord.IsActive }),
            remarks = $"Financial Manager '{userId}' revoked delegation #{delegateId} for '{delegateRecord.DelegateUserId}'.",
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true, message = $"Delegate {delegateRecord.DelegateUserId} revoked successfully." });
    }

    /// <summary>
    /// GET /api/executive-approvals/delegates — List delegates configured by Financial Manager.
    /// </summary>
    [HttpGet("delegates")]
    public async Task<IActionResult> GetDelegates()
    {
        var (role, _) = GetCurrentUser();
        bool isFm = string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase);

        if (!isFm)
        {
            return Forbid("Only the Financial Manager may view configured delegates.");
        }

        var list = await _fomsDb.FinanceManagerDelegates
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// POST /api/executive-approvals — Record an executive approval.
    /// Only Financial Manager or a configured delegate can record approvals.
    /// Mandatory proof attachment and coverage checks enforced.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExecutiveApprovalDto dto)
    {
        var (role, userId) = GetCurrentUser();

        // 1. Role Authorization Guard: Only Financial Manager or Configured Delegate
        bool isFm = string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase);

        if (!isFm && !dto.IsDelegated)
        {
            return Forbid("Only the Financial Manager or a configured delegate may record an Executive Approval.");
        }

        if (dto.IsDelegated)
        {
            if (string.IsNullOrWhiteSpace(dto.DelegateJustification))
            {
                return BadRequest(new { message = "delegate_justification is mandatory when approval is recorded by a delegate." });
            }

            // Verify configured delegate in database (Q9)
            var now = DateTime.UtcNow;
            var isConfiguredDelegate = await _fomsDb.Database.SqlQueryRaw<int>(@"
                SELECT COUNT(*) AS Value
                FROM dbo.FinanceManagerDelegates
                WHERE delegate_user_id = @p0 
                  AND is_active = 1 
                  AND valid_from <= @p1 
                  AND valid_until >= @p1", userId, now).FirstOrDefaultAsync() > 0;

            if (!isConfiguredDelegate && !isFm)
            {
                return Forbid("User is not an active configured delegate for the Financial Manager.");
            }
        }

        // 1b. Separation of Duties (Item 5): A delegate cannot record an approval for a transaction they requested or validated
        if (dto.IsDelegated || !isFm)
        {
            if (!string.IsNullOrWhiteSpace(dto.RequestedBy) &&
                string.Equals(userId, dto.RequestedBy, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Separation of duties violation: A delegate cannot record an approval for a transaction they requested." });
            }

            if (!string.IsNullOrWhiteSpace(dto.ValidatedBy) &&
                string.Equals(userId, dto.ValidatedBy, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Separation of duties violation: A delegate cannot record an approval for a transaction they validated." });
            }

            if (dto.TransactionType == "LargeCashOutflow")
            {
                var ca = await _fomsDb.CashAdvances.FirstOrDefaultAsync(c => c.AdvanceId == dto.TransactionReference);
                if (ca != null && (string.Equals(ca.CourierName, userId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ca.ReleasedBy, userId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ca.ApprovedBy, userId, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { message = "Separation of duties violation: A delegate cannot record an approval for a transaction they requested or validated." });
                }
            }
            else if (dto.TransactionType == "Discount")
            {
                var inv = await _fomsDb.Invoices.FirstOrDefaultAsync(i => i.InvoiceNo == dto.TransactionReference || i.Id == dto.TransactionReference);
                if (inv != null && (string.Equals(inv.CreatedBy, userId, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(inv.ApprovedBy, userId, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { message = "Separation of duties violation: A delegate cannot record an approval for a transaction they requested or validated." });
                }
            }
        }

        // 2. Validate Approver Title (Item 3: CEO or President only)
        if (dto.ApproverTitle != "CEO" && dto.ApproverTitle != "President")
        {
            return BadRequest(new { message = "Approver title must be either 'CEO' or 'President'." });
        }

        // 3. Validate Decision
        if (dto.Decision != "Approved" && dto.Decision != "Rejected")
        {
            return BadRequest(new { message = "Decision must be either 'Approved' or 'Rejected'." });
        }

        // 4. Validate Transaction Type
        var validTypes = new[] { "Discount", "LargeCashOutflow", "BudgetOverride", "BudgetTransfer", "BudgetCarryOver" };
        if (!validTypes.Contains(dto.TransactionType))
        {
            return BadRequest(new { message = $"Invalid transaction_type. Allowed types: {string.Join(", ", validTypes)}." });
        }

        // 5. Mandatory Proof Attachment Guard (Condition #4: through upload service)
        if (string.IsNullOrWhiteSpace(dto.ProofAttachmentUrl) || !dto.ProofAttachmentUrl.Contains("/uploads/"))
        {
            return BadRequest(new { message = "A valid proof file URL generated by the FOMS upload service is strictly mandatory for Executive Approvals." });
        }

        // 6. Coverage Validation: Approved amount must cover the transaction amount
        if (dto.Decision == "Approved" && dto.TransactionAmount > 0 && dto.ApprovedAmount < dto.TransactionAmount)
        {
            return BadRequest(new { message = $"Approved amount (₱{dto.ApprovedAmount:N2}) must cover the transaction amount (₱{dto.TransactionAmount:N2})." });
        }

        // 7. Parse Approval Date
        if (!DateOnly.TryParse(dto.ApprovalDate, out var parsedDate))
        {
            parsedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        // 8. One Active Record per Transaction Reference: Deactivate existing active record if creating a superseding one
        var existingActive = await _fomsDb.ExecutiveApprovalRecords
            .FirstOrDefaultAsync(r => r.TransactionReference == dto.TransactionReference && r.IsActive);

        int? supersedesId = null;
        if (existingActive != null)
        {
            existingActive.IsActive = false;
            supersedesId = existingActive.ApprovalId;
        }

        var record = new ExecutiveApprovalRecord
        {
            ApproverTitle = dto.ApproverTitle,
            ApproverName = dto.ApproverName.Trim(),
            ApprovalDate = parsedDate,
            Decision = dto.Decision,
            ApprovedAmount = dto.ApprovedAmount,
            TransactionType = dto.TransactionType,
            TransactionReference = dto.TransactionReference.Trim(),
            ProofAttachmentUrl = dto.ProofAttachmentUrl.Trim(),
            RecordedBy = userId,
            IsDelegated = dto.IsDelegated,
            DelegateJustification = dto.IsDelegated ? dto.DelegateJustification?.Trim() : null,
            SupersedesApprovalId = supersedesId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _fomsDb.ExecutiveApprovalRecords.Add(record);

        // 9. Append-Only Audit Trail Capture (AUD-01)
        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = role,
            action = "EXECUTIVE_APPROVAL_RECORDED",
            module = "ExecutiveApprovals",
            affected_record_type = "ExecutiveApproval",
            remarks = $"{dto.ApproverTitle} ({dto.ApproverName}) {dto.Decision} {dto.TransactionType} {dto.TransactionReference} for ₱{dto.ApprovedAmount:N2}. Recorded by {userId}" + (dto.IsDelegated ? $" (Delegated: {dto.DelegateJustification})" : ""),
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"Executive Approval record #{record.ApprovalId} saved successfully.",
            data = record
        });
    }
}
