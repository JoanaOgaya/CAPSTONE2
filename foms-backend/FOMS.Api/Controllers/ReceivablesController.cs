using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Services;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[Authorize]
[Route("receivables")]
[Route("ar")]
[Route("api/receivables")]
[Route("api/v1/receivables")]
[Route("api/ar")]
[ApiController]
public class ReceivablesController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public ReceivablesController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string? clientId) GetCaller()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var clientId = User.FindFirst("client_id")?.Value;
        return (role, clientId);
    }

    [HttpGet("records")]
    public async Task<IActionResult> GetArRecords()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var inv in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(inv);
        }

        var records = invoices.Select(i =>
        {
            var dueDateStr = i.DueDate?.ToString("yyyy-MM-dd") ?? i.DueDateStr;
            var isDueSoon = DateTime.TryParse(dueDateStr, out var dt) && (dt - DateTime.UtcNow).TotalDays <= 7 && (dt - DateTime.UtcNow).TotalDays >= 0;
            return new
            {
                id = i.Id,
                invoiceId = i.Id,
                invoiceNumber = i.InvoiceNo,
                clientId = i.ClientIdStr ?? (i.ClientId.HasValue ? i.ClientId.Value.ToString() : "1"),
                clientName = !string.IsNullOrEmpty(i.ClientName) ? i.ClientName : $"Client {i.ClientIdStr}",
                invoiceDate = i.BillingDate,
                dueDate = dueDateStr,
                originalAmount = i.TotalAmount,
                paidAmount = i.AmountPaid,
                outstandingBalance = i.Balance,
                agingBracket = i.DaysOverdue <= 0 ? "Current" :
                               i.DaysOverdue <= 30 ? "0-30 days" :
                               i.DaysOverdue <= 60 ? "31-60 days" :
                               i.DaysOverdue <= 90 ? "61-90 days" : "90+ days",
                agingDays = i.DaysOverdue,
                status = i.Balance <= 0 ? "Current" :
                         i.DaysOverdue > 0 ? "Overdue" :
                         isDueSoon ? "Due Soon" : "Outstanding"
            };
        }).ToList();

        return Ok(records);
    }

    [HttpGet("aging")]
    public async Task<IActionResult> GetArAging()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived && i.Balance > 0);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var inv in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(inv);
        }

        var result = new List<object>
        {
            new { label = "Current", value = invoices.Where(i => i.DaysOverdue <= 0).Sum(i => i.Balance) },
            new { label = "0-30 days", value = invoices.Where(i => i.DaysOverdue > 0 && i.DaysOverdue <= 30).Sum(i => i.Balance) },
            new { label = "31-60 days", value = invoices.Where(i => i.DaysOverdue > 30 && i.DaysOverdue <= 60).Sum(i => i.Balance) },
            new { label = "61-90 days", value = invoices.Where(i => i.DaysOverdue > 60 && i.DaysOverdue <= 90).Sum(i => i.Balance) },
            new { label = "90+ days", value = invoices.Where(i => i.DaysOverdue > 90).Sum(i => i.Balance) },
        };

        return Ok(result);
    }

    [HttpGet("follow-ups")]
    public IActionResult GetFollowUps() => Ok(new List<object>());

    [HttpPost("follow-ups")]
    public IActionResult LogFollowUp([FromBody] object body) => Ok(body);

    [HttpGet]
    public async Task<IActionResult> GetReceivables()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.AccountsReceivable.AsNoTracking().AsQueryable();

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            if (int.TryParse(clientIdClaim, out var parsedCId))
            {
                query = query.Where(r => r.ClientId == parsedCId);
            }
        }

        var receivables = await query.ToListAsync();
        var result = receivables.Select(r => new
        {
            Id = r.ArId.ToString(),
            ClientId = r.ClientId.ToString(),
            InvoiceId = r.InvoiceId.ToString(),
            BalanceAmount = r.OutstandingBalance ?? 0m,
            DueDate = r.DueDate?.ToString("yyyy-MM-dd"),
        }).ToList();

        return Ok(result);
    }

    [HttpGet("outstanding")]
    public async Task<IActionResult> GetOutstandingBalances()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var outstanding = invoices
            .Where(i => i.Balance > 0)
            .OrderByDescending(i => i.DaysOverdue)
            .ToList();

        return Ok(outstanding);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetOutstandingSummary()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var outstanding = invoices.Where(i => i.Balance > 0).ToList();

        var summary = new
        {
            TotalOutstanding = outstanding.Sum(i => i.Balance),
            TotalOverdue = outstanding.Where(i => i.DaysOverdue > 0).Sum(i => i.Balance),
            TotalCurrent = outstanding.Where(i => i.DaysOverdue == 0).Sum(i => i.Balance),
            InvoiceCount = outstanding.Count,
            OverdueCount = outstanding.Count(i => i.DaysOverdue > 0),
            Buckets = new
            {
                Current = outstanding.Where(i => i.AgingBucket == "Current").Sum(i => i.Balance),
                Days1_30 = outstanding.Where(i => i.AgingBucket == "1-30").Sum(i => i.Balance),
                Days31_60 = outstanding.Where(i => i.AgingBucket == "31-60").Sum(i => i.Balance),
                Days61_90 = outstanding.Where(i => i.AgingBucket == "61-90").Sum(i => i.Balance),
                Days90Plus = outstanding.Where(i => i.AgingBucket == "90+").Sum(i => i.Balance),
            }
        };

        return Ok(summary);
    }

    [HttpGet("aging-accounts")]
    public async Task<IActionResult> GetAgingAccounts()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived && i.Balance > 0);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var aging = invoices
            .GroupBy(i => i.ClientIdStr ?? (i.ClientId.HasValue ? i.ClientId.Value.ToString() : "1"))
            .Select(g => new
            {
                ClientId = g.Key,
                ClientName = g.First().ClientName,
                TotalBalance = g.Sum(i => i.Balance),
                DaysPastDue = g.Max(i => i.DaysOverdue),
                AgingBucket = g.OrderByDescending(i => i.DaysOverdue).First().AgingBucket,
                Status = g.OrderByDescending(i => i.DaysOverdue).First().PaymentStatus,
            })
            .OrderByDescending(a => a.DaysPastDue)
            .ToList();

        return Ok(aging);
    }

    [HttpGet("overdue-accounts")]
    public async Task<IActionResult> GetOverdueAccounts()
    {
        var (role, clientIdClaim) = GetCaller();

        var query = _fomsDb.Invoices.AsNoTracking().Where(i => !i.Archived && i.Balance > 0);

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(i => i.ClientIdStr == clientIdClaim || (i.ClientId.HasValue && i.ClientId.ToString() == clientIdClaim) || i.ClientName == clientIdClaim);
        }

        var invoices = await query.ToListAsync();
        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var overdue = invoices
            .Where(i => i.DaysOverdue > 0)
            .GroupBy(i => i.ClientIdStr ?? (i.ClientId.HasValue ? i.ClientId.Value.ToString() : "1"))
            .Select(g => new
            {
                ClientId = g.Key,
                ClientName = g.First().ClientName,
                TotalBalance = g.Sum(i => i.Balance),
                DaysPastDue = g.Max(i => i.DaysOverdue),
                AgingBucket = g.OrderByDescending(i => i.DaysOverdue).First().AgingBucket,
                InvoiceCount = g.Count(),
            })
            .OrderByDescending(a => a.DaysPastDue)
            .ToList();

        return Ok(overdue);
    }

    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetClientOutstanding(string clientId)
    {
        var (role, clientIdClaim) = GetCaller();

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim) || (!clientId.Equals(clientIdClaim, StringComparison.OrdinalIgnoreCase) && !clientIdClaim.Contains(clientId)))
            {
                return Forbid("Access denied to another client's outstanding balance.");
            }
        }

        var altClientId = clientId.StartsWith("CL-")
            ? clientId.Replace("CL-", "CA-")
            : clientId.StartsWith("CA-")
                ? clientId.Replace("CA-", "CL-")
                : clientId;

        var invoices = await _fomsDb.Invoices
            .Where(i => (i.ClientIdStr == clientId || i.ClientIdStr == altClientId || (i.ClientId.HasValue && i.ClientId.ToString() == clientId)) && !i.Archived && i.Balance > 0)
            .ToListAsync();

        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var client = await _fomsDb.Clients.FirstOrDefaultAsync(c => c.Id == clientId || c.ClientCode == clientId || c.Id == altClientId);

        var result = new
        {
            ClientId = clientId,
            ClientName = client?.Name ?? client?.CompanyName ?? invoices.FirstOrDefault()?.ClientName ?? "Unknown",
            TotalOutstanding = invoices.Sum(i => i.Balance),
            Invoices = invoices.OrderByDescending(i => i.DaysOverdue).ToList()
        };

        return Ok(result);
    }
}
