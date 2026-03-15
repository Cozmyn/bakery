using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class EncoderEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Range(1, 4)]
    public int SegmentId { get; set; }

    [Required]
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    public decimal SpeedMps { get; set; }

    public bool IsStopped { get; set; }
}
