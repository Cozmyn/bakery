using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Mandatory prompts shown to Operator (downtime reason, changeover question, belt empty, end production).
/// </summary>
public class OperatorPrompt : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RunId { get; set; }

    /// <summary>
    /// String type to keep UI flexible (e.g. DOWNTIME_REQUIRED, CHANGEOVER_QUESTION, BELT_EMPTY_REQUIRED).
    /// </summary>
    [Required]
    [MaxLength(60)]
    public string Type { get; set; } = string.Empty;

    [Required]
    public PromptStatus Status { get; set; } = PromptStatus.Open;

    public DateTime TriggeredAtUtc { get; set; } = DateTime.UtcNow;

    public int ThresholdSec { get; set; } = 60;

    /// <summary>
    /// Small JSON payload for UI context (timestamps, flags). Not large data.
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    // Resolution
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? Comment { get; set; }

    public Run? Run { get; set; }
}
