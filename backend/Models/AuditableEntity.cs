using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public abstract class AuditableEntity
{
    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public string Source { get; set; } = "system";

    /// <summary>
    /// A monotonic stamp to help trace ingestion batches (e.g. UUID or increment).
    /// </summary>
    public string DataStamp { get; set; } = Guid.NewGuid().ToString("N");
}
