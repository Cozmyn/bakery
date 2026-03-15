using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class BatchWasteEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public Guid BatchId { get; set; }

    [Required]
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(50)]
    public string WasteType { get; set; } = "MIX_SCRAP";

    public decimal AmountKg { get; set; }

    public decimal EquivalentUnits { get; set; }

    public decimal ValueLoss { get; set; }

    public string? ReasonCode { get; set; }

    public string? Comment { get; set; }
}
