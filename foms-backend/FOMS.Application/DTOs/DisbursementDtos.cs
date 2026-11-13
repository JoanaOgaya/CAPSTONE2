using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FOMS.Application.DTOs;

public static class DisbursementCategories
{
    public static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fuel",
        "Toll Fees",
        "Toll",
        "Parking",
        "Food Allowance",
        "Food allowance",
        "Vulcanizing",
        "Repairs and Maintenance",
        "Hotel",
        "Lodging",
        "Branch Visits",
        "Branch Visit",
        "Office Supplies",
        "Supplies",
        "Transportation",
        "Other",
        "Branch Visit - Lodging",
        "Branch Visit - Transportation",
        "Branch Visit - Supplies",
        "Branch Visit - Meals"
    };

    public static bool IsValidCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        if (AllowedCategories.Contains(category.Trim())) return true;
        // Allow supervisor branch visit items
        var cat = category.Trim().ToLowerInvariant();
        return cat.Contains("branch visit") || cat.Contains("supervisor");
    }
}

public static class LiquidationRejectionReasons
{
    public const string WrongReceiptDateOrAmount = "wrong receipt date or amount";
    public const string ManualFuelReceipt = "manual fuel receipt";
    public const string LateSubmission = "late submission";
    public const string OtherRemarks = "other with remarks";

    public static readonly string[] ValidReasons = new[]
    {
        WrongReceiptDateOrAmount,
        "wrong receipt date/amount",
        ManualFuelReceipt,
        LateSubmission,
        OtherRemarks,
        "other (remarks)"
    };

    public static bool IsValid(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;
        var r = reason.Trim().ToLowerInvariant();
        return ValidReasons.Any(v => v.Equals(r, StringComparison.OrdinalIgnoreCase));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Cash Advance DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class RequestCashAdvanceDto
{
    [Required]
    public string CourierName { get; set; } = string.Empty;
    public string TripReference { get; set; } = string.Empty;
    [Range(0.01, 100000000, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
    [Required]
    public string DepartmentCode { get; set; } = string.Empty;
    [Required]
    public string Category { get; set; } = "Fuel";
    public string LocationType { get; set; } = "NCR"; // NCR, Luzon, Out-of-Area
    public DateTime? ReturnDate { get; set; }
    public string? Notes { get; set; }
    public int FiscalYear { get; set; } = 2026;
    public int Month { get; set; } = 1;
}

public class CashAdvanceResponseDto
{
    public string AdvanceId { get; set; } = string.Empty;
    public string CourierName { get; set; } = string.Empty;
    public string TripReference { get; set; } = string.Empty;
    public decimal AmountGiven { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public bool IsLate { get; set; }
    public string Status { get; set; } = string.Empty; // Requested, Approved, Released, Liquidated, Rejected, Cancelled
    public bool RequiresCeoApproval { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Direct Disbursement DTOs (CSH-12)
// ─────────────────────────────────────────────────────────────────────────────

public class RequestDirectDisbursementDto
{
    [Required]
    public string DepartmentCode { get; set; } = string.Empty;
    [Required]
    public string Category { get; set; } = "Repairs and Maintenance";
    [Range(0.01, 100000000)]
    public decimal Amount { get; set; }
    [Required]
    public string Purpose { get; set; } = string.Empty;
    public int FiscalYear { get; set; } = 2026;
    public int Month { get; set; } = 1;
}

public class DirectDisbursementResponseDto
{
    public int DisbursementId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Requested, Approved, Released, Rejected, Cancelled
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int FiscalYear { get; set; }
    public int Month { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Reimbursement DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class SubmitReimbursementDto
{
    [Required]
    public string EmployeeId { get; set; } = string.Empty;
    [Required]
    public string EmployeeName { get; set; } = string.Empty;
    [Required]
    public string DepartmentCode { get; set; } = string.Empty;
    [Required]
    public string Category { get; set; } = "Food allowance";
    [Range(0.01, 100000000)]
    public decimal Amount { get; set; }
    [Required]
    public string Purpose { get; set; } = string.Empty;
    public string? ReceiptUrls { get; set; }
    public int FiscalYear { get; set; } = 2026;
    public int Month { get; set; } = 1;
}

public class ReimbursementResponseDto
{
    public int ReimbursementId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Submitted, Validated, Approved, Paid, Rejected
    public DateTime SubmittedAt { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? PaidBy { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? RejectionReason { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Liquidation DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class SubmitLiquidationDto
{
    [Required]
    public string AdvanceId { get; set; } = string.Empty;
    [Required]
    public string SubmittedByName { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public bool SupervisorPreApproved { get; set; } = false;
    public List<LiquidationLineItemDto> LineItems { get; set; } = new();
}

public class LiquidationLineItemDto
{
    public int LineItemId { get; set; }
    public string Category { get; set; } = "Fuel";
    public string? Description { get; set; }
    [Range(0.01, 100000000)]
    public decimal Amount { get; set; }
    public bool ReceiptAttached { get; set; } = true;
    public DateTime? ReceiptDate { get; set; }
    public string? ReceiptNumber { get; set; }
    public bool IsManualFuelReceipt { get; set; } = false;
}

public class ValidateLiquidationDto
{
    public int LiquidationId { get; set; }
    public string? Remarks { get; set; }
}

public class RejectLiquidationDto
{
    public int LiquidationId { get; set; }
    [Required]
    public string Reason { get; set; } = string.Empty; // wrong receipt date or amount, manual fuel receipt, late submission, other with remarks
    public string? Remarks { get; set; }
    public string? Status { get; set; } // Optional: "Rejected" (default) or "For Correction"
}

public class LiquidationResponseDto
{
    public int LiquidationId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string CashAdvanceId { get; set; } = string.Empty;
    public string SubmittedByName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AdvanceAmount { get; set; }
    public decimal LeftoverCash { get; set; }
    public decimal ReimbursementAmount { get; set; }
    public string Status { get; set; } = string.Empty; // Pending, Validated, Rejected, For Correction
    public DateTime? DateSubmitted { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsLate { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Remarks { get; set; }
    public bool SupervisorPreApproved { get; set; }
    public List<LiquidationLineItemDto> LineItems { get; set; } = new();
}
