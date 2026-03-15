using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class ProductTolerance : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public PointCode Point { get; set; }

    // Dimensions in mm
    public decimal? WidthMinMm { get; set; }
    public decimal? WidthMaxMm { get; set; }
    public decimal? LengthMinMm { get; set; }
    public decimal? LengthMaxMm { get; set; }
    public decimal? HeightMinMm { get; set; }
    public decimal? HeightMaxMm { get; set; }

    // Volume in mm^3
    public decimal? VolumeMinMm3 { get; set; }
    public decimal? VolumeMaxMm3 { get; set; }

    // Weight in g (estimated/calibrated)
    public decimal? WeightMinG { get; set; }
    public decimal? WeightMaxG { get; set; }

    public Product? Product { get; set; }
}
