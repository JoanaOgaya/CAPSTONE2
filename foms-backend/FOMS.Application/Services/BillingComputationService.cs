using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;


namespace FOMS.Application.Services;

/// <summary>
/// BillingComputationService — The single authoritative source of truth
/// for all FOMS financial computations (FR-010 to FR-025).
///
/// Official FOMS Billing Formula:
///   Subtotal       = FreightCharges + OtherCharges
///   VAT Amount     = Subtotal × 12%
///   Fuel surcharge = FreightCharges × FuelSurchargeRate
///   Total Amount   = Subtotal + VAT + Surcharge
///   Outstanding    = Total Amount − Total Payments Received
///
/// Rules:
///   - VAT is ALWAYS 12% of Subtotal. No exceptions.
///   - No invoice may have VAT = 0 when Subtotal > 0.
///   - No invoice may have Balance < 0.
///   - Aging is always computed dynamically from today's date.
///   - Client.CurrentBalance is always the SUM of unpaid invoice balances.
/// </summary>
public class WaybillBillingBreakdown
{
    public string WaybillNumber { get; set; } = string.Empty;
    public int Pieces { get; set; } = 1;
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal ActualWeight { get; set; }
    public decimal VolumetricWeight { get; set; }
    public decimal ChargeableWeight { get; set; }
    public string DeliveryArea { get; set; } = "NCR";
    public decimal MinimumKg { get; set; } = 5m;
    public decimal MinimumRate { get; set; } = 200m;
    public decimal ExcessRatePerKg { get; set; } = 20m;
    public decimal ExcessKg { get; set; }
    public decimal Freight { get; set; }
    public decimal DeclaredValue { get; set; }
    public decimal ValuationRate { get; set; } = 0.01m;
    public decimal ValuationCharge { get; set; }
    public decimal OdaCharge { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 0.12m;
    public decimal VatAmount { get; set; }
    public decimal FuelSurchargeRate { get; set; } = 0.15m;
    public decimal FuelSurcharge { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsSuspect { get; set; }
    public string? SuspectReason { get; set; }
}

public static class BillingComputationService
{
    public const double VAT_RATE = 0.12;
    public const decimal DEFAULT_FUEL_SURCHARGE_RATE = 0.15m;
    public const decimal DEFAULT_VALUATION_RATE = 0.01m;

    // ─────────────────────────────────────────────────────────────────
    // WAYBILL RATING ENGINE (CW, Freight, Valuation, ODA, VAT, Fuel, Total)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the official rating for a single waybill:
    ///   CW       = max((L * W * H * pieces) / 3500.0, actual_kg)
    ///   Freight  = Rmin + max(0, CW - 5) * Rexcess
    ///   Subtotal = Freight + 1% declared value + ODA
    ///   VAT      = 12% of Subtotal
    ///   Fuel     = f * Freight
    ///   Total    = Subtotal + VAT + Fuel
    /// </summary>
    public static WaybillBillingBreakdown ComputeWaybillCost(
        int pieces,
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        decimal actualWeight,
        decimal declaredValue,
        string deliveryArea,
        decimal minimumRate,
        decimal excessRatePerKg,
        decimal odaRate = 0m,
        decimal fuelSurchargeRate = 0.15m,
        decimal valuationRate = 0.01m,
        decimal minimumKg = 5m,
        string waybillNumber = "")
    {
        // Volumetric weight = (L * W * H * pieces) / 3500
        decimal volumetricWeight = (lengthCm > 0 && widthCm > 0 && heightCm > 0 && pieces > 0)
            ? Math.Round((lengthCm * widthCm * heightCm * (decimal)pieces) / 3500m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        // CW = max((L*W*H*pieces)/3500, actual kg)
        decimal chargeableWeight = Math.Max(volumetricWeight, actualWeight);

        // Freight = Rmin + max(0, CW - 5) * Rexcess
        decimal excessKg = Math.Max(0m, chargeableWeight - minimumKg);
        decimal freight = minimumRate + (excessKg * excessRatePerKg);

        // Valuation = 1% declared value
        decimal valuationCharge = Math.Round(declaredValue * valuationRate, 2, MidpointRounding.AwayFromZero);

        // Subtotal = Freight + 1% declared value + ODA
        decimal subtotal = freight + valuationCharge + odaRate;

        // VAT = 12% of Subtotal
        decimal vatAmount = Math.Round(subtotal * (decimal)VAT_RATE, 2, MidpointRounding.AwayFromZero);

        // Fuel = f * Freight
        decimal fuelSurcharge = Math.Round(freight * fuelSurchargeRate, 2, MidpointRounding.AwayFromZero);

        // Total = Subtotal + VAT + Fuel
        decimal total = subtotal + vatAmount + fuelSurcharge;

        var breakdown = new WaybillBillingBreakdown
        {
            WaybillNumber = waybillNumber,
            Pieces = pieces > 0 ? pieces : 1,
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm,
            ActualWeight = actualWeight,
            VolumetricWeight = volumetricWeight,
            ChargeableWeight = chargeableWeight,
            DeliveryArea = string.IsNullOrWhiteSpace(deliveryArea) ? "NCR" : deliveryArea,
            MinimumKg = minimumKg,
            MinimumRate = minimumRate,
            ExcessRatePerKg = excessRatePerKg,
            ExcessKg = excessKg,
            Freight = freight,
            DeclaredValue = declaredValue,
            ValuationRate = valuationRate,
            ValuationCharge = valuationCharge,
            OdaCharge = odaRate,
            Subtotal = subtotal,
            VatRate = (decimal)VAT_RATE,
            VatAmount = vatAmount,
            FuelSurchargeRate = fuelSurchargeRate,
            FuelSurcharge = fuelSurcharge,
            TotalAmount = total
        };

        // Pre-alert on suspect computation (BIL-06) / Zero-Negative checks
        if (freight <= 0m || subtotal <= 0m || total <= 0m || chargeableWeight <= 0m)
        {
            breakdown.IsSuspect = true;
            breakdown.SuspectReason = $"Zero or negative amount computed: Freight={freight:N2}, Subtotal={subtotal:N2}, Total={total:N2}";
        }
        else if (chargeableWeight > 1000m || total > 500000m)
        {
            breakdown.IsSuspect = true;
            breakdown.SuspectReason = $"BIL-06 Suspect parameter: CW={chargeableWeight}kg, Total={total:N2}";
        }

        return breakdown;
    }

    /// <summary>
    /// Validates discount requirements: discounts around 10% require recorded CEO or President approval.
    /// </summary>
    public static (bool isValid, string? error) ValidateDiscountApproval(decimal discountAmount, decimal discountRate, string? approver)
    {
        if (discountAmount <= 0m && discountRate <= 0m)
            return (true, null);

        if (string.IsNullOrWhiteSpace(approver))
            return (false, "Discounts require documented CEO or President approval.");

        var norm = approver.Trim().ToLowerInvariant();
        bool isCeoOrPresident = norm.Contains("ceo") || norm.Contains("president") || norm.Contains("chief executive");
        if (!isCeoOrPresident)
            return (false, $"Discount approval by '{approver}' is invalid. Discounts require recorded CEO or President approval.");

        return (true, null);
    }

    // ─────────────────────────────────────────────────────────────────
    // INVOICE COMPUTATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the VAT amount: Subtotal × 12%, rounded to 2 decimal places.
    /// </summary>
    public static decimal ComputeVat(decimal subtotal)
        => Math.Round(subtotal * (decimal)VAT_RATE, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Compute the fuel surcharge against freight only.
    /// </summary>
    public static decimal ComputeFuelSurcharge(decimal freightCharges, decimal fuelSurchargeRate)
        => Math.Round(freightCharges * fuelSurchargeRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Compute Total Amount = Subtotal + VAT + Surcharge.
    /// </summary>
    public static decimal ComputeTotalAmount(decimal subtotal, decimal vatAmount, decimal surcharge)
        => subtotal + vatAmount + surcharge;

    /// <summary>
    /// Compute Outstanding Balance = TotalAmount − AmountPaid. Never goes below 0.
    /// </summary>
    public static decimal ComputeOutstandingBalance(decimal totalAmount, decimal amountPaid)
        => Math.Max(0m, totalAmount - amountPaid);

    // ─────────────────────────────────────────────────────────────────
    // STATUS ENGINE
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determine payment status from the outstanding balance. Aging is tracked separately.
    /// </summary>
    public static string ComputePaymentStatus(decimal balance, decimal amountPaid, string dueDate)
    {
        if (balance <= 0m)
            return "Paid";

        return "Unpaid";
    }

    // ─────────────────────────────────────────────────────────────────
    // AGING ENGINE
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the number of days the invoice is past due.
    /// Returns 0 if not yet overdue.
    /// Always computed from today — never stored statically.
    /// </summary>
    public static int ComputeDaysOverdue(string dueDate)
    {
        if (!DateTime.TryParse(dueDate, out var parsedDue))
            return 0;
        var days = (int)(DateTime.UtcNow.Date - parsedDue.Date).TotalDays;
        return Math.Max(0, days);
    }

    /// <summary>
    /// Compute the aging bucket from the due date.
    /// Buckets: "Current" | "1-30" | "31-60" | "61-90" | "90+"
    /// Always computed from today — never stored statically.
    /// </summary>
    public static string ComputeAgingBucket(string dueDate)
    {
        var days = ComputeDaysOverdue(dueDate);
        if (days == 0) return "Current";
        if (days <= 30) return "1-30";
        if (days <= 60) return "31-60";
        if (days <= 90) return "61-90";
        return "90+";
    }

    /// <summary>
    /// Derived Aging Buckets per specification:
    /// "Aging buckets derived, not stored: Not Yet Due, Due Soon (configurable), 1-30, 31-60, 61-90, 90+."
    /// </summary>
    public static string ComputeDerivedAgingBucket(string? dueDate, int dueSoonDays = 7)
    {
        if (string.IsNullOrWhiteSpace(dueDate) || !DateTime.TryParse(dueDate, out var parsedDue))
            return "Not Yet Due";

        var today = DateTime.UtcNow.Date;
        var due = parsedDue.Date;

        if (due > today)
        {
            var daysUntilDue = (due - today).Days;
            if (daysUntilDue <= dueSoonDays)
                return "Due Soon";
            return "Not Yet Due";
        }

        var overdueDays = (today - due).Days;
        if (overdueDays == 0)
            return "Due Soon";
        if (overdueDays <= 30) return "1-30";
        if (overdueDays <= 60) return "31-60";
        if (overdueDays <= 90) return "61-90";
        return "90+";
    }

    /// <summary>
    /// Returns true if the invoice due date has passed today.
    /// </summary>
    public static bool IsOverdue(string dueDate)
    {
        if (!DateTime.TryParse(dueDate, out var parsedDue))
            return false;
        return DateTime.UtcNow.Date > parsedDue.Date;
    }

    // ─────────────────────────────────────────────────────────────────
    // DYNAMIC AGING REFRESH — called on every GET
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refresh aging, days-overdue, and payment status on an invoice object
    /// WITHOUT saving to the database. Used when serving GET responses so
    /// that stale DB values are never returned to the client.
    /// </summary>
    public static void RefreshInvoiceAging(Invoice invoice)
    {
        if (invoice.Balance <= 0m)
        {
            invoice.PaymentStatus = "Paid";
            invoice.DaysOverdue = 0;
            invoice.AgingBucket = "Current";
            return;
        }

        invoice.DaysOverdue = ComputeDaysOverdue(invoice.DueDate);
        invoice.AgingBucket = ComputeAgingBucket(invoice.DueDate);
        invoice.PaymentStatus = ComputePaymentStatus(invoice.Balance, invoice.AmountPaid, invoice.DueDate);
    }

    // ─────────────────────────────────────────────────────────────────
    // CLIENT BALANCE ENGINE
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronize Client.CurrentBalance so it always equals
    /// the sum of all unpaid Invoice balances for that client.
    ///
    /// This is the ONLY authoritative way to update a client's balance.
    /// No feature should manually do: client.CurrentBalance -= amount.
    /// </summary>
    public static async Task SyncClientBalanceAsync(
        string clientId,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null) return;

        var outstandingBalance = await context.Invoices
            .Where(i => i.ClientId == clientId)
            .SumAsync(i => i.Balance, cancellationToken);

        var totalBilled = await context.Invoices
            .Where(i => i.ClientId == clientId)
            .SumAsync(i => i.TotalAmount, cancellationToken);

        var totalPaid = await context.Invoices
            .Where(i => i.ClientId == clientId)
            .SumAsync(i => i.AmountPaid, cancellationToken);

        client.CurrentBalance = outstandingBalance;
        client.TotalBilled = totalBilled;
        client.TotalPaid = totalPaid;
        client.LastTransaction = DateTime.UtcNow.ToString("MMM dd, yyyy");
    }

    public static async Task ApplyAvailableCreditAsync(
        string clientId,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var client = await context.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client == null || client.AvailableCredit <= 0m)
            return;

        var openInvoices = await context.Invoices
            .Where(i => i.ClientId == clientId && i.Balance > 0m)
            .OrderBy(i => i.BillingDate)
            .ThenBy(i => i.InvoiceNo)
            .ToListAsync(cancellationToken);

        foreach (var invoice in openInvoices)
        {
            var applied = Math.Min(client.AvailableCredit, invoice.Balance);
            if (applied <= 0m)
                continue;

            invoice.CreditApplied += applied;
            client.AvailableCredit -= applied;
            RecalculateInvoice(invoice);
            await SyncBillingInvoiceAndReceivableAsync(invoice, context, cancellationToken);

            if (client.AvailableCredit <= 0m)
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // PAYMENT VALIDATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validate a payment amount before applying it to an invoice.
    /// Amount mismatches are recorded as exceptions but cannot be applied.
    /// </summary>
    public static string? ValidatePaymentAmount(decimal amount, decimal outstandingBalance)
    {
        if (amount <= 0m)
            return "Payment amount must be greater than zero.";

        if (amount != outstandingBalance)
            return $"Payment amount ({amount:N2}) must exactly match the outstanding balance ({outstandingBalance:N2}).";

        return null; // valid
    }

    // ─────────────────────────────────────────────────────────────────
    // DYNAMIC INVOICE & RECEIVABLE SYNCHRONIZATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronize standard Billing Invoice (BI-...) and ReceivableBalance when an Invoice is updated.
    /// </summary>
    public static async Task SyncBillingInvoiceAndReceivableAsync(
        Invoice invoice,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Determine the corresponding Billing Invoice No
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

        // Only sync if they are actually different (i.e. sync client invoice to billing invoice)
        if (invoice.InvoiceNo != billingInvoiceNo)
        {
            var billingInvoice = await context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceNo == billingInvoiceNo, cancellationToken);

            if (billingInvoice != null)
            {
                // Sync financial values
                billingInvoice.FreightCharges = invoice.FreightCharges;
                billingInvoice.OtherCharges = invoice.OtherCharges;
                billingInvoice.Subtotal = invoice.Subtotal;
                billingInvoice.VatRate = invoice.VatRate;
                billingInvoice.VatAmount = invoice.VatAmount;
                billingInvoice.Surcharge = invoice.Surcharge;
                billingInvoice.FuelSurchargeRate = invoice.FuelSurchargeRate;
                billingInvoice.TotalAmount = invoice.TotalAmount;
                billingInvoice.AmountPaid = invoice.AmountPaid;
                billingInvoice.CreditMemoAmount = invoice.CreditMemoAmount;
                billingInvoice.CreditApplied = invoice.CreditApplied;
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
                // If the billing invoice does not exist, create it (e.g., when a client invoice is newly created)
                var billingClientId = invoice.ClientId.StartsWith("CL-") 
                    ? invoice.ClientId.Replace("CL-", "CA-") 
                    : invoice.ClientId;

                billingInvoice = new Invoice
                {
                    Id = billingInvoiceNo,
                    InvoiceNo = billingInvoiceNo,
                    ClientId = billingClientId,
                    ClientName = ((invoice.ClientName ?? "Client").Contains("Account") ? (invoice.ClientName ?? "Client") : (invoice.ClientName ?? "Client") + " Account"),
                    BillingDate = invoice.BillingDate,
                    DueDate = invoice.DueDate,
                    FreightCharges = invoice.FreightCharges,
                    OtherCharges = invoice.OtherCharges,
                    Subtotal = invoice.Subtotal,
                    VatRate = invoice.VatRate,
                    VatAmount = invoice.VatAmount,
                    Surcharge = invoice.Surcharge,
                    FuelSurchargeRate = invoice.FuelSurchargeRate,
                    TotalAmount = invoice.TotalAmount,
                    AmountPaid = invoice.AmountPaid,
                    CreditMemoAmount = invoice.CreditMemoAmount,
                    CreditApplied = invoice.CreditApplied,
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

        // 2. Synchronize ReceivableBalance record
        // receivableClientId: always use CA- prefix (billing account ID)
        var receivableClientId = invoice.ClientId.StartsWith("CL-") 
            ? invoice.ClientId.Replace("CL-", "CA-") 
            : invoice.ClientId;

        // receivableInvoiceId: always use the BI- invoice number (authoritative FK)
        // Use InvoiceNo (not Id) because BI- invoices created by the sync use InvoiceNo as their Id,
        // but direct billing invoices also need InvoiceNo as the stable identifier.
        var receivableInvoiceId = invoice.InvoiceNo.StartsWith("BI-") ? invoice.InvoiceNo : billingInvoiceNo;

        var receivable = await context.ReceivableBalances
            .FirstOrDefaultAsync(r => r.InvoiceId == receivableInvoiceId, cancellationToken);

        // Remove receivable if archived OR fully paid (balance == 0)
        if (invoice.Balance <= 0m)
        {
            if (receivable != null)
            {
                context.ReceivableBalances.Remove(receivable);
            }
        }
        else
        {
            DateTime? dueDate = null;
            if (!string.IsNullOrWhiteSpace(invoice.DueDate) && DateTime.TryParse(invoice.DueDate, out var parsedDue))
            {
                dueDate = parsedDue;
            }

            // On Sent: create receivable (no due date), recognize accrual revenue.
            // On Received: due = received date + 30 days (client-configurable).
            if (string.Equals(invoice.DocumentStatus, "Sent", StringComparison.OrdinalIgnoreCase) && invoice.ReceivedDate == null)
            {
                dueDate = null;
            }
            else if (string.Equals(invoice.DocumentStatus, "Received", StringComparison.OrdinalIgnoreCase) && invoice.ReceivedDate.HasValue)
            {
                var client = await context.Clients.FirstOrDefaultAsync(c => c.Id == invoice.ClientId, cancellationToken);
                int termDays = client?.CreditTermDays ?? 30;
                dueDate = invoice.ReceivedDate.Value.AddDays(termDays);
                invoice.DueDate = dueDate.Value.ToString("yyyy-MM-dd");
            }

            if (receivable != null)
            {
                receivable.BalanceAmount = invoice.Balance;
                receivable.DueDate = dueDate;
                context.ReceivableBalances.Update(receivable);
            }
            else
            {
                // Create new receivable record for this invoice
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

    /// <summary>
    /// Accrual Revenue Recognition on Invoice Sent:
    /// Debits Accounts Receivable (1100), Credits Freight Revenue (4000) and Output VAT (2150).
    /// </summary>
    public static async Task RecognizeAccrualRevenueAsync(
        Invoice invoice,
        IApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        if (invoice.AccrualRevenueRecognized) return;

        var arAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "1100", cancellationToken);
        if (arAccount == null)
        {
            arAccount = new ChartOfAccount { Id = "COA-1100", AccountCode = "1100", Name = "Accounts Receivable", Type = "Asset", NormalBalance = "Debit" };
            context.ChartOfAccounts.Add(arAccount);
        }

        var revAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "4000", cancellationToken);
        if (revAccount == null)
        {
            revAccount = new ChartOfAccount { Id = "COA-4000", AccountCode = "4000", Name = "Freight & Logistics Revenue", Type = "Revenue", NormalBalance = "Credit" };
            context.ChartOfAccounts.Add(revAccount);
        }

        var vatAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "2150", cancellationToken);
        if (vatAccount == null)
        {
            vatAccount = new ChartOfAccount { Id = "COA-2150", AccountCode = "2150", Name = "Output VAT Payable", Type = "Liability", NormalBalance = "Credit" };
            context.ChartOfAccounts.Add(vatAccount);
        }

        var jeId = Guid.NewGuid().ToString();
        var je = new JournalEntry
        {
            Id = jeId,
            EntryNumber = $"JE-ACCRUAL-{invoice.InvoiceNo}-{jeId[..8]}",
            InvoiceId = invoice.Id,
            Date = DateTime.UtcNow,
            Description = $"Accrual Revenue Recognition on Invoice Sent: {invoice.InvoiceNo} ({invoice.ClientName})",
            Posted = true
        };
        context.JournalEntries.Add(je);

        // Debit Accounts Receivable
        context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
        {
            Id = Guid.NewGuid().ToString(),
            JournalEntryId = jeId,
            AccountId = arAccount.Id,
            Debit = invoice.TotalAmount,
            Credit = 0m
        });

        // Credit Revenue (Subtotal + Surcharge)
        var netRevenue = invoice.Subtotal + invoice.Surcharge;
        if (netRevenue > 0)
        {
            context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = jeId,
                AccountId = revAccount.Id,
                Debit = 0m,
                Credit = netRevenue
            });
        }

        // Credit Output VAT
        if (invoice.VatAmount > 0)
        {
            context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = jeId,
                AccountId = vatAccount.Id,
                Debit = 0m,
                Credit = invoice.VatAmount
            });
        }

        invoice.AccrualRevenueRecognized = true;
        invoice.AccrualJournalEntryId = jeId;
    }

    /// <summary>
    /// Reverses the accrual revenue journal entry when an Approved invoice is Voided before dispatch.
    /// Debits Revenue and Output VAT, Credits Accounts Receivable.
    /// </summary>
    public static async Task ReverseAccrualRevenueAsync(
        Invoice invoice,
        IApplicationDbContext context,
        string voidReason,
        CancellationToken cancellationToken = default)
    {
        if (!invoice.AccrualRevenueRecognized || string.IsNullOrEmpty(invoice.AccrualJournalEntryId)) return;

        var arAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "1100", cancellationToken);
        var revAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "4000", cancellationToken);
        var vatAccount = await context.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountCode == "2150", cancellationToken);

        var revJeId = Guid.NewGuid().ToString();
        var je = new JournalEntry
        {
            Id = revJeId,
            EntryNumber = $"JE-REVERSAL-{invoice.InvoiceNo}",
            Date = DateTime.UtcNow,
            Description = $"Reversal of Accrual Revenue Recognition for Voided Invoice: {invoice.InvoiceNo} ({invoice.ClientName}). Reason: {voidReason}",
            Posted = true
        };
        context.JournalEntries.Add(je);

        // Credit Accounts Receivable (Reverse the original Debit)
        context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
        {
            Id = Guid.NewGuid().ToString(),
            JournalEntryId = revJeId,
            AccountId = arAccount?.Id ?? "COA-1100",
            Debit = 0m,
            Credit = invoice.TotalAmount
        });

        // Debit Revenue (Reverse the original Credit)
        var netRevenue = invoice.Subtotal + invoice.Surcharge;
        if (netRevenue > 0)
        {
            context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = revJeId,
                AccountId = revAccount?.Id ?? "COA-4000",
                Debit = netRevenue,
                Credit = 0m
            });
        }

        // Debit Output VAT (Reverse the original Credit)
        if (invoice.VatAmount > 0)
        {
            context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = revJeId,
                AccountId = vatAccount?.Id ?? "COA-2150",
                Debit = invoice.VatAmount,
                Credit = 0m
            });
        }

        invoice.AccrualRevenueRecognized = false;
        invoice.AccrualJournalEntryId = null;
    }

    // ─────────────────────────────────────────────────────────────────
    // FULL INVOICE RECALCULATION — called on create and update
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fully recompute all financial fields on an invoice:
    /// Subtotal, VAT, Surcharge, TotalAmount, Balance, Status, Aging.
    /// Call this any time FreightCharges, OtherCharges, or DueDate changes.
    /// </summary>
    public static void RecalculateInvoice(Invoice invoice)
    {
        // Fallback for seeded/legacy invoices where TotalAmount was manually set but Freight/Other charges were 0.
        // Derive freight so VAT and the fuel surcharge together preserve the stored total.
        if (invoice.FreightCharges == 0 && invoice.OtherCharges == 0 && invoice.TotalAmount > 0)
        {
            invoice.Subtotal = invoice.TotalAmount / (1m + (decimal)VAT_RATE + invoice.FuelSurchargeRate);
            invoice.FreightCharges = Math.Round(invoice.Subtotal, 2, MidpointRounding.AwayFromZero);
        }

        invoice.Subtotal = invoice.FreightCharges + invoice.OtherCharges;
        invoice.VatRate = (decimal)VAT_RATE;
        invoice.VatAmount = ComputeVat(invoice.Subtotal);
        invoice.Surcharge = ComputeFuelSurcharge(invoice.FreightCharges, invoice.FuelSurchargeRate);
        invoice.TotalAmount = ComputeTotalAmount(invoice.Subtotal, invoice.VatAmount, invoice.Surcharge);
        invoice.TotalAmount = Math.Max(0m, invoice.TotalAmount - invoice.CreditMemoAmount - invoice.DiscountAmount);
        invoice.Balance = ComputeOutstandingBalance(invoice.TotalAmount, invoice.AmountPaid + invoice.CreditApplied);
        invoice.DaysOverdue = ComputeDaysOverdue(invoice.DueDate);
        invoice.AgingBucket = ComputeAgingBucket(invoice.DueDate);
        invoice.PaymentStatus = ComputePaymentStatus(invoice.Balance, invoice.AmountPaid, invoice.DueDate);
        invoice.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
}
