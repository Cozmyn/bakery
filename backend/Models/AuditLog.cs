using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Immutable audit record for compliance + troubleshooting.
/// Stored in UTC. UI is responsible for local timezone display.
/// </summary>
public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime TsUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(16)]
    public string Method { get; set; } = "GET";

    [MaxLength(300)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(600)]
    public string Action { get; set; } = string.Empty; // e.g., "POST /products/{id}/publish"

    [MaxLength(200)]
    public string? UserEmail { get; set; }

    [MaxLength(30)]
    public string? UserRole { get; set; }

    [MaxLength(80)]
    public string? EntityType { get; set; }

    [MaxLength(80)]
    public string? EntityId { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public int StatusCode { get; set; }

    /// <summary>Small JSON payload with query/body preview and metadata. Keep small (<= ~20KB).</summary>
    public string? DetailJson { get; set; }
}
