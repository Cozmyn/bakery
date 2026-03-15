using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// One-tap downtime/belt-empty reasons used by Operator prompts.
/// </summary>
public class DowntimeReason : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Category { get; set; } = "GENERAL";

    public bool IsOneTap { get; set; } = true;

    public int SortOrder { get; set; } = 100;
}
