using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FOMS.Domain.Entities.SqlServer;

[Table("DepartmentChangeLogs")]
public class DepartmentChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("LogId")]
    public int LogId { get; set; }

    [Column("RequestType")]
    [MaxLength(50)]
    public string RequestType { get; set; } = string.Empty;

    [Column("RequestId")]
    [MaxLength(50)]
    public string RequestId { get; set; } = string.Empty;

    [Column("OldDepartmentCode")]
    [MaxLength(20)]
    public string OldDepartmentCode { get; set; } = string.Empty;

    [Column("NewDepartmentCode")]
    [MaxLength(20)]
    public string NewDepartmentCode { get; set; } = string.Empty;

    [Column("Reason")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Column("ChangedBy")]
    [MaxLength(50)]
    public string ChangedBy { get; set; } = string.Empty;

    [Column("ChangedAt")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
