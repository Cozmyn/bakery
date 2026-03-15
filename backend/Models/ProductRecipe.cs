using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Versioned recipe per product. Only one is current.
/// Standard quantities are set by Admin; Operator can create batch-level overrides.
/// </summary>
public class ProductRecipe : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProductId { get; set; }

    public int Version { get; set; } = 1;

    public bool IsCurrent { get; set; } = true;

    public Product? Product { get; set; }

    public List<RecipeIngredient> Ingredients { get; set; } = new();
}
