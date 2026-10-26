using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Application.Services;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/financial-manager/executive-dashboard")]
[Route("financial-manager/executive-dashboard")]
public class ExecutiveDashboardController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public ExecutiveDashboardController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? clientId, [FromQuery] string? period = "monthly")
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value;
        if (!IsFinancialManager(role))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Access denied. Financial Manager role required." });
        }

        var now = DateTime.UtcNow;
        var today = now.Date;
        var invoices = await _fomsDb.Invoices
            .AsNoTracking()
            .Where(invoice => !invoice.Archived && (string.IsNullOrEmpty(clientId) || invoice.ClientIdStr == clientId || (invoice.ClientId.HasValue && invoice.ClientId.ToString() == clientId)))
            .ToListAsync();

        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var clientIds = invoices.Select(invoice => invoice.ClientIdStr ?? (invoice.ClientId.HasValue ? invoice.ClientId.Value.ToString() : "1")).Distinct().ToList();
        var clients = await _fomsDb.Clients
            .AsNoTracking()
            .Where(client => clientIds.Contains(client.Id) || (client.ClientCode != null && clientIds.Contains(client.ClientCode)))
            .ToListAsync();
        var clientNames = clients.ToDictionary(client => client.Id, client => client.Name);

        var paymentsQuery = _fomsDb.Payments.AsNoTracking()
            .Where(payment =>
                (payment.PaymentStatus == "Validated" || payment.PaymentStatus == "Approved") &&
                payment.Flow != null && payment.Flow.ToLower() == "inflow");
        if (!string.IsNullOrEmpty(clientId))
        {
            paymentsQuery = paymentsQuery.Where(payment => payment.ClientIdStr == clientId);
        }
        var payments = await paymentsQuery.ToListAsync();

        var inflows = await _fomsDb.CashInflows
            .AsNoTracking()
            .ToListAsync();

        var totalBilled = invoices.Sum(invoice => invoice.TotalAmount);
        var totalCollected = invoices.Sum(invoice => invoice.AmountPaid);
        var totalArOutstanding = invoices.Where(invoice => invoice.Balance > 0).Sum(invoice => invoice.Balance);
        var collectionRate = totalBilled > 0
            ? Math.Round((double)(totalCollected / totalBilled * 100m), 1)
            : 0d;
        var cashInflowThisMonth = inflows
            .Where(row => (row.TransactionDate ?? row.CreatedAt ?? DateTime.UtcNow).Month == now.Month && (row.TransactionDate ?? row.CreatedAt ?? DateTime.UtcNow).Year == now.Year)
            .Sum(row => row.Amount);

        var nearDueInvoices = invoices
            .Where(invoice =>
                invoice.Balance > 0 &&
                TryGetDate(invoice.DueDate, out var dueDate) &&
                dueDate.Date >= today &&
                dueDate.Date <= today.AddDays(7))
            .Select(invoice =>
            {
                TryGetDate(invoice.DueDate, out var dueDate);
                var daysRemaining = (dueDate.Date - today).Days;
                var cId = invoice.ClientIdStr ?? (invoice.ClientId.HasValue ? invoice.ClientId.Value.ToString() : "1");
                var name = !string.IsNullOrWhiteSpace(invoice.ClientName)
                    ? invoice.ClientName
                    : clientNames.GetValueOrDefault(cId, cId);
                return new
                {
                    clientId = cId,
                    clientName = name,
                    invoiceNumber = invoice.InvoiceNo,
                    amountDue = invoice.Balance,
                    dueDate = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    daysRemaining
                };
            })
            .GroupBy(invoice => invoice.clientId)
            .Select(group => new
            {
                clientId = group.Key,
                clientName = group.First().clientName,
                invoiceNumber = group.OrderBy(invoice => invoice.daysRemaining).First().invoiceNumber,
                amountDue = group.Sum(invoice => invoice.amountDue),
                dueDate = group.OrderBy(invoice => invoice.daysRemaining).First().dueDate,
                daysRemaining = group.Min(invoice => invoice.daysRemaining)
            })
            .OrderBy(invoice => invoice.daysRemaining)
            .ToList();

        var agingBuckets = new[]
        {
            new { label = "Current", value = invoices.Where(invoice => invoice.Balance > 0 && invoice.DaysOverdue <= 0).Sum(invoice => invoice.Balance) },
            new { label = "0-30 days", value = invoices.Where(invoice => invoice.Balance > 0 && invoice.DaysOverdue is > 0 and <= 30).Sum(invoice => invoice.Balance) },
            new { label = "31-60 days", value = invoices.Where(invoice => invoice.Balance > 0 && invoice.DaysOverdue is > 30 and <= 60).Sum(invoice => invoice.Balance) },
            new { label = "61-90 days", value = invoices.Where(invoice => invoice.Balance > 0 && invoice.DaysOverdue is > 60 and <= 90).Sum(invoice => invoice.Balance) },
            new { label = "90+ days", value = invoices.Where(invoice => invoice.Balance > 0 && invoice.DaysOverdue > 90).Sum(invoice => invoice.Balance) }
        };

        var topPayingClients = payments
            .GroupBy(payment => payment.ClientIdStr ?? (payment.ClientId.HasValue ? payment.ClientId.Value.ToString() : "1"))
            .Select(group => new
            {
                clientId = group.Key,
                clientName = group.Select(payment => payment.ClientName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? clientNames.GetValueOrDefault(group.Key, group.Key),
                amount = group.Sum(payment => payment.Amount)
            })
            .OrderByDescending(client => client.amount)
            .Take(5)
            .ToList();

        var duplicateAlerts = await _fomsDb.AIReviewHistories
            .AsNoTracking()
            .CountAsync(review => review.IsDeleted != true && review.FinalDecision != null &&
                (review.FinalDecision.Contains("Duplicate") || review.FinalDecision.Contains("Flagged") || review.FinalDecision.Contains("FLAGGED") || review.FinalDecision.Contains("PENDING_REVIEW")));

        var recentLogs = await _fomsDb.AuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.created_at)
            .Take(5)
            .ToListAsync();
        var recentUserIds = recentLogs
            .Select(log => log.user_id?.ToString())
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct()
            .ToList();
        var recentUsers = await _fomsDb.Users
            .AsNoTracking()
            .ToDictionaryAsync(user => user.UserId.ToString(), user => user.FullName);

        var recentActivity = recentLogs.Select(log => new
        {
            id = log.audit_id.ToString(),
            timestamp = log.created_at ?? DateTime.UtcNow,
            userId = log.user_id?.ToString() ?? "System",
            userFullName = log.user_id.HasValue && recentUsers.TryGetValue(log.user_id.Value.ToString(), out var userName)
                ? userName
                : (log.user_id?.ToString() ?? "System"),
            userRole = log.role ?? string.Empty,
            action = log.action,
            module = log.module,
            recordId = log.affected_record_id?.ToString(),
            recordType = log.affected_record_type,
            details = log.remarks,
            ipAddress = string.Empty
        }).ToList();

        var daysInTrend = period?.ToLowerInvariant() switch
        {
            "weekly" => 7,
            "quarterly" => 90,
            "annual" => 365,
            _ => 30
        };

        var trendStart = today.AddDays(-daysInTrend + 1);
        var invoicesInTrend = invoices
            .Where(invoice =>
                TryGetDate(invoice.BillingDate, out var billingDate) &&
                billingDate.Date >= trendStart &&
                billingDate.Date <= today)
            .ToList();
        var paymentsInTrend = payments
            .Where(payment =>
                TryGetDate(payment.PaymentDate, out var paymentDate) &&
                paymentDate.Date >= trendStart &&
                paymentDate.Date <= today)
            .ToList();

        var collectionTrend = new List<object>();
        for (var date = trendStart; date <= today; date = date.AddDays(1))
        {
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var billed = invoicesInTrend
                .Where(invoice => TryGetDate(invoice.BillingDate, out var billingDate) && billingDate.Date == date)
                .Sum(invoice => invoice.TotalAmount);
            var collected = paymentsInTrend
                .Where(payment => TryGetDate(payment.PaymentDate, out var paymentDate) && paymentDate.Date == date)
                .Sum(payment => payment.Amount);
            collectionTrend.Add(new
            {
                date = dateStr,
                billed,
                collected
            });
        }

        var cashFlowAnalytics = BuildCashFlowAnalytics(inflows, period);

        return Ok(new
        {
            success = true,
            data = new
            {
                totalInvoicesBilled = invoices.Count,
                totalInvoiceAmount = totalBilled,
                totalCollected,
                totalArOutstanding,
                collectionRate,
                cashInflowThisMonth,
                accountsNearDueDate = nearDueInvoices.Count,
                potentialDuplicateAlerts = duplicateAlerts,
                nearDueAccounts = nearDueInvoices,
                agingBuckets,
                collectionTrend,
                cashFlowAnalytics,
                topPayingClients,
                recentActivity
            }
        });
    }

    private static bool IsFinancialManager(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        return role.Equals("Financial Manager", StringComparison.OrdinalIgnoreCase) ||
               role.Equals("FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase) ||
               role.Equals("FinancialManager", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDate(DateOnly? raw, out DateTime date)
    {
        if (raw.HasValue)
        {
            date = raw.Value.ToDateTime(TimeOnly.MinValue);
            return true;
        }
        date = default;
        return false;
    }

    private static bool TryGetDate(string? raw, out DateTime date)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateTime.TryParse(raw, out date);
    }

    private static List<object> BuildCashFlowAnalytics(
        List<FOMS.Domain.Entities.SqlServer.CashInflow> inflows,
        string? period)
    {
        var normalizedPeriod = period?.ToLowerInvariant();
        if (normalizedPeriod == "weekly")
        {
            var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
            return Enumerable.Range(0, 7)
                .Select(offset => sevenDaysAgo.AddDays(offset))
                .Select(day => new
                {
                    period = day.ToString("ddd", CultureInfo.InvariantCulture),
                    inflow = inflows
                        .Where(row => (row.TransactionDate ?? row.CreatedAt ?? DateTime.UtcNow).Date == day)
                        .Sum(row => row.Amount),
                    outflow = 0m
                })
                .Cast<object>()
                .ToList();
        }

        var now = DateTime.UtcNow;
        var months = new List<DateTime>();
        for (var offset = 5; offset >= 0; offset--)
        {
            months.Add(new DateTime(now.Year, now.Month, 1).AddMonths(-offset));
        }

        return months
            .Select(month => new
            {
                period = month.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                inflow = inflows
                    .Where(row =>
                        (row.TransactionDate ?? row.CreatedAt ?? DateTime.UtcNow).Month == month.Month &&
                        (row.TransactionDate ?? row.CreatedAt ?? DateTime.UtcNow).Year == month.Year)
                    .Sum(row => row.Amount),
                outflow = 0m
            })
            .Cast<object>()
            .ToList();
    }
}
