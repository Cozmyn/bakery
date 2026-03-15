using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class Product : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? PublishedAtUtc { get; set; }

    // Economics
    public decimal? CostPerUnit { get; set; }
    public decimal? ValuePerUnit { get; set; }
    public decimal? CostPerHour { get; set; }

    // Performance
    public int? IdealCycleTimeSec { get; set; }
    public decimal? TargetSpeedDefaultMps { get; set; }

    // Proofing
    public int? ProofingMinMinutes { get; set; }
    public int? ProofingMaxMinutes { get; set; }

    // For waste conversions (D10 locked: P3 nominal)
    public decimal? NominalUnitWeightG_P3 { get; set; }

    // Travel-time heuristic for analytics bucket mapping (VIS -> P3), per product.
    // Used only for Analytics Subpage 2 bucket rollups.
    public int? VisToP3Minutes { get; set; }

    public List<ProductTolerance> Tolerances { get; set; } = new();
    public List<ProductSegment> Segments { get; set; } = new();
    public ProductDensityDefaults? DensityDefaults { get; set; }

    public List<ProductRecipe> Recipes { get; set; } = new();
}
