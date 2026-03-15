using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

/// <summary>
/// Operator-facing mix sheet per batch:
/// - Shows standard recipe (admin-defined)
/// - Allows operator to adjust quantities or add ingredients (audit)
/// - Computes total mixer weight (kg) and compares with standard
/// </summary>
[ApiController]
[Route("batches/{batchId:guid}/mix-sheet")]
[Authorize(Policy = "OperatorOrAdmin")]
public class BatchMixSheetController : ControllerBase
{
    private readonly AppDbContext _db;
    public BatchMixSheetController(AppDbContext db) { _db = db; }
    private string Actor() => User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "unknown";

    public record MixLine(Guid IngredientId, decimal Quantity, string? Unit, bool? IsRemoved, string? ReasonCode, string? Comment);

    public record UpsertMixSheetRequest(List<MixLine> Lines, string? ReasonCode, string? Comment);

    [HttpGet]
    public async Task<IActionResult> Get(Guid batchId)
    {
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId);
        if (batch is null) return NotFound();

        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batch.RunId);
        if (run is null) return BadRequest(new { error = "Run not found" });

        var recipe = await _db.ProductRecipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .ThenInclude(ri => ri.Ingredient)
            .Where(r => r.ProductId == run.ProductId && r.IsCurrent)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync();

        if (recipe is null)
            return BadRequest(new { error = "Product has no current recipe" });

        // Ensure batch snapshot exists (first time only)
        var existing = await _db.BatchRecipeIngredients
            .Include(x => x.Ingredient)
            .Where(x => x.BatchId == batchId)
            .ToListAsync();

        if (existing.Count == 0)
        {
            foreach (var ri in recipe.Ingredients)
            {
                _db.BatchRecipeIngredients.Add(new BatchRecipeIngredient
                {
                    BatchId = batchId,
                    IngredientId = ri.IngredientId,
                    Quantity = ri.Quantity,
                    Unit = ri.Unit,
                    IsAdded = false,
                    IsRemoved = false,
                    CreatedBy = Actor(),
                    Source = "ui"
                });
            }
            await _db.SaveChangesAsync();

            existing = await _db.BatchRecipeIngredients
                .Include(x => x.Ingredient)
                .Where(x => x.BatchId == batchId)
                .ToListAsync();
        }

        // Standard map
        var stdMap = recipe.Ingredients.ToDictionary(x => x.IngredientId, x => x);

        var standardTotalKg = recipe.Ingredients.Sum(x => ToKg(x.Quantity, x.Unit));
        var actualTotalKg = existing.Where(x => !x.IsRemoved).Sum(x => ToKg(x.Quantity, x.Unit));

        var lines = existing
            .OrderBy(x => x.Ingredient!.ItemNumber)
            .Select(x => new
            {
                x.Id,
                ingredient = new { x.IngredientId, x.Ingredient!.ItemNumber, x.Ingredient!.Code, x.Ingredient.Name, defaultUnit = x.Ingredient.DefaultUnit },
                standard = stdMap.TryGetValue(x.IngredientId, out var s)
                    ? new { quantity = s.Quantity, unit = s.Unit }
                    : null,
                actual = new { quantity = x.Quantity, unit = x.Unit },
                x.IsAdded,
                x.IsRemoved,
                x.ReasonCode,
                x.Comment
            })
            .ToList();

        return Ok(new
        {
            batchId,
            runId = run.Id,
            recipeVersion = recipe.Version,
            totals = new
            {
                standardKg = Math.Round(standardTotalKg, 3),
                actualKg = Math.Round(actualTotalKg, 3),
                deltaKg = Math.Round(actualTotalKg - standardTotalKg, 3),
                deltaPct = standardTotalKg <= 0 ? 0 : Math.Round((actualTotalKg - standardTotalKg) / standardTotalKg, 4)
            },
            lines
        });
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(Guid batchId, [FromBody] UpsertMixSheetRequest req)
    {
        if (req.Lines is null || req.Lines.Count == 0) return BadRequest(new { error = "Lines required" });

        var batch = await _db.Batches.FirstOrDefaultAsync(x => x.Id == batchId);
        if (batch is null) return NotFound();

        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batch.RunId);
        if (run is null) return BadRequest(new { error = "Run not found" });

        var recipe = await _db.ProductRecipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => r.ProductId == run.ProductId && r.IsCurrent)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync();
        if (recipe is null) return BadRequest(new { error = "Product has no current recipe" });

        // validate units & ingredients
        foreach (var l in req.Lines)
        {
            var ing = await _db.Ingredients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.IngredientId);
            if (ing is null) return BadRequest(new { error = $"Ingredient not found: {l.IngredientId}" });
            if (l.Quantity < 0) return BadRequest(new { error = "Quantity must be >= 0" });
            var unit = string.IsNullOrWhiteSpace(l.Unit) ? (ing.DefaultUnit ?? "kg") : l.Unit!;
            _ = ToKg(l.Quantity, unit); // throws if invalid
        }

        var existing = await _db.BatchRecipeIngredients
            .Where(x => x.BatchId == batchId)
            .ToListAsync();

        var incomingIds = req.Lines.Select(l => l.IngredientId).ToHashSet();

        // Soft-remove lines not included
        foreach (var ex in existing)
        {
            if (!incomingIds.Contains(ex.IngredientId))
            {
                ex.IsRemoved = true;
                ex.UpdatedAtUtc = DateTime.UtcNow;
                ex.UpdatedBy = Actor();
                ex.Source = "ui";
                ex.DataStamp = Guid.NewGuid().ToString("N");
            }
        }

        var stdIds = recipe.Ingredients.Select(x => x.IngredientId).ToHashSet();

        foreach (var l in req.Lines)
        {
            var ing = await _db.Ingredients.AsNoTracking().FirstAsync(x => x.Id == l.IngredientId);
            var unit = string.IsNullOrWhiteSpace(l.Unit) ? (ing.DefaultUnit ?? "kg") : l.Unit!;

            var ex = existing.FirstOrDefault(x => x.IngredientId == l.IngredientId);
            if (ex is null)
            {
                _db.BatchRecipeIngredients.Add(new BatchRecipeIngredient
                {
                    BatchId = batchId,
                    IngredientId = l.IngredientId,
                    Quantity = l.Quantity,
                    Unit = unit,
                    IsAdded = !stdIds.Contains(l.IngredientId),
                    IsRemoved = l.IsRemoved ?? false,
                    ReasonCode = l.ReasonCode ?? req.ReasonCode,
                    Comment = l.Comment ?? req.Comment,
                    CreatedBy = Actor(),
                    Source = "ui"
                });
            }
            else
            {
                ex.Quantity = l.Quantity;
                ex.Unit = unit;
                ex.IsRemoved = l.IsRemoved ?? false;
                ex.IsAdded = !stdIds.Contains(l.IngredientId);
                ex.ReasonCode = l.ReasonCode ?? req.ReasonCode;
                ex.Comment = l.Comment ?? req.Comment;
                ex.UpdatedAtUtc = DateTime.UtcNow;
                ex.UpdatedBy = Actor();
                ex.Source = "ui";
                ex.DataStamp = Guid.NewGuid().ToString("N");
            }
        }

        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = batch.RunId,
            TsUtc = DateTime.UtcNow,
            Type = "BATCH_MIXSHEET_UPDATE",
            ReasonCode = req.ReasonCode,
            Comment = req.Comment,
            CreatedBy = Actor(),
            Source = "ui"
        });

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static decimal ToKg(decimal qty, string unit)
    {
        var u = (unit ?? "kg").Trim().ToLowerInvariant();
        return u switch
        {
            "kg" => qty,
            "g" => qty / 1000m,
            _ => throw new InvalidOperationException($"Unsupported unit for mix sheet: {unit}. Use kg or g.")
        };
    }
}
