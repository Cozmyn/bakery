using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Batch-level mix sheet (operator editable). It starts as a snapshot of the standard recipe
/// and can be modified (quantity changes) or extended (additional ingredients) by the Operator.
/// </summary>
public class BatchRecipeIngredient : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid BatchId { get; set; }

    [Required]
    public Guid IngredientId { get; set; }

    public decimal Quantity { get; set; }

    [MaxLength(16)]
    public string Unit { get; set; } = "kg";

    /// <summary>
    /// True if this line was added by operator (not present in standard).
    /// </summary>
    public bool IsAdded { get; set; } = false;

    /// <summary>
    /// Soft-delete to keep audit-friendly history.
    /// </summary>
    public bool IsRemoved { get; set; } = false;

    public string? ReasonCode { get; set; }
    public string? Comment { get; set; }

    public Batch? Batch { get; set; }
    public Ingredient? Ingredient { get; set; }
}
