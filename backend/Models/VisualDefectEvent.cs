using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class VisualDefectEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsDefect { get; set; } = true;

    [Required]
    [MaxLength(80)]
    public string DefectType { get; set; } = "UNKNOWN";

    public decimal Confidence { get; set; } = 0.9m;

    // TTL-only reference to an image stored in Redis/memory
    public string? ImageTokenId { get; set; }

    public string? CohortHintId { get; set; }

    // Stable, monotonically-increasing per-run piece counter shared with P1/P2 (strict alignment by count)
    public int PieceSeqIndex { get; set; }
}
