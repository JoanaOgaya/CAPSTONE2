using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;

namespace FOMS.Application.Services;

/// <summary>
/// Separates database persistence synchronization out of BillingComputationService,
/// ensuring BillingComputationService remains a pure calculation engine (PDF NFR-12, Requirement 7).
/// </summary>
public static class BillingSyncService
{
    public static async Task SyncBillingInvoiceAndReceivableAsync(
        Invoice invoice,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        string billingInvoiceNo;
        var parts = invoice.InvoiceNo.Split('-');
        if (parts.Length >= 3)
        {
            billingInvoiceNo = $"BI-{parts[1]}-{parts[2]}";
        }
        else
        {
            billingInvoiceNo = invoice.InvoiceNo;
        }

        if (invoice.InvoiceNo != billingInvoiceNo)
        {
            var billingInvoice = await context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceNo == billingInvoiceNo, cancellationToken);

            if (billingInvoice != null)
            {
                billingInvoice.FreightCharges = invoice.FreightCharges;
                billingInvoice.OtherCharges = invoice.OtherCharges;
                billingInvoice.Subtotal = invoice.Subtotal;
                billingInvoice.VatRate = invoice.VatRate;
                billingInvoice.VatAmount = invoice.VatAmount;
                billingInvoice.Surcharge = invoice.Surcharge;
                billingInvoice.TotalAmount = invoice.TotalAmount;
                billingInvoice.AmountPaid = invoice.AmountPaid;
                billingInvoice.Balance = invoice.Balance;
                billingInvoice.DaysOverdue = invoice.DaysOverdue;
                billingInvoice.AgingBucket = invoice.AgingBucket;
                billingInvoice.PaymentStatus = invoice.PaymentStatus;
                billingInvoice.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd");
                billingInvoice.UpdatedBy = invoice.UpdatedBy;
                billingInvoice.Archived = invoice.Archived;

                context.Invoices.Update(billingInvoice);
            }
            else
            {
                var billingClientId = !string.IsNullOrEmpty(invoice.ClientId) && invoice.ClientId.StartsWith("CL-") 
                    ? invoice.ClientId.Replace("CL-", "CA-") 
                    : (invoice.ClientId ?? string.Empty);

                billingInvoice = new Invoice
                {
                    Id = billingInvoiceNo,
                    InvoiceNo = billingInvoiceNo,
                    ClientId = billingClientId,
                    ClientName = !string.IsNullOrEmpty(invoice.ClientName)
                        ? (invoice.ClientName.Contains("Account") ? invoice.ClientName : invoice.ClientName + " Account")
                        : "Client Account",
                    BillingDate = invoice.BillingDate,
                    DueDate = invoice.DueDate,
                    FreightCharges = invoice.FreightCharges,
                    OtherCharges = invoice.OtherCharges,
                    Subtotal = invoice.Subtotal,
                    VatRate = invoice.VatRate,
                    VatAmount = invoice.VatAmount,
                    Surcharge = invoice.Surcharge,
                    TotalAmount = invoice.TotalAmount,
                    AmountPaid = invoice.AmountPaid,
                    Balance = invoice.Balance,
                    PaymentStatus = invoice.PaymentStatus,
                    AgingBucket = invoice.AgingBucket,
                    DaysOverdue = invoice.DaysOverdue,
                    Description = invoice.Description,
                    EncodedBy = invoice.EncodedBy,
                    DateEncoded = invoice.DateEncoded,
                    LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    UpdatedBy = invoice.UpdatedBy,
                    Archived = invoice.Archived
                };
                context.Invoices.Add(billingInvoice);
            }
        }

        var receivableClientId = !string.IsNullOrEmpty(invoice.ClientId) && invoice.ClientId.StartsWith("CL-") 
            ? invoice.ClientId.Replace("CL-", "CA-") 
            : (invoice.ClientId ?? string.Empty);

        var receivableInvoiceId = (!string.IsNullOrEmpty(invoice.InvoiceNo) && invoice.InvoiceNo.StartsWith("BI-")) 
            ? invoice.InvoiceNo 
            : billingInvoiceNo;

        var receivable = await context.ReceivableBalances
            .FirstOrDefaultAsync(r => r.InvoiceId == receivableInvoiceId, cancellationToken);

        if (invoice.Archived || invoice.Balance <= 0m)
        {
            if (receivable != null)
            {
                context.ReceivableBalances.Remove(receivable);
            }
        }
        else
        {
            var dueDate = DateTime.TryParse(invoice.DueDate, out var parsedDue) ? parsedDue : DateTime.UtcNow;
            if (receivable != null)
            {
                receivable.BalanceAmount = invoice.Balance;
                receivable.DueDate = dueDate;
                context.ReceivableBalances.Update(receivable);
            }
            else
            {
                receivable = new ReceivableBalance
                {
                    ClientId = receivableClientId,
                    InvoiceId = receivableInvoiceId,
                    BalanceAmount = invoice.Balance,
                    DueDate = dueDate
                };
                context.ReceivableBalances.Add(receivable);
            }
        }
    }

    public static async Task SyncClientBalanceAsync(
        string clientId,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null) return;

        var outstandingBalance = await context.Invoices
            .Where(i => i.ClientId == clientId && !i.Archived)
            .SumAsync(i => i.Balance, cancellationToken);

        var totalBilled = await context.Invoices
            .Where(i => i.ClientId == clientId && !i.Archived)
            .SumAsync(i => i.TotalAmount, cancellationToken);

        var totalPaid = await context.Invoices
            .Where(i => i.ClientId == clientId && !i.Archived)
            .SumAsync(i => i.AmountPaid, cancellationToken);

        client.CurrentBalance = outstandingBalance;
        client.TotalBilled = totalBilled;
        client.TotalPaid = totalPaid;
        client.LastTransaction = DateTime.UtcNow.ToString("MMM dd, yyyy");
    }
}
