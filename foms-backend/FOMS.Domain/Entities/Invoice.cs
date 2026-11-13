using System;

namespace FOMS.Domain.Entities;

public class Invoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceNo { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public Client? Client { get; set; }
    public string? ClientName { get; set; }
    public string? BillingDate { get; set; }
    public string? DueDate { get; set; }
    public decimal FreightCharges { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 0.12m;
    public decimal VatAmount { get; set; }
    public decimal Surcharge { get; set; }
    public decimal FuelSurchargeRate { get; set; } = 0.15m;
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal CreditMemoAmount { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid"; // "Paid" | "Unpaid"
    public string? PaymentValidationStatus { get; set; } // "Pending Validation" | "Validated" | "Rejected" | "Returned for Correction"
    public string? AgingBucket { get; set; } = "Current"; // "Current" | "1-30" | "31-60" | "61-90" | "90+"
    public int DaysOverdue { get; set; }
    public string? Description { get; set; }
    public string? EncodedBy { get; set; }
    public string? DateEncoded { get; set; }
    public string? LastUpdated { get; set; }
    public string? UpdatedBy { get; set; }
    public bool Archived { get; set; } = false;
    public bool HasCtc { get; set; } = false;
    public string? CtcWaybills { get; set; }
    public string? WaybillIds { get; set; }
    public decimal DiscountAmount { get; set; } = 0m;
    public decimal DiscountRate { get; set; } = 0m;
    public string? DiscountApprovedBy { get; set; }
    public string? DiscountReason { get; set; }
    public bool IsSuspect { get; set; } = false;
    public string? SuspectReason { get; set; }
    public string? VoidReason { get; set; }
    public string? DocumentStatus { get; set; } = "Draft"; // "Draft", "For Review", "Approved", "Sent", "Received", "Disputed", "Void"
    public DateTime? SentDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string? ReviewRejectionReason { get; set; }
    public bool IsSampledForReview { get; set; } = false;
    public string? SampledBy { get; set; }
    public DateTime? SampledAt { get; set; }
    public string? DisputeReasonCode { get; set; }
    public string? DisputeRemarks { get; set; }
    public bool IsDisputed { get; set; } = false;
    public bool AccrualRevenueRecognized { get; set; } = false;
    public string? AccrualJournalEntryId { get; set; }
}
