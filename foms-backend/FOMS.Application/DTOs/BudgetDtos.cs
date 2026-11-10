using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FOMS.Application.DTOs;

public class DepartmentDto
{
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsOverhead { get; set; }
    public bool IsLegacy { get; set; }
}

public class MonthlyBudgetLineDto
{
    public int BudgetLineId { get; set; }
    public int AnnualBudgetId { get; set; }
    public int FiscalYear { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Month { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal HistoricalActual { get; set; }
    public decimal Available { get; set; }
    public decimal Remaining { get; set; }
    public decimal UtilizationPercent { get; set; }
    public int OverrideCount { get; set; }
    public string AlertStatus { get; set; } = "Normal"; // Normal, Warning, Exhausted
}

public class AnnualBudgetDto
{
    public int AnnualBudgetId { get; set; }
    public int FiscalYear { get; set; }
    public decimal TotalBudget { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, Submitted, Approved, Rejected
    public string PreparedBy { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public decimal WarningAlertThresholdPercent { get; set; } = 80.00m;
    public decimal TotalCommitted { get; set; }
    public decimal TotalActual { get; set; }
    public decimal TotalAvailable { get; set; }
    public decimal TotalRemaining { get; set; }
    public List<MonthlyBudgetLineDto> Lines { get; set; } = new();
}

public class CreateOrUpdateDraftBudgetRequest
{
    [Required]
    public int FiscalYear { get; set; }
    public decimal WarningAlertThresholdPercent { get; set; } = 80.00m;
    public List<BudgetLineEstimateDto> Lines { get; set; } = new();
}

public class BudgetLineEstimateDto
{
    [Required]
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    [Required]
    public string Category { get; set; } = string.Empty;
    [Range(1, 12)]
    public int Month { get; set; }
    [Range(0, 1000000000)]
    public decimal BudgetAmount { get; set; }
    public decimal HistoricalActual { get; set; }
}

public class RejectBudgetRequest
{
    [Required(ErrorMessage = "Rejection reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

public class AllocateBudgetRequest
{
    [Required]
    public int AnnualBudgetId { get; set; }
    public List<AllocateLineDto> Allocations { get; set; } = new();
}

public class AllocateLineDto
{
    public int BudgetLineId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Month { get; set; }
    [Range(0, 1000000000)]
    public decimal AllocatedAmount { get; set; }
}

public class CheckBudgetAvailabilityRequest
{
    public int FiscalYear { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Month { get; set; }
    public decimal RequestedAmount { get; set; }
}

public class BudgetAvailabilityResponse
{
    public bool IsAvailable { get; set; }
    public decimal AvailableAmount { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal OverBudgetAmount { get; set; }
    public bool IsOverBudget { get; set; }
    public bool RequiresCeoApproval { get; set; }
    public decimal ThresholdLimit { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RequestBudgetOverrideRequest
{
    [Required]
    public int FiscalYear { get; set; }
    [Required]
    public string DepartmentCode { get; set; } = string.Empty;
    [Required]
    public string Category { get; set; } = string.Empty;
    [Range(1, 12)]
    public int Month { get; set; }
    [Range(0.01, 1000000000)]
    public decimal RequestedAmount { get; set; }
    [Required(ErrorMessage = "Override reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

public class RejectOverrideRequest
{
    [Required(ErrorMessage = "Rejection reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

public class RequestBudgetTransferRequest
{
    [Required]
    public int FiscalYear { get; set; }
    public string TransferType { get; set; } = "Transfer"; // Transfer or CarryOver
    [Required]
    public string FromDepartmentCode { get; set; } = string.Empty;
    [Required]
    public string FromCategory { get; set; } = string.Empty;
    [Range(1, 12)]
    public int FromMonth { get; set; }

    [Required]
    public string ToDepartmentCode { get; set; } = string.Empty;
    [Required]
    public string ToCategory { get; set; } = string.Empty;
    [Range(1, 12)]
    public int ToMonth { get; set; }

    [Range(0.01, 1000000000, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Transfer reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

public class RejectTransferRequest
{
    [Required(ErrorMessage = "Rejection reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

public class WeeklyBudgetReportDto
{
    public int FiscalYear { get; set; }
    public int Month { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public decimal TotalMonthlyBudget { get; set; }
    public decimal TotalActualSpend { get; set; }
    public decimal TotalCommitted { get; set; }
    public decimal TotalAvailable { get; set; }
    public decimal TotalRemaining { get; set; }
    public decimal OverallUtilizationPercent { get; set; }
    public List<DepartmentBudgetSummaryDto> Departments { get; set; } = new();
}

public class DepartmentBudgetSummaryDto
{
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public decimal MonthlyBudget { get; set; }
    public decimal ActualSpend { get; set; }
    public decimal Committed { get; set; }
    public decimal Available { get; set; }
    public decimal Remaining { get; set; }
    public decimal UtilizationPercent { get; set; }
    public string AlertStatus { get; set; } = "Normal";
    public List<MonthlyBudgetLineDto> CategoryLines { get; set; } = new();
}
