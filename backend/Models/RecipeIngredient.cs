using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

public class RecipeIngredient : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RecipeId { get; set; }

    [Required]
    public Guid IngredientId { get; set; }

    /// <summary>
    /// Quantity in the unit given (typically kg or g).
    /// </summary>
    public decimal Quantity { get; set; }

    [MaxLength(16)]
    public string Unit { get; set; } = "kg";

    public ProductRecipe? Recipe { get; set; }
    public Ingredient? Ingredient { get; set; }
}
