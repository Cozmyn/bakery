using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class Batch : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    public int BatchNumber { get; set; }

    public DateTime? MixedAtUtc { get; set; }

    public DateTime? AddedToLineAtUtc { get; set; }

    public BatchStatus Status { get; set; } = BatchStatus.Planned;

    public BatchDisposition Disposition { get; set; } = BatchDisposition.Used;

    public DateTime? DiscardedAtUtc { get; set; }

    public decimal? DiscardAmountKg { get; set; }

    public string? DiscardReasonCode { get; set; }

    public string? DiscardComment { get; set; }

    // Computed
    public int? ProofingActualMinutes { get; set; }

    public Run? Run { get; set; }
}
