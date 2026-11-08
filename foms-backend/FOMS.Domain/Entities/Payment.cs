using System;

namespace FOMS.Domain.Entities;

public class Payment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OrNumber { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string PaymentDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public string Flow { get; set; } = "inflow"; // "inflow" or "outflow"
    public string PaymentMethod { get; set; } = "Bank Transfer"; // "Check" | "Bank Transfer" | "Cash"
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? ProofImageUrl { get; set; }
    public string? Remarks { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string? RecordedByEmployeeId { get; set; }
    public string DateRecorded { get; set; } = string.Empty;

    // Retained for historical payment records; current payments use staff validation only.
    public string? SpeedPayReference { get; set; }
    public string? PayMongoReference { get; set; }
    public string PaymentStatus { get; set; } = "Pending Finance Validation";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidatedAt { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public string? ExceptionResolutionReason { get; set; }
}
