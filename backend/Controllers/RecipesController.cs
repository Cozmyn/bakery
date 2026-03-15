using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("products/{productId:guid}/recipe")]
[Authorize(Policy = "OperatorOrAdmin")]
public class RecipesController : ControllerBase
{
    private readonly AppDbContext _db;
    public RecipesController(AppDbContext db) { _db = db; }
    private string Actor() => User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "unknown";

    public record IngredientLine(Guid IngredientId, decimal Quantity, string? Unit);
    public record UpsertRecipeRequest(int? Version, List<IngredientLine> Ingredients);

    [HttpGet]
    public async Task<IActionResult> GetCurrent(Guid productId)
    {
        var recipe = await _db.ProductRecipes
            .AsNoTracking()
            .Include(x => x.Ingredients)
            .ThenInclude(x => x.Ingredient)
            .Where(x => x.ProductId == productId && x.IsCurrent)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync();

        if (recipe is null) return Ok(new { exists = false });

        return Ok(new
        {
            exists = true,
            recipe = new
            {
                recipe.Id,
                recipe.Version,
                recipe.IsCurrent,
                ingredients = recipe.Ingredients.OrderBy(i => i.Ingredient!.ItemNumber).Select(i => new
                {
                    i.Id,
                    ingredient = new { i.IngredientId, i.Ingredient!.ItemNumber, i.Ingredient!.Code, i.Ingredient.Name },
                    i.Quantity,
                    i.Unit
                })
            }
        });
    }

    [HttpPut]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Upsert(Guid productId, [FromBody] UpsertRecipeRequest req)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p is null) return NotFound();
        if (req.Ingredients is null || req.Ingredients.Count == 0)
            return BadRequest(new { error = "Recipe must have at least 1 ingredient" });

        var current = await _db.ProductRecipes
            .Include(x => x.Ingredients)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.IsCurrent);

        var nextVersion = req.Version ?? ((current?.Version ?? 0) + 1);

        if (current is null)
        {
            current = new ProductRecipe
            {
                ProductId = productId,
                Version = nextVersion,
                IsCurrent = true,
                CreatedBy = Actor(),
                Source = "ui"
            };
            _db.ProductRecipes.Add(current);
        }
        else
        {
            // create new version to keep audit clean
            current.IsCurrent = false;
            current.UpdatedAtUtc = DateTime.UtcNow;
            current.UpdatedBy = Actor();
            current.Source = "ui";
            current.DataStamp = Guid.NewGuid().ToString("N");

            var newRecipe = new ProductRecipe
            {
                ProductId = productId,
                Version = nextVersion,
                IsCurrent = true,
                CreatedBy = Actor(),
                Source = "ui"
            };
            _db.ProductRecipes.Add(newRecipe);
            current = newRecipe;
        }

        foreach (var line in req.Ingredients)
        {
            var ing = await _db.Ingredients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.IngredientId);
            if (ing is null) return BadRequest(new { error = $"Ingredient not found: {line.IngredientId}" });
            if (line.Quantity <= 0) return BadRequest(new { error = "Quantity must be > 0" });

            _db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = current.Id,
                IngredientId = line.IngredientId,
                Quantity = line.Quantity,
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? (ing.DefaultUnit ?? "kg") : line.Unit!,
                CreatedBy = Actor(),
                Source = "ui"
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true, version = current.Version });
    }
}
