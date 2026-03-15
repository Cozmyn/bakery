using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class MeasurementEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public PointCode Point { get; set; }

    [Required]
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    // Cohort/time bucket (primary linkage P2↔VIS, VIS↔P3 fallback)
    [MaxLength(32)]
    public string? CohortId { get; set; }

    // For P1/P2 row alignment
    public int? RowIndex { get; set; }
    public int? PosInRow { get; set; }

    public int PieceSeqIndex { get; set; }

    public decimal WidthMm { get; set; }
    public decimal LengthMm { get; set; }
    public decimal HeightMm { get; set; }

    public decimal VolumeMm3 { get; set; }

    public decimal EstimatedWeightG { get; set; }

    public WeightConfidence WeightConfidence { get; set; } = WeightConfidence.Low;

    [MaxLength(50)]
    public string SourceDeviceId { get; set; } = "sim";

    // piece_uid (internal identity)
    [MaxLength(120)]
    public string PieceUid { get; set; } = string.Empty;
}
