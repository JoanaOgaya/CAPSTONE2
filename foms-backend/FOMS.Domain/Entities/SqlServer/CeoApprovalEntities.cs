using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FOMS.Domain.Entities.SqlServer;

[Table("CeoApprovalThresholdSettings")]
public class CeoApprovalThresholdSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("SettingId")]
    public int SettingId { get; set; }

    [Column("SettingKey")]
    [MaxLength(100)]
    public string SettingKey { get; set; } = "CSH-09_CEO_THRESHOLD";

    [Column("ThresholdAmount", TypeName = "decimal(18,2)")]
    public decimal? ThresholdAmount { get; set; } // NULL = unset (no default seed)

    [Column("Currency")]
    [MaxLength(10)]
    public string Currency { get; set; } = "PHP";

    [Column("EffectiveFrom")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    [Column("EffectiveTo")]
    public DateTime? EffectiveTo { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Column("UpdatedBy")]
    [MaxLength(450)]
    public string UpdatedBy { get; set; } = string.Empty;

    [Column("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("CeoApprovalThresholdChangeLogs")]
public class CeoApprovalThresholdChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("LogId")]
    public int LogId { get; set; }

    [Column("SettingId")]
    public int SettingId { get; set; }

    [Column("OldAmount", TypeName = "decimal(18,2)")]
    public decimal? OldAmount { get; set; }

    [Column("NewAmount", TypeName = "decimal(18,2)")]
    public decimal? NewAmount { get; set; }

    [Column("ChangedBy")]
    [MaxLength(450)]
    public string ChangedBy { get; set; } = string.Empty;

    [Column("ChangedAt")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [Column("Reason")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

[Table("CeoApprovalRecords")]
public class CeoApprovalRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("RecordId")]
    public int RecordId { get; set; }

    [Column("ItemType")]
    [MaxLength(50)]
    public string ItemType { get; set; } = string.Empty;

    [Column("ItemId")]
    [MaxLength(100)]
    public string ItemId { get; set; } = string.Empty;

    [Column("Status")]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty; // Approved or Rejected only

    [Column("ApproverLabel")]
    [MaxLength(50)]
    public string ApproverLabel { get; set; } = "CEO/President (in person)";

    [Column("EnteredBy")]
    [MaxLength(450)]
    public string EnteredBy { get; set; } = string.Empty; // FM employee ID

    [Column("EnteredAt")]
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;

    [Column("ApprovalDate", TypeName = "date")]
    public DateTime ApprovalDate { get; set; }

    [Column("Notes")]
    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Column("RejectionReason")]
    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    [Column("AttachmentPath")]
    [MaxLength(500)]
    public string? AttachmentPath { get; set; }

    [Column("AttachmentFileName")]
    [MaxLength(255)]
    public string? AttachmentFileName { get; set; }

    [Column("HasAttachment")]
    public bool HasAttachment { get; set; } = false;

    [Column("IsCorrection")]
    public bool IsCorrection { get; set; } = false;

    [Column("OriginalRecordId")]
    public int? OriginalRecordId { get; set; }

    [Column("CorrectionReason")]
    [MaxLength(1000)]
    public string? CorrectionReason { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;
}

[Table("CeoApprovalConfirmations")]
public class CeoApprovalConfirmation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("ConfirmationId")]
    public int ConfirmationId { get; set; }

    [Column("RecordId")]
    public int RecordId { get; set; }

    [Column("Kind")]
    [MaxLength(20)]
    public string Kind { get; set; } = "Recording"; // "Recording" or "Correction"

    [Column("ConfirmedBy")]
    [MaxLength(450)]
    public string ConfirmedBy { get; set; } = string.Empty;

    [Column("ConfirmedAt")]
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}
