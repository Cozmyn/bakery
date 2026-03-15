using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Persisted 20-minute analytics rollup for reporting (P1/P2 per position, VIS pareto over same counted pieces,
/// and P3 time-based "amalgam" randomized distribution over positions).
/// </summary>
public class AnalyticsBucket20 : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public DateTime BucketStartUtc { get; set; }

    [Required]
    public DateTime BucketEndUtc { get; set; }

    public int Positions { get; set; }

    public int P1StartPieceSeq { get; set; }
    public int P1EndPieceSeq { get; set; }

    public int P1PieceCount { get; set; }
    public int P2PieceCount { get; set; }

    public bool IsFinal { get; set; }

    // jsonb payloads (kept flexible; UI expects structured objects)
    [Required]
    public string P1Json { get; set; } = "{}";

    [Required]
    public string P2Json { get; set; } = "{}";

    [Required]
    public string VisParetoJson { get; set; } = "{}";

    [Required]
    public string P3Json { get; set; } = "{}";
}
