using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("ingredients")]
[Authorize(Policy = "OperatorOrAdmin")]
public class IngredientsController : ControllerBase
{
    private readonly AppDbContext _db;
    public IngredientsController(AppDbContext db) { _db = db; }
    private string Actor() => User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "unknown";

    public record CreateIngredient(int ItemNumber, string Name, string? DefaultUnit);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.Ingredients.AsNoTracking().OrderBy(x => x.ItemNumber)
            // keep Code in response for backward compatibility (UI/Reports), but enforce numeric-only.
            .Select(x => new { x.Id, x.ItemNumber, x.Code, x.Name, x.DefaultUnit })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateIngredient req)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (req.ItemNumber <= 0 || name.Length < 2) return BadRequest(new { error = "Invalid itemNumber/name" });

        if (await _db.Ingredients.AnyAsync(x => x.ItemNumber == req.ItemNumber))
            return Conflict(new { error = "Ingredient itemNumber exists" });

        // Keep Code for compatibility; enforce numeric-only by deriving it from ItemNumber.
        var code = req.ItemNumber.ToString();

        var ing = new Ingredient
        {
            ItemNumber = req.ItemNumber,
            Code = code,
            Name = name,
            DefaultUnit = string.IsNullOrWhiteSpace(req.DefaultUnit) ? "kg" : req.DefaultUnit!,
            CreatedBy = Actor(),
            Source = "ui"
        };
        _db.Ingredients.Add(ing);
        await _db.SaveChangesAsync();
        return Ok(new { id = ing.Id });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ing = await _db.Ingredients.FirstOrDefaultAsync(x => x.Id == id);
        if (ing is null) return NotFound();

        var usedInRecipe = await _db.RecipeIngredients.AsNoTracking().AnyAsync(x => x.IngredientId == id);
        var usedInBatch = await _db.BatchRecipeIngredients.AsNoTracking().AnyAsync(x => x.IngredientId == id);

        if (usedInRecipe || usedInBatch)
            return Conflict(new { error = "Ingredient is used by a recipe or batch and cannot be deleted" });

        _db.Ingredients.Remove(ing);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

}
