using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class OperatorEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    [Required]
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(60)]
    public string Type { get; set; } = "COMMENT";

    [MaxLength(60)]
    public string? ReasonCode { get; set; }

    public string? Comment { get; set; }
}
