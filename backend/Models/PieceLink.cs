using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Piece-to-piece linkage with confidence. Used for P1↔P2 high-confidence matching,
/// and later for best-effort matching across points.
/// </summary>
public class PieceLink : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [MaxLength(8)]
    public string FromPoint { get; set; } = "";

    [MaxLength(8)]
    public string ToPoint { get; set; } = "";

    [Required]
    [MaxLength(120)]
    public string FromPieceUid { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string ToPieceUid { get; set; } = string.Empty;

    public decimal Confidence { get; set; } = 0.5m;

    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}
