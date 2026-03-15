using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class WeightSample : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public PointCode Point { get; set; }

    [Required]
    public DateTime SampledAtUtc { get; set; } = DateTime.UtcNow;

    [Range(1, 1000)]
    public int PiecesCount { get; set; }

    // JSON string: [123.4, 125.1, ...]
    public string WeightsGJson { get; set; } = "[]";

    public decimal? ComputedKFactor { get; set; }

    public Run? Run { get; set; }
}
