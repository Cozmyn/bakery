using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public enum AlertSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum AlertStatus
{
    Active = 1,
    Snoozed = 2,
    Acknowledged = 3,
    Closed = 4
}

/// <summary>
/// Persistent alert for operator/admin.
/// - ACK and Snooze are persisted.
/// - Alerts may be auto-closed when condition clears.
/// </summary>
public class Alert : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? RunId { get; set; }

    [Required]
    [MaxLength(60)]
    public string Type { get; set; } = string.Empty; // e.g. DEFECT_SPIKE, DOWNTIME

    [Required]
    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(600)]
    public string Message { get; set; } = string.Empty;

    [Required]
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    [Required]
    public AlertStatus Status { get; set; } = AlertStatus.Active;

    public DateTime TriggeredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SnoozedUntilUtc { get; set; }

    public string? AcknowledgedByEmail { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>
    /// Used to deduplicate alerts (e.g. runId|type|bucketStartUtc).
    /// </summary>
    [MaxLength(160)]
    public string? DedupeKey { get; set; }

    public string? MetadataJson { get; set; }
}
