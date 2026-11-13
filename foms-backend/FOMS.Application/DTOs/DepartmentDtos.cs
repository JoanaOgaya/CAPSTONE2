using System;
using System.ComponentModel.DataAnnotations;

namespace FOMS.Application.DTOs;

public class DepartmentSuggestionDto
{
    public string? DepartmentCode { get; set; }
    public string? DepartmentName { get; set; }
    public bool HasSuggestion { get; set; }
}

public class ChangeDepartmentRequest
{
    [Required]
    public string RequestType { get; set; } = string.Empty; // CashAdvance, DirectDisbursement, Reimbursement

    [Required]
    public string RequestId { get; set; } = string.Empty;

    [Required]
    public string NewDepartmentCode { get; set; } = string.Empty;

    [Required]
    [MinLength(3, ErrorMessage = "A detailed reason for the department change is strictly required.")]
    public string Reason { get; set; } = string.Empty;
}

public class DepartmentChangeLogDto
{
    public int LogId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string OldDepartmentCode { get; set; } = string.Empty;
    public string OldDepartmentName { get; set; } = string.Empty;
    public string NewDepartmentCode { get; set; } = string.Empty;
    public string NewDepartmentName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
