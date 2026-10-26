using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class AuditLogInputDto
{
    public string? Action { get; set; }
    public string? Module { get; set; }
    public string? EntityName { get; set; }
    public string? RecordId { get; set; }
    public string? EntityId { get; set; }
    public string? RecordType { get; set; }
    public string? Details { get; set; }
    public string? Remarks { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string? UserId { get; set; }
    public string? UserRole { get; set; }
}

/// <summary>
/// AUD-01 to AUD-03: Append-Only Immutable Audit Trail.
/// Only Head Accountant and Financial Manager can view audit logs.
/// Head Accountant audits Accounts and Journal entries.
/// Financial Manager audits Expenses and Cash Flow.
/// Both can view Executive Approvals.
/// All other roles receive 403 Forbidden.
/// Physical UPDATE and DELETE are prohibited.
/// </summary>
[Authorize]
[ApiController]
[Route("api/audit-logs")]
[Route("audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public AuditLogsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string userId) GetUserRoleAndId()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? string.Empty;

        return (role, userId);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? module = null, [FromQuery] string? search = null, [FromQuery] string? startDate = null, [FromQuery] string? endDate = null)
    {
        var (role, _) = GetUserRoleAndId();

        // AUD-03: Access Guard — Only Head Accountant and Financial Manager may view audit logs
        if (!string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "HEAD_ACCOUNTANT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid("Access denied. Only the Head Accountant and Financial Manager may view the audit trail per AUD-03.");
        }

        var query = _fomsDb.AuditLogs.AsNoTracking().AsQueryable();

        // AUD-03 Role Scoping:
        // Head Accountant audits Accounts, Billing, Payments, Invoices, Settlements, Adjustments
        // Financial Manager audits Expenses, Cash Advances, Liquidations, Cash Outflow, Budget
        // Both can audit Executive Approvals
        if (string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "HEAD_ACCOUNTANT", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => 
                a.module == "ExecutiveApprovals" ||
                a.module == "Invoices" ||
                a.module == "Payments" ||
                a.module == "AccountsReceivable" ||
                a.module == "Billing" ||
                a.module == "Settlements" ||
                a.module == "Adjustments" ||
                a.module == "Clients" ||
                a.module == "Rates" ||
                a.affected_record_type == "ExecutiveApproval" ||
                a.affected_record_type == "Invoice" ||
                a.affected_record_type == "Payment");
        }
        else if (string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => 
                a.module == "ExecutiveApprovals" ||
                a.module == "CashAdvances" ||
                a.module == "Liquidations" ||
                a.module == "CashFlow" ||
                a.module == "CashOutflows" ||
                a.module == "Budget" ||
                a.module == "Expenses" ||
                a.affected_record_type == "ExecutiveApproval" ||
                a.affected_record_type == "CashOutflow" ||
                a.affected_record_type == "CashAdvance" ||
                a.affected_record_type == "Liquidation");
        }

        if (!string.IsNullOrWhiteSpace(module) && module != "All")
        {
            query = query.Where(a => a.module == module);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a => 
                a.action.Contains(search) || 
                (a.remarks != null && a.remarks.Contains(search)) ||
                (a.affected_record_type != null && a.affected_record_type.Contains(search)));
        }

        if (DateTime.TryParse(startDate, out var start))
        {
            query = query.Where(a => a.created_at >= start);
        }
        if (DateTime.TryParse(endDate, out var end))
        {
            query = query.Where(a => a.created_at <= end.AddDays(1));
        }

        var list = await query.OrderByDescending(a => a.created_at).Take(500).ToListAsync();

        var result = list.Select(a => new
        {
            id = a.audit_id.ToString(),
            auditId = a.audit_id.ToString(),
            timestamp = a.created_at?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            loggedAt = a.created_at?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            userId = a.user_id?.ToString() ?? "SYSTEM",
            userFullName = a.role ?? "System User",
            userRole = a.role ?? string.Empty,
            role = a.role ?? string.Empty,
            action = a.action,
            module = a.module,
            entityName = a.affected_record_type ?? a.module,
            recordId = a.affected_record_id?.ToString() ?? string.Empty,
            entityId = a.affected_record_id?.ToString() ?? string.Empty,
            recordType = a.affected_record_type ?? a.module,
            beforeValue = a.before_value,
            afterValue = a.after_value,
            details = a.remarks ?? $"{a.action} on {a.module}",
            remarks = a.remarks
        }).ToList();

        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv([FromQuery] string? module, [FromQuery] string? startDate, [FromQuery] string? endDate)
    {
        var (role, _) = GetUserRoleAndId();
        if (!string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "HEAD_ACCOUNTANT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid("Access denied.");
        }

        var query = _fomsDb.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(module) && module != "All") query = query.Where(a => a.module == module);
        if (DateTime.TryParse(startDate, out var start)) query = query.Where(a => a.created_at >= start);
        if (DateTime.TryParse(endDate, out var end)) query = query.Where(a => a.created_at <= end.AddDays(1));

        var logs = await query.OrderByDescending(a => a.created_at).Take(2000).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("AuditID,Timestamp,User,Role,Action,Module,RecordType,RecordID,Remarks");
        foreach (var l in logs)
        {
            sb.AppendLine($"\"{l.audit_id}\",\"{l.created_at:yyyy-MM-dd HH:mm:ss}\",\"{l.user_id}\",\"{l.role}\",\"{l.action}\",\"{l.module}\",\"{l.affected_record_type}\",\"{l.affected_record_id}\",\"{l.remarks?.Replace("\"", "\"\"")}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"FOMS_Audit_Trail_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Append an immutable audit record (AUD-01).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AuditLogInputDto dto)
    {
        var (role, userId) = GetUserRoleAndId();
        var effectiveRole = !string.IsNullOrWhiteSpace(role) ? role : (dto.UserRole ?? "User");
        var effectiveUserId = !string.IsNullOrWhiteSpace(userId) ? userId : (dto.UserId ?? "SYSTEM");

        int? parsedUserId = null;
        if (int.TryParse(effectiveUserId.Replace("EMP-", "").Replace("USER-", ""), out var uid))
        {
            parsedUserId = uid;
        }

        int? parsedRecordId = null;
        var rawRecordId = dto.RecordId ?? dto.EntityId;
        if (!string.IsNullOrWhiteSpace(rawRecordId) && int.TryParse(rawRecordId.Replace("INV-", "").Replace("CO-", "").Replace("CA-", ""), out var rid))
        {
            parsedRecordId = rid;
        }

        var log = new AuditLog
        {
            user_id = parsedUserId,
            role = effectiveRole,
            action = dto.Action ?? "USER_ACTION",
            module = dto.Module ?? dto.EntityName ?? "General",
            affected_record_type = dto.RecordType ?? dto.EntityName ?? "General",
            affected_record_id = parsedRecordId,
            before_value = dto.BeforeValue,
            after_value = dto.AfterValue,
            remarks = dto.Remarks ?? dto.Details ?? $"{dto.Action} recorded",
            created_at = DateTime.UtcNow
        };

        _fomsDb.AuditLogs.Add(log);
        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            id = log.audit_id.ToString(),
            auditId = log.audit_id.ToString(),
            timestamp = log.created_at?.ToString("yyyy-MM-ddTHH:mm:ss"),
            loggedAt = log.created_at?.ToString("yyyy-MM-ddTHH:mm:ss"),
            userId = effectiveUserId,
            role = effectiveRole,
            action = log.action,
            module = log.module,
            remarks = log.remarks
        });
    }
}
