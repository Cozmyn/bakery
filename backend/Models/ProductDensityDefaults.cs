using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class ProductDensityDefaults : AuditableEntity
{
    [Key]
    public Guid ProductId { get; set; }

    public decimal DensityP1_GPerCm3 { get; set; }
    public decimal DensityP2_GPerCm3 { get; set; }
    public decimal DensityP3_GPerCm3 { get; set; }

    public Product? Product { get; set; }
}
