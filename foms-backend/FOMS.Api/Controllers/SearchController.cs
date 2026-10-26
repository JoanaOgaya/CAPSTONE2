using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.DTOs;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[Authorize]
[Route("api/search")]
[Route("api/v1/search")]
[ApiController]
public class SearchController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public SearchController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    [HttpPost("query")]
    public async Task<IActionResult> AdvancedSearch([FromBody] AdvancedFilterRequest request)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        var clientIdClaim = User.FindFirst("client_id")?.Value;
        var isClient = string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase);

        if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate.Value > request.EndDate.Value)
        {
            return BadRequest(new { message = "Validation Error: Start Date cannot be greater than End Date." });
        }

        DateTime now = DateTime.UtcNow;
        if ((request.StartDate.HasValue && request.StartDate.Value > now.AddDays(1)) ||
            (request.EndDate.HasValue && request.EndDate.Value > now.AddDays(1)))
        {
            return BadRequest(new { message = "Validation Error: Future dates cannot be selected." });
        }

        DateTime? filterStart = request.StartDate;
        DateTime? filterEnd = request.EndDate;

        if (!string.IsNullOrEmpty(request.DateFilterType))
        {
            DateTime today = DateTime.Today;
            switch (request.DateFilterType.ToLower())
            {
                case "today":
                    filterStart = today;
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
                case "yesterday":
                    filterStart = today.AddDays(-1);
                    filterEnd = today.AddSeconds(-1);
                    break;
                case "last7days":
                    filterStart = today.AddDays(-7);
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
                case "last30days":
                    filterStart = today.AddDays(-30);
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
                case "thismonth":
                    filterStart = new DateTime(today.Year, today.Month, 1);
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
                case "lastmonth":
                    var lm = today.AddMonths(-1);
                    filterStart = new DateTime(lm.Year, lm.Month, 1);
                    filterEnd = new DateTime(today.Year, today.Month, 1).AddSeconds(-1);
                    break;
                case "quarterly":
                    int quarter = (today.Month - 1) / 3;
                    filterStart = new DateTime(today.Year, (quarter * 3) + 1, 1);
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
                case "yearly":
                    filterStart = new DateTime(today.Year, 1, 1);
                    filterEnd = today.AddDays(1).AddSeconds(-1);
                    break;
            }
        }

        var clientsQuery = _fomsDb.Clients.AsNoTracking().AsQueryable();
        var invoicesQuery = _fomsDb.Invoices.AsNoTracking().AsQueryable();
        var paymentsQuery = _fomsDb.Payments.AsNoTracking().AsQueryable();

        if (isClient)
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            clientsQuery = clientsQuery.Where(c => c.Id == clientIdClaim || c.ClientCode == clientIdClaim || (c.ClientId.HasValue && c.ClientId.ToString() == clientIdClaim));
            invoicesQuery = invoicesQuery.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
            paymentsQuery = paymentsQuery.Where(p => p.ClientIdStr == clientIdClaim || (p.ClientId.HasValue && p.ClientId.ToString() == clientIdClaim));
        }

        var clients = await clientsQuery.ToListAsync();
        var invoices = await invoicesQuery.ToListAsync();
        var payments = await paymentsQuery.ToListAsync();

        var results = new List<SmartSearchResult>();
        string query = request.SearchQuery?.Trim().ToLower() ?? string.Empty;

        // 1. Invoices
        foreach (var inv in invoices)
        {
            if (DateTime.TryParse(inv.BillingDate, out var bDate))
            {
                if (filterStart.HasValue && bDate < filterStart.Value) continue;
                if (filterEnd.HasValue && bDate > filterEnd.Value) continue;
            }

            if (request.PaymentStatuses != null && request.PaymentStatuses.Any())
            {
                if (!request.PaymentStatuses.Contains(inv.PaymentStatus ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    if (request.PaymentStatuses.Contains("Overdue", StringComparer.OrdinalIgnoreCase) && inv.DaysOverdue > 0)
                    {
                    }
                    else continue;
                }
            }

            if (!string.IsNullOrEmpty(request.ClientId) && inv.ClientIdStr != request.ClientId && (inv.ClientId.HasValue && inv.ClientId.ToString() != request.ClientId))
                continue;

            if (request.MinAmount.HasValue && inv.TotalAmount < request.MinAmount.Value) continue;
            if (request.MaxAmount.HasValue && inv.TotalAmount > request.MaxAmount.Value) continue;

            double score = 0;
            var invNum = inv.InvoiceNo ?? inv.InvoiceNumber ?? string.Empty;
            var cName = inv.ClientName ?? string.Empty;
            if (string.IsNullOrEmpty(query)) score = 0.5;
            else
            {
                if (invNum.ToLower() == query) score = 1.0;
                else if (invNum.ToLower().Contains(query)) score = 0.8;
                else if (cName.ToLower().Contains(query)) score = 0.7;
                else if (inv.Description != null && inv.Description.ToLower().Contains(query)) score = 0.6;
                else
                {
                    double distance = ComputeLevenshteinDistance(invNum.ToLower(), query);
                    if (distance <= 2) score = 0.5;
                    else continue;
                }
            }

            results.Add(new SmartSearchResult
            {
                Id = inv.Id,
                Type = "Invoice",
                ReferenceNumber = invNum,
                Title = $"Invoice #{invNum}",
                Subtitle = $"{cName} · {inv.Description}",
                Amount = inv.TotalAmount,
                Date = inv.BillingDate ?? inv.CreatedAt?.ToString("yyyy-MM-dd"),
                Status = inv.PaymentStatus,
                MatchScore = score,
                Archived = inv.Archived
            });
        }

        // 2. Payments
        foreach (var p in payments)
        {
            var pDateStr = p.PaymentDate ?? p.DateRecorded ?? p.SubmittedAt?.ToString("yyyy-MM-dd");
            if (DateTime.TryParse(pDateStr, out var pDate))
            {
                if (filterStart.HasValue && pDate < filterStart.Value) continue;
                if (filterEnd.HasValue && pDate > filterEnd.Value) continue;
            }

            if (!string.IsNullOrEmpty(request.ClientId) && p.ClientIdStr != request.ClientId && (p.ClientId.HasValue && p.ClientId.ToString() != request.ClientId))
                continue;

            if (request.MinAmount.HasValue && p.Amount < request.MinAmount.Value) continue;
            if (request.MaxAmount.HasValue && p.Amount > request.MaxAmount.Value) continue;

            double score = 0;
            var refNum = p.ReferenceNumber ?? string.Empty;
            var pClient = p.ClientName ?? string.Empty;
            if (string.IsNullOrEmpty(query)) score = 0.5;
            else
            {
                if (refNum.ToLower() == query) score = 1.0;
                else if (refNum.ToLower().Contains(query)) score = 0.8;
                else if (pClient.ToLower().Contains(query)) score = 0.7;
                else if (p.Remarks != null && p.Remarks.ToLower().Contains(query)) score = 0.6;
                else
                {
                    double distance = ComputeLevenshteinDistance(refNum.ToLower(), query);
                    if (distance <= 2) score = 0.5;
                    else continue;
                }
            }

            results.Add(new SmartSearchResult
            {
                Id = p.Id,
                Type = "Payment",
                ReferenceNumber = refNum,
                Title = $"Payment #{refNum} ({p.PaymentMethod})",
                Subtitle = $"{pClient} · Inv: {p.InvoiceNo} · {p.Remarks}",
                Amount = p.Amount,
                Date = pDateStr,
                Status = p.PaymentStatus,
                MatchScore = score,
                Archived = false
            });
        }

        // 3. Clients
        foreach (var c in clients)
        {
            if (request.MinAmount.HasValue && c.CurrentBalance < request.MinAmount.Value) continue;
            if (request.MaxAmount.HasValue && c.CurrentBalance > request.MaxAmount.Value) continue;

            double score = 0;
            var cCode = c.ClientCode ?? c.Id;
            var cName = c.Name ?? c.CompanyName ?? string.Empty;
            if (string.IsNullOrEmpty(query)) score = 0.5;
            else
            {
                if (cCode.ToLower() == query) score = 1.0;
                else if (cName.ToLower().Contains(query) || (c.BusinessName != null && c.BusinessName.ToLower().Contains(query))) score = 0.8;
                else if ((c.ContactPerson != null && c.ContactPerson.ToLower().Contains(query)) || (c.Email != null && c.Email.ToLower().Contains(query))) score = 0.6;
                else
                {
                    double distance = ComputeLevenshteinDistance(cName.ToLower(), query);
                    if (distance <= 2) score = 0.5;
                    else continue;
                }
            }

            results.Add(new SmartSearchResult
            {
                Id = c.Id,
                Type = "Client",
                ReferenceNumber = cCode,
                Title = $"Client: {cName}",
                Subtitle = $"Contact: {c.ContactPerson} ({c.Email}) · Balance: {c.CurrentBalance:C2}",
                Amount = c.CurrentBalance,
                Date = c.DateRegistered,
                Status = c.Status,
                MatchScore = score,
                Archived = c.Archived
            });
        }

        // 4. Audit Logs (internal roles only, per AUD-03)
        if (!isClient && (string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase)))
        {
            var auditLogs = await _fomsDb.AuditLogs.AsNoTracking().ToListAsync();
            foreach (var log in auditLogs)
            {
                if (filterStart.HasValue && log.created_at.HasValue && log.created_at.Value < filterStart.Value) continue;
                if (filterEnd.HasValue && log.created_at.HasValue && log.created_at.Value > filterEnd.Value) continue;

                double score = 0;
                var logAct = log.action ?? string.Empty;
                var logMod = log.module ?? string.Empty;
                var logRem = log.remarks ?? string.Empty;
                if (string.IsNullOrEmpty(query)) score = 0.5;
                else
                {
                    if (logAct.ToLower().Contains(query)) score = 0.8;
                    else if (logMod.ToLower().Contains(query)) score = 0.7;
                    else if (logRem.ToLower().Contains(query)) score = 0.6;
                    else continue;
                }

                results.Add(new SmartSearchResult
                {
                    Id = log.audit_id.ToString(),
                    Type = "AuditLog",
                    ReferenceNumber = log.affected_record_id?.ToString(),
                    Title = $"Audit Log: {logAct} on {logMod}",
                    Subtitle = $"Role: {log.role} · Details: {logRem}",
                    Amount = 0,
                    Date = log.created_at?.ToString("yyyy-MM-dd HH:mm"),
                    Status = "Log",
                    MatchScore = score,
                    Archived = false
                });
            }
        }

        var sorted = results
            .OrderByDescending(r => r.MatchScore)
            .ThenByDescending(r => r.Date)
            .ToList();

        return Ok(sorted);
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
