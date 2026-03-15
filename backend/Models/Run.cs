using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class Run : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public DateTime StartUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the operator confirms the production ended (no more new P1 pieces) or changeover occurs.
    /// KPI counting continues while WIP drains downstream.
    /// </summary>
    public DateTime? ProductionEndUtc { get; set; }

    /// <summary>
    /// Final close time (after last piece passed, i.e. WIP drained). Used for reports.
    /// </summary>
    public DateTime? EndUtc { get; set; }

    [Required]
    public RunStatus Status { get; set; } = RunStatus.Running;

    // Grey zone / WIP
    public int WipWindowSec { get; set; } = 900;

    public DateTime? GreyZoneStartUtc { get; set; }
    public DateTime? GreyZoneEndUtc { get; set; }

    public Product? Product { get; set; }

    public List<Batch> Batches { get; set; } = new();
    public RunOverride? Override { get; set; }
}
