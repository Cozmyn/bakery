using System.ComponentModel.DataAnnotations;

namespace Bakery.Api.Models;

/// <summary>
/// Master data: ingredient list. Operator can only read; Admin can manage.
/// Units: for v1 we support mass units (kg, g). If you need liters etc, add density/UM conversion.
/// </summary>
public class Ingredient : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// External/master identifier. Numeric only. Unique.
    /// Stored as int (no leading zeros).
    /// </summary>
    [Required]
    public int ItemNumber { get; set; }

    [Required]
    [MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(16)]
    public string? DefaultUnit { get; set; } = "kg";
}
