using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class RunOverride : AuditableEntity
{
    [Key]
    public Guid RunId { get; set; }

    public decimal? DensityP1_GPerCm3 { get; set; }
    public decimal? DensityP2_GPerCm3 { get; set; }
    public decimal? DensityP3_GPerCm3 { get; set; }

    public string? ReasonCode { get; set; }
    public string? Comment { get; set; }

    public Run? Run { get; set; }
}
