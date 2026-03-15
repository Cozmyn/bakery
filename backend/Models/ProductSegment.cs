using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class ProductSegment : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProductId { get; set; }

    [Range(1, 4)]
    public int SegmentId { get; set; }

    public decimal LengthM { get; set; }
    public decimal TargetSpeedMps { get; set; }

    public Product? Product { get; set; }
}
