using System;
using System.Collections.Generic;

namespace FOMS.Application.DTOs;

public static class CeoApprovalItemTypes
{
    public const string AnnualBudget = "AnnualBudget";
    public const string BudgetTransfer = "BudgetTransfer";
    public const string BudgetCarryOver = "BudgetCarryOver";
    public const string BudgetOverride = "BudgetOverride";
    public const string Reimbursement = "Reimbursement";
    public const string CashOutflow = "CashOutflow";
    public const string InvoiceDiscount = "InvoiceDiscount";
    public const string AnnualFinancialStatement = "AnnualFinancialStatement";

    public static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AnnualBudget,
        BudgetTransfer,
        BudgetCarryOver,
        BudgetOverride,
        Reimbursement,
        CashOutflow,
        InvoiceDiscount,
        AnnualFinancialStatement
    };

    public static readonly HashSet<string> AlwaysRequiredTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AnnualBudget,
        BudgetTransfer,
        BudgetCarryOver,
        InvoiceDiscount,
        AnnualFinancialStatement
    };

    public static readonly HashSet<string> AttachmentAllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AnnualBudget,
        CashOutflow,
        AnnualFinancialStatement
    };

    public static readonly HashSet<string> AttachmentMandatoryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AnnualFinancialStatement
    };

    public static readonly HashSet<string> MakerCheckerConfirmationRequiredTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BudgetTransfer,
        BudgetCarryOver
    };
}

public class CreateCeoApprovalRecordRequest
{
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Approved" or "Rejected"
    public DateTime ApprovalDate { get; set; } = DateTime.UtcNow.Date;
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public byte[]? AttachmentBytes { get; set; }
    public string? AttachmentFileName { get; set; }
}

public class CreateCeoApprovalCorrectionRequest
{
    public int OriginalRecordId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Approved" or "Rejected"
    public DateTime ApprovalDate { get; set; } = DateTime.UtcNow.Date;
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public string CorrectionReason { get; set; } = string.Empty;
    public byte[]? AttachmentBytes { get; set; }
    public string? AttachmentFileName { get; set; }
}

public class ConfirmCeoApprovalRequest
{
    public string Kind { get; set; } = "Recording"; // "Recording" or "Correction"
}

public class UpdateCeoThresholdRequest
{
    public decimal? ThresholdAmount { get; set; } // null = unset / fail-safe
    public DateTime? EffectiveFrom { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CeoApprovalRecordDto
{
    public int RecordId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ApproverLabel { get; set; } = "CEO/President (in person)";
    public string EnteredBy { get; set; } = string.Empty;
    public DateTime EnteredAt { get; set; }
    public DateTime ApprovalDate { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public bool HasAttachment { get; set; }
    public string? AttachmentFileName { get; set; }
    public string AttachmentFlag => HasAttachment
        ? "Attached"
        : (CeoApprovalItemTypes.AttachmentAllowedTypes.Contains(ItemType) ? "No attachment" : "N/A");
    public bool IsCorrection { get; set; }
    public int? OriginalRecordId { get; set; }
    public string? CorrectionReason { get; set; }
    public bool IsActive { get; set; }
    public bool IsEffective { get; set; }
    public List<CeoApprovalConfirmationDto> Confirmations { get; set; } = new();
}

public class CeoApprovalConfirmationDto
{
    public int ConfirmationId { get; set; }
    public int RecordId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTime ConfirmedAt { get; set; }
}

public class CeoApprovalThresholdDto
{
    public int SettingId { get; set; }
    public string SettingKey { get; set; } = "CSH-09_CEO_THRESHOLD";
    public decimal? ThresholdAmount { get; set; }
    public bool IsUnsetFailSafe => !ThresholdAmount.HasValue;
    public decimal EffectiveCeoLimit { get; set; } // Actual threshold or fail-safe to Limit A
    public string Currency { get; set; } = "PHP";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class CeoApprovalThresholdChangeLogDto
{
    public int LogId { get; set; }
    public int SettingId { get; set; }
    public decimal? OldAmount { get; set; }
    public decimal? NewAmount { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CeoApprovalGateCheckResult
{
    public bool IsRequired { get; set; }
    public bool IsSatisfied { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public decimal? EvaluatedAmount { get; set; }
    public decimal? ThresholdInForce { get; set; }
    public bool UsedFailSafeLimit { get; set; }
    public string Message { get; set; } = string.Empty;
    public CeoApprovalRecordDto? EffectiveRecord { get; set; }
}
