using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FOMS.Domain.Entities.SqlServer;

[Table("Departments")]
public class Department
{
    [Key]
    [Column("department_id")]
    public int DepartmentId { get; set; }

    [Column("department_code")]
    [MaxLength(50)]
    public string DepartmentCode { get; set; } = string.Empty;

    [Column("department_name")]
    [MaxLength(100)]
    public string DepartmentName { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("UserClientMappings")]
public class UserClientMapping
{
    [Key]
    [Column("mapping_id")]
    public int MappingId { get; set; }

    [Column("user_id")]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    [Column("client_id")]
    [MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("RevokedTokens")]
public class RevokedToken
{
    [Key]
    [Column("token_id")]
    public int TokenId { get; set; }

    [Column("token_hash")]
    [MaxLength(256)]
    public string TokenHash { get; set; } = string.Empty;

    [Column("revoked_at")]
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}

[Table("ExecutiveApprovalRecords")]
public class ExecutiveApprovalRecord
{
    [Key]
    [Column("approval_id")]
    public int ApprovalId { get; set; }

    [Column("approver_title")]
    [MaxLength(50)]
    public string ApproverTitle { get; set; } = "CEO"; // 'CEO' or 'President'

    [Column("approver_name")]
    [MaxLength(150)]
    public string ApproverName { get; set; } = string.Empty;

    [Column("approval_date")]
    public DateOnly ApprovalDate { get; set; }

    [Column("decision")]
    [MaxLength(50)]
    public string Decision { get; set; } = "Approved"; // 'Approved' or 'Rejected'

    [Column("approved_amount")]
    public decimal ApprovedAmount { get; set; }

    [Column("transaction_type")]
    [MaxLength(100)]
    public string TransactionType { get; set; } = string.Empty; // 'Discount', 'LargeCashOutflow', 'BudgetOverride', 'BudgetTransfer', 'BudgetCarryOver'

    [Column("transaction_reference")]
    [MaxLength(100)]
    public string TransactionReference { get; set; } = string.Empty;

    [Column("proof_attachment_url")]
    [MaxLength(500)]
    public string ProofAttachmentUrl { get; set; } = string.Empty;

    [Column("recorded_by")]
    [MaxLength(100)]
    public string RecordedBy { get; set; } = string.Empty;

    [Column("is_delegated")]
    public bool IsDelegated { get; set; } = false;

    [Column("delegate_justification")]
    [MaxLength(500)]
    public string? DelegateJustification { get; set; }

    [Column("supersedes_approval_id")]
    public int? SupersedesApprovalId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("FinanceManagerDelegates")]
public class FinanceManagerDelegate
{
    [Key]
    [Column("delegate_id")]
    public int DelegateId { get; set; }

    [Column("delegate_user_id")]
    [MaxLength(100)]
    public string DelegateUserId { get; set; } = string.Empty;

    [Column("delegate_name")]
    [MaxLength(150)]
    public string DelegateName { get; set; } = string.Empty;

    [Column("assigned_by")]
    [MaxLength(100)]
    public string AssignedBy { get; set; } = string.Empty;

    [Column("valid_from")]
    public DateTime ValidFrom { get; set; }

    [Column("valid_until")]
    public DateTime ValidUntil { get; set; }

    [Column("reason")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

