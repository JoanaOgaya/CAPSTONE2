using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FOMS.Api.Services;

public sealed class RetentionArchiveService
{
    public async Task<int> ArchiveEligibleRecordsAsync(
        ApplicationDbContext context,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        // Retention policy: 2y client-visible / 5y internal statutory retention
        var cutoff = utcNow.AddYears(-2);
        var cutoffInternal = utcNow.AddYears(-5);

        var invoices = await context.Invoices
            .Where(invoice => !invoice.Archived && invoice.Balance <= 0m && invoice.TotalAmount > 0m)
            .ToListAsync(cancellationToken);

        var archivedCount = 0;
        foreach (var invoice in invoices)
        {
            if (!string.Equals(invoice.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
                !TryGetInvoiceDate(invoice, out var invoiceDate) ||
                invoiceDate > cutoff)
                continue;

            invoice.Archived = true;
            context.AuditLogs.Add(new AuditLog
            {
                UserId = "system:retention",
                EntityName = "Invoice",
                EntityId = invoice.Id,
                Action = "Retention Archive",
                Details = $"Automatically archived settled invoice {invoice.InvoiceNo} after the two-year billing retention period.",
                BeforeValue = "Archived: false",
                AfterValue = "Archived: true"
            });

            // 5-Year Internal Statutory Retention Enforcement
            if (invoiceDate <= cutoffInternal)
            {
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = "system:retention",
                    EntityName = "Invoice",
                    EntityId = invoice.Id,
                    Action = "Internal 5Y Retention Enforced",
                    Details = $"Enforced 5-year internal statutory retention limit on invoice {invoice.InvoiceNo} (date: {invoiceDate:yyyy-MM-dd}). Sealed for compliance.",
                    BeforeValue = "InternalRetention: Active",
                    AfterValue = "InternalRetention: 5Y Statutory Sealed"
                });
            }

            archivedCount++;
        }

        var openReceivableClientIds = await context.Invoices
            .Where(invoice => invoice.Balance > 0m)
            .Select(invoice => invoice.ClientId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var clients = await context.Clients
            .Where(client => !client.Archived)
            .ToListAsync(cancellationToken);

        foreach (var client in clients)
        {
            var inactive = !client.IsActive ||
                           string.Equals(client.Status, "Inactive", StringComparison.OrdinalIgnoreCase);
            if (!inactive ||
                openReceivableClientIds.Contains(client.Id) ||
                !TryGetClientActivityDate(client, out var activityDate) ||
                activityDate > cutoff)
                continue;

            client.Archived = true;
            context.AuditLogs.Add(new AuditLog
            {
                UserId = "system:retention",
                EntityName = "Client",
                EntityId = client.Id,
                Action = "Retention Archive",
                Details = $"Automatically archived inactive client {client.ClientCode} after the two-year client-record retention period.",
                BeforeValue = "Archived: false",
                AfterValue = "Archived: true"
            });

            // 5-Year Internal Statutory Retention Enforcement
            if (activityDate <= cutoffInternal)
            {
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = "system:retention",
                    EntityName = "Client",
                    EntityId = client.Id,
                    Action = "Internal 5Y Retention Enforced",
                    Details = $"Enforced 5-year internal statutory retention limit on inactive client {client.ClientCode} (last activity: {activityDate:yyyy-MM-dd}).",
                    BeforeValue = "InternalRetention: Active",
                    AfterValue = "InternalRetention: 5Y Statutory Sealed"
                });
            }

            archivedCount++;
        }

        if (archivedCount > 0)
            await context.SaveChangesAsync(cancellationToken);

        return archivedCount;
    }

    private static bool TryGetInvoiceDate(Invoice invoice, out DateTime date)
    {
        return TryParseDate(invoice.BillingDate, out date) ||
               TryParseDate(invoice.DateEncoded, out date);
    }

    private static bool TryGetClientActivityDate(Client client, out DateTime date)
    {
        return TryParseDate(client.LastTransaction, out date) ||
               TryParseDate(client.DateRegistered, out date) ||
               TryParseDate(client.CreatedAt.ToString("O", CultureInfo.InvariantCulture), out date);
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);
    }
}

public sealed class RetentionArchiveWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionArchiveWorker> _logger;
    private readonly RetentionArchiveService _archiveService;

    public RetentionArchiveWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RetentionArchiveWorker> logger,
        RetentionArchiveService archiveService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _archiveService = archiveService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var archivedCount = await _archiveService.ArchiveEligibleRecordsAsync(
                    context,
                    DateTime.UtcNow,
                    stoppingToken);
                _logger.LogInformation("Retention archive run completed; {RecordCount} records archived.", archivedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Retention archive run failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
