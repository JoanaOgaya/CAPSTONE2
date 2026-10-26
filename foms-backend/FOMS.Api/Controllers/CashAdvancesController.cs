using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[ApiController]
[Route("api/cash-advances")]
[Route("api/v1/cash-advances")]
[Route("cash-advances")]
public class CashAdvancesController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;
    private readonly ILogger<CashAdvancesController> _logger;

    public CashAdvancesController(FomsDbContext fomsDb, ILogger<CashAdvancesController> logger)
    {
        _fomsDb = fomsDb;
        _logger = logger;
    }

    private (int userId, string username, string role, string employeeId) GetCurrentUserInfo()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? "Accountant";

        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                    ?? User.FindFirst("name")?.Value
                    ?? User.Identity?.Name
                    ?? "Accountant";

        var employeeId = User.FindFirst("employee_id")?.Value
                      ?? User.FindFirst("employee_number")?.Value
                      ?? User.FindFirst("sub")?.Value
                      ?? username;

        var userIdClaim = User.FindFirst("user_id")?.Value
                       ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        int userId = 1;
        if (int.TryParse(userIdClaim, out var parsed))
        {
            userId = parsed;
        }

        return (userId, username, role, employeeId);
    }

    private static string MapStatus(string dbStatus, string? approvalStatus)
    {
        var target = !string.IsNullOrWhiteSpace(approvalStatus) ? approvalStatus : dbStatus;
        return target switch
        {
            "Draft" => "Draft",
            "Pending Assistant Review" => "Submitted",
            "Pending Financial Manager Approval" => "PendingFinanceManager",
            "Approved by Assistant" => "Approved",
            "Approved by Financial Manager" => "Approved",
            "Approved" => "Approved",
            "Disbursed" => "Approved",
            "Rejected by Assistant" => "ReturnedForRevision",
            "Rejected by Financial Manager" => "ReturnedForRevision",
            "Returned for Correction" => "ReturnedForRevision",
            "ReturnedForRevision" => "ReturnedForRevision",
            "Returned" => "ReturnedForRevision",
            "Rejected" => "ReturnedForRevision",
            "Pending Finance Review" => "Submitted",
            "Pending Liquidation" => "Approved",
            "Liquidated" => "Approved",
            _ => "Submitted"
        };
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var query = _fomsDb.CashAdvances.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        var list = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
        return Ok(new
        {
            items = list,
            total = list.Count
        });
    }

    /// <summary>
    /// Fetch cash advances for the current user/accountant portal
    /// </summary>
    [HttpGet("mine")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMine()
    {
        var list = await _fomsDb.CashAdvances.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync();
        var approvalRequests = await _fomsDb.CashOutflowApprovalRequests.AsNoTracking()
            .Where(r => r.Category == "CashAdvance")
            .ToListAsync();

        var result = list.Select(item =>
        {
            var req = approvalRequests.FirstOrDefault(r => r.CashOutflowId == item.AdvanceId || r.TransactionReference == item.AdvanceId);
            var status = MapStatus(item.Status, req?.Status);

            return new
            {
                id = item.AdvanceId,
                referenceNo = item.AdvanceId,
                amount = item.AmountGiven,
                purpose = !string.IsNullOrWhiteSpace(item.TripReference) ? item.TripReference : (!string.IsNullOrWhiteSpace(item.Notes) ? item.Notes : "Cash Advance Request"),
                dateNeeded = (item.ReleaseDate ?? item.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
                remarks = item.Notes,
                requestedBy = item.CourierName ?? item.ReleasedBy ?? "Accountant",
                requestedById = item.ReleasedBy ?? "USER-ACC-001",
                status = status,
                rejectionReason = req?.RejectionReason,
                createdAt = (item.CreatedAt ?? DateTime.UtcNow).ToString("o"),
                updatedAt = (item.ReleaseDate ?? item.CreatedAt ?? DateTime.UtcNow).ToString("o")
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] CashAdvanceCreateDto dto)
    {
        if (dto == null) return BadRequest(new { message = "Invalid request payload." });
        if (dto.Amount <= 0) return BadRequest(new { message = "Amount must be greater than 0." });
        if (string.IsNullOrWhiteSpace(dto.Purpose)) return BadRequest(new { message = "Purpose is required." });

        var (_, username, _, employeeId) = GetCurrentUserInfo();

        DateTime? neededDate = null;
        if (!string.IsNullOrWhiteSpace(dto.DateNeeded) && DateTime.TryParse(dto.DateNeeded, out var parsedDate))
        {
            neededDate = parsedDate;
        }

        var advId = $"CA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
        var item = new CashAdvance
        {
            AdvanceId = advId,
            CourierName = !string.IsNullOrWhiteSpace(username) ? username : "Accountant",
            TripReference = dto.Purpose.Trim(),
            AmountGiven = dto.Amount,
            ReleaseDate = neededDate ?? DateTime.UtcNow,
            ReleasedBy = employeeId,
            Status = "Draft",
            Notes = !string.IsNullOrWhiteSpace(dto.Remarks) ? $"{dto.Purpose.Trim()} - {dto.Remarks.Trim()}" : dto.Purpose.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _fomsDb.CashAdvances.Add(item);
        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            id = item.AdvanceId,
            referenceNo = item.AdvanceId,
            amount = item.AmountGiven,
            purpose = dto.Purpose.Trim(),
            dateNeeded = (item.ReleaseDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            remarks = dto.Remarks,
            requestedBy = item.CourierName,
            requestedById = employeeId,
            status = "Draft",
            createdAt = item.CreatedAt?.ToString("o")
        });
    }

    [HttpPut("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Update(string id, [FromBody] CashAdvanceCreateDto dto)
    {
        var item = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == id);
        if (item == null) return NotFound(new { message = $"Cash Advance '{id}' not found." });

        if (dto.Amount > 0) item.AmountGiven = dto.Amount;
        if (!string.IsNullOrWhiteSpace(dto.Purpose))
        {
            item.TripReference = dto.Purpose.Trim();
            item.Notes = !string.IsNullOrWhiteSpace(dto.Remarks) ? $"{dto.Purpose.Trim()} - {dto.Remarks.Trim()}" : dto.Purpose.Trim();
        }
        if (!string.IsNullOrWhiteSpace(dto.DateNeeded) && DateTime.TryParse(dto.DateNeeded, out var parsedDate))
        {
            item.ReleaseDate = parsedDate;
        }

        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            id = item.AdvanceId,
            referenceNo = item.AdvanceId,
            amount = item.AmountGiven,
            purpose = item.TripReference ?? item.Notes ?? "Cash Advance",
            dateNeeded = (item.ReleaseDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            remarks = item.Notes,
            requestedBy = item.CourierName ?? "Accountant",
            status = MapStatus(item.Status, null),
            createdAt = item.CreatedAt?.ToString("o")
        });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(string id)
    {
        if (string.Equals(id, "mine", StringComparison.OrdinalIgnoreCase))
        {
            return await GetMine();
        }

        var item = await _fomsDb.CashAdvances.AsNoTracking().FirstOrDefaultAsync(a => a.AdvanceId == id);
        if (item == null) return NotFound(new { message = $"Cash Advance '{id}' not found." });
        return Ok(item);
    }

    /// <summary>
    /// FR-050: Finance Review and Approval of Cash Advance prior to payout.
    /// </summary>
    [HttpPost("{id}/approve")]
    [AllowAnonymous]
    public async Task<IActionResult> Approve(string id, [FromBody] System.Text.Json.JsonElement? body)
    {
        var item = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == id);
        if (item == null) return NotFound(new { message = $"Cash Advance '{id}' not found." });

        string reviewer = "Financial Manager";
        if (body.HasValue && body.Value.TryGetProperty("approvedBy", out var apprProp) && !string.IsNullOrWhiteSpace(apprProp.GetString()))
        {
            reviewer = apprProp.GetString()!;
        }
        else if (User.Identity?.Name != null)
        {
            reviewer = User.Identity.Name;
        }

        item.Status = "Approved";
        item.ApprovedBy = reviewer;
        item.ApprovedAt = DateTime.UtcNow;

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = "Finance",
            action = "CASH_ADVANCE_APPROVED",
            module = "Cash Advances",
            remarks = $"Cash Advance {item.AdvanceId} for {item.CourierName} approved for disbursement: ₱{item.AmountGiven:N2} by {reviewer} per FR-050.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            isSuccess = true,
            message = $"Cash Advance {item.AdvanceId} approved per FR-050.",
            data = item
        });
    }

    /// <summary>
    /// FR-043, FR-044: Payout and Disbursement of approved Cash Advance.
    /// Records Cash Outflow in FOMS_DB.
    /// </summary>
    [HttpPost("{id}/disburse")]
    [AllowAnonymous]
    public async Task<IActionResult> Disburse(string id, [FromBody] System.Text.Json.JsonElement? body)
    {
        var item = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == id);
        if (item == null) return NotFound(new { message = $"Cash Advance '{id}' not found." });

        string disburser = "Finance Disbursing Officer";
        if (body.HasValue && body.Value.TryGetProperty("disbursedBy", out var disbProp) && !string.IsNullOrWhiteSpace(disbProp.GetString()))
        {
            disburser = disbProp.GetString()!;
        }

        item.Status = "Disbursed";
        item.DisbursedBy = disburser;
        item.DisbursedAt = DateTime.UtcNow;

        // FR-043, FR-044: Record Cash Outflow
        var outflow = new CashOutflow
        {
            SourceReference = item.AdvanceId,
            RecordedBy = disburser,
            Category = "Delivery Cash Advance Payout",
            Description = $"Cash Advance {item.AdvanceId} payout for courier {item.CourierName} (Trip: {item.TripReference})",
            Amount = item.AmountGiven,
            Status = "Completed",
            OutflowDate = DateTime.UtcNow,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _fomsDb.CashOutflows.Add(outflow);

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = "Finance",
            action = "CASH_ADVANCE_DISBURSED",
            module = "Cash Advances",
            remarks = $"Cash Advance {item.AdvanceId} disbursed: ₱{item.AmountGiven:N2} to {item.CourierName}. Cash Outflow recorded per FR-043, FR-044.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            isSuccess = true,
            message = $"Cash Advance {item.AdvanceId} disbursed. Cash outflow of ₱{item.AmountGiven:N2} recorded.",
            data = item
        });
    }
}

public class CashAdvanceCreateDto
{
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? DateNeeded { get; set; }
    public string? Remarks { get; set; }
}
