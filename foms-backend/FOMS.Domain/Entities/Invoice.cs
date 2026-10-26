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
    public double VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Surcharge { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid"; // "Paid" | "Unpaid" | "Overdue"
    public string? PaymentValidationStatus { get; set; } // "Pending Validation" | "Validated" | "Rejected" | "Returned for Correction"
    public string? AgingBucket { get; set; } = "Current"; // "Current" | "1-30" | "31-60" | "61-90" | "90+"
    public int DaysOverdue { get; set; }
    public string? Description { get; set; }
    public string? EncodedBy { get; set; }
    public string? DateEncoded { get; set; }
    public string? LastUpdated { get; set; }
    public string? UpdatedBy { get; set; }
    public bool Archived { get; set; } = false;
    public string? WaybillIds { get; set; }
}
