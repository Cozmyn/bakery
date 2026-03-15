using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Master list of camera-visible defect classifications (no images stored).
/// Code is persisted on events; label/category drive UI & reports.
/// </summary>
public class DefectTypeDef : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Label { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string Category { get; set; } = "OTHER";

    public int SortOrder { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 1=low, 2=medium, 3=high. Used for alerting/triage.
    /// </summary>
    public int SeverityDefault { get; set; } = 2;
}
