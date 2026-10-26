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
///   Surcharge      = (Subtotal + VAT) × 5%  [only if invoice is overdue at creation]
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
public static class BillingComputationService
{
    public const double VAT_RATE = 0.12;
    public const double SURCHARGE_RATE = 0.05;

    /// <summary>
    /// Uniform Money Rounding Rule:
    /// Half-Up rounding (MidpointRounding.AwayFromZero) to 2 decimal places per line.
    /// Assumption documented for confirmation with Speedex finance team.
    /// </summary>
    public static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ─────────────────────────────────────────────────────────────────
    // INVOICE COMPUTATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the VAT amount: Subtotal × 12%, rounded to 2 decimal places using MidpointRounding.AwayFromZero.
    /// </summary>
    public static decimal ComputeVat(decimal subtotal)
        => RoundMoney(subtotal * (decimal)VAT_RATE);

    /// <summary>
    /// Compute surcharge (5%) if the invoice due date has already passed.
    /// Surcharge is applied on (Subtotal + VAT).
    /// </summary>
    public static decimal ComputeSurcharge(decimal subtotal, decimal vatAmount, string dueDate)
    {
        if (DateTime.TryParse(dueDate, out var parsedDue) && DateTime.UtcNow.Date > parsedDue.Date)
        {
            return RoundMoney((subtotal + vatAmount) * (decimal)SURCHARGE_RATE);
        }
        return 0m;
    }

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
    /// Determine the payment status from balance, amountPaid, and due date.
    ///   Balance = 0                          → Paid
    ///   Balance > 0, Overdue                 → Overdue
    ///   Balance > 0, Not Overdue             → Unpaid
    /// </summary>
    public static string ComputePaymentStatus(decimal balance, decimal amountPaid, string dueDate)
    {
        if (balance <= 0m)
            return "Paid";

        var isOverdue = IsOverdue(dueDate);
        if (isOverdue)
            return "Overdue";

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

    public static void RefreshInvoiceAging(FOMS.Domain.Entities.SqlServer.Invoice invoice)
    {
        if (invoice == null) return;
        if (invoice.Balance <= 0m)
        {
            invoice.PaymentStatus = "Paid";
            invoice.DaysOverdue = 0;
            invoice.AgingBucket = "Current";
            return;
        }

        var dueDateStr = invoice.DueDate?.ToString("yyyy-MM-dd") ?? invoice.DueDateStr;
        invoice.DaysOverdue = ComputeDaysOverdue(dueDateStr);
        invoice.AgingBucket = ComputeAgingBucket(dueDateStr);
        invoice.PaymentStatus = ComputePaymentStatus(invoice.Balance, invoice.AmountPaid, dueDateStr);
    }

    // ─────────────────────────────────────────────────────────────────
    // CLIENT BALANCE ENGINE
    // ─────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────
    // PURE CAPSTONE 2 FORMULA COMPUTATIONS
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes Chargeable Weight: max(actualWeight, volumetricWeight)
    /// Volumetric = (L × W × H × pieces) / 3500 (per NFR-12 & BIL-02)
    /// </summary>
    public static decimal ComputeChargeableWeight(decimal actualWeight, decimal lengthCm, decimal widthCm, decimal heightCm, int pieces = 1)
    {
        if (lengthCm <= 0 || widthCm <= 0 || heightCm <= 0 || pieces <= 0)
            return RoundMoney(Math.Max(0m, actualWeight));

        var volumetric = (lengthCm * widthCm * heightCm * pieces) / 3500m;
        return RoundMoney(Math.Max(actualWeight, volumetric));
    }

    /// <summary>
    /// Computes Freight Charges: R_min + max(0, CW - 5) × R_excess
    /// </summary>
    public static decimal ComputeFreight(decimal chargeableWeight, decimal baseRateMin, decimal excessRatePerKg, decimal minWeightKg = 5.0m)
    {
        var excessWeight = Math.Max(0m, chargeableWeight - minWeightKg);
        var freight = baseRateMin + (excessWeight * excessRatePerKg);
        return RoundMoney(freight);
    }

    /// <summary>
    /// Computes Valuation Charge (1% of Declared Value)
    /// </summary>
    public static decimal ComputeValuation(decimal declaredValue, decimal rate = 0.01m)
        => RoundMoney(Math.Max(0m, declaredValue) * rate);

    /// <summary>
    /// Computes Fuel Surcharge: f × Freight
    /// </summary>
    public static decimal ComputeFuelSurcharge(decimal freight, decimal fuelSurchargeRate)
        => RoundMoney(Math.Max(0m, freight) * Math.Max(0m, fuelSurchargeRate));

    /// <summary>
    /// Computes Grand Total: Subtotal + VAT + Fuel Surcharge
    /// </summary>
    public static decimal ComputeGrandTotal(decimal subtotal, decimal vatAmount, decimal fuelSurcharge)
        => RoundMoney(subtotal + vatAmount + fuelSurcharge);

    // ─────────────────────────────────────────────────────────────────
    // PAYMENT VALIDATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validate a payment amount before recording it.
    /// Returns null if valid; returns an error message string if invalid.
    /// </summary>
    public static string? ValidatePaymentAmount(decimal amount, decimal outstandingBalance)
    {
        if (amount <= 0m)
            return "Payment amount must be greater than zero.";

        if (amount > outstandingBalance)
            return $"Payment amount ({amount:N2}) exceeds the outstanding balance ({outstandingBalance:N2}). Overpayments are not allowed.";

        return null; // valid
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
        // We retroactively set FreightCharges so the recalculation preserves the TotalAmount.
        if (invoice.FreightCharges == 0 && invoice.OtherCharges == 0 && invoice.TotalAmount > 0)
        {
            invoice.Subtotal = invoice.TotalAmount / (decimal)(1 + VAT_RATE);
            invoice.FreightCharges = RoundMoney(invoice.Subtotal);
        }

        invoice.Subtotal = invoice.FreightCharges + invoice.OtherCharges;
        invoice.VatRate = VAT_RATE;
        invoice.VatAmount = ComputeVat(invoice.Subtotal);
        invoice.Surcharge = ComputeSurcharge(invoice.Subtotal, invoice.VatAmount, invoice.DueDate);
        invoice.TotalAmount = ComputeTotalAmount(invoice.Subtotal, invoice.VatAmount, invoice.Surcharge);
        invoice.Balance = ComputeOutstandingBalance(invoice.TotalAmount, invoice.AmountPaid);
        invoice.DaysOverdue = ComputeDaysOverdue(invoice.DueDate);
        invoice.AgingBucket = ComputeAgingBucket(invoice.DueDate);
        invoice.PaymentStatus = ComputePaymentStatus(invoice.Balance, invoice.AmountPaid, invoice.DueDate);
        invoice.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
}

