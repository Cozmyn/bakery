using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> List()
    {
        var products = await _db.Products
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.IsActive,
                x.PublishedAtUtc,
                x.NominalUnitWeightG_P3,
                x.ProofingMinMinutes,
                x.ProofingMaxMinutes
            })
            .ToListAsync();

        return Ok(products);
    }

    public record CreateProductRequest(string Code, string Name);

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest req)
    {
        var p = new Product
        {
            Code = req.Code.Trim(),
            Name = req.Name.Trim(),
            CreatedBy = User.Identity?.Name ?? User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value,
            Source = "ui"
        };
        _db.Products.Add(p);
        await _db.SaveChangesAsync();
        return Ok(new { p.Id });
    }

    public record UpdateProductRequest(
        string? Name,
        bool? IsActive,
        decimal? CostPerUnit,
        decimal? ValuePerUnit,
        decimal? CostPerHour,
        int? IdealCycleTimeSec,
        decimal? TargetSpeedDefaultMps,
        int? ProofingMinMinutes,
        int? ProofingMaxMinutes,
        decimal? NominalUnitWeightG_P3
    );

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest req)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        if (req.Name is not null) p.Name = req.Name.Trim();
        if (req.IsActive.HasValue) p.IsActive = req.IsActive.Value;
        p.CostPerUnit = req.CostPerUnit ?? p.CostPerUnit;
        p.ValuePerUnit = req.ValuePerUnit ?? p.ValuePerUnit;
        p.CostPerHour = req.CostPerHour ?? p.CostPerHour;
        p.IdealCycleTimeSec = req.IdealCycleTimeSec ?? p.IdealCycleTimeSec;
        p.TargetSpeedDefaultMps = req.TargetSpeedDefaultMps ?? p.TargetSpeedDefaultMps;
        p.ProofingMinMinutes = req.ProofingMinMinutes ?? p.ProofingMinMinutes;
        p.ProofingMaxMinutes = req.ProofingMaxMinutes ?? p.ProofingMaxMinutes;
        p.NominalUnitWeightG_P3 = req.NominalUnitWeightG_P3 ?? p.NominalUnitWeightG_P3;

        p.UpdatedAtUtc = DateTime.UtcNow;
        p.UpdatedBy = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value;
        p.Source = "ui";
        p.DataStamp = Guid.NewGuid().ToString("N");

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("{id:guid}/readiness")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Readiness(Guid id)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        var missing = new List<string>();

        if (p.ProofingMinMinutes is null || p.ProofingMaxMinutes is null) missing.Add("Proofing min/max");
        if (p.NominalUnitWeightG_P3 is null) missing.Add("Nominal unit weight (P3)");
        if (p.CostPerUnit is null && p.ValuePerUnit is null && p.CostPerHour is null) missing.Add("Economics (cost/value)");
        if (p.IdealCycleTimeSec is null) missing.Add("Ideal cycle time");

        var dens = await _db.ProductDensityDefaults.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == id);
        if (dens is null) missing.Add("Density defaults P1/P2/P3");

        var tolCount = await _db.ProductTolerances.AsNoTracking().CountAsync(x => x.ProductId == id);
        if (tolCount < 3) missing.Add("Tolerances for P1/P2/P3 (incl. weight)");

        var segCount = await _db.ProductSegments.AsNoTracking().CountAsync(x => x.ProductId == id);
        if (segCount < 4) missing.Add("Segments 1..4 length + target speed");

        var recipe = await _db.ProductRecipes.AsNoTracking().Where(x => x.ProductId == id && x.IsCurrent)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync();
        if (recipe is null)
        {
            missing.Add("Recipe (current) with ingredients");
        }
        else
        {
            var ingCount = await _db.RecipeIngredients.AsNoTracking().CountAsync(x => x.RecipeId == recipe.Id);
            if (ingCount == 0) missing.Add("Recipe ingredients");
        }

        return Ok(new { canPublish = missing.Count == 0, missing });
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        // enforce readiness
        var dens = await _db.ProductDensityDefaults.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == id);
        var tolCount = await _db.ProductTolerances.AsNoTracking().CountAsync(x => x.ProductId == id);
        var segCount = await _db.ProductSegments.AsNoTracking().CountAsync(x => x.ProductId == id);
        var recipe = await _db.ProductRecipes.AsNoTracking().Where(x => x.ProductId == id && x.IsCurrent)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync();
        var ingCount = recipe is null ? 0 : await _db.RecipeIngredients.AsNoTracking().CountAsync(x => x.RecipeId == recipe.Id);

        if (p.ProofingMinMinutes is null || p.ProofingMaxMinutes is null || p.NominalUnitWeightG_P3 is null || dens is null || tolCount < 3 || segCount < 4 || recipe is null || ingCount == 0)
            return BadRequest(new { error = "Product not ready for production. Check readiness." });

        p.PublishedAtUtc = DateTime.UtcNow;
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.UpdatedBy = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value;
        await _db.SaveChangesAsync();

        return Ok();
    }

    public record CopyFromRequest(Guid SourceProductId, string NewCode, string NewName);

    [HttpPost("copy")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Copy([FromBody] CopyFromRequest req)
    {
        var src = await _db.Products
            .Include(x => x.Tolerances)
            .Include(x => x.Segments)
            .Include(x => x.DensityDefaults)
            .Include(x => x.Recipes)
            .FirstOrDefaultAsync(x => x.Id == req.SourceProductId);

        if (src is null) return NotFound();

        var p = new Product
        {
            Code = req.NewCode.Trim(),
            Name = req.NewName.Trim(),
            CostPerUnit = src.CostPerUnit,
            ValuePerUnit = src.ValuePerUnit,
            CostPerHour = src.CostPerHour,
            IdealCycleTimeSec = src.IdealCycleTimeSec,
            TargetSpeedDefaultMps = src.TargetSpeedDefaultMps,
            ProofingMinMinutes = src.ProofingMinMinutes,
            ProofingMaxMinutes = src.ProofingMaxMinutes,
            NominalUnitWeightG_P3 = src.NominalUnitWeightG_P3,
            CreatedBy = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value,
            Source = "ui"
        };

        _db.Products.Add(p);

        foreach (var t in src.Tolerances)
        {
            _db.ProductTolerances.Add(new ProductTolerance
            {
                ProductId = p.Id,
                Point = t.Point,
                WidthMinMm = t.WidthMinMm,
                WidthMaxMm = t.WidthMaxMm,
                LengthMinMm = t.LengthMinMm,
                LengthMaxMm = t.LengthMaxMm,
                HeightMinMm = t.HeightMinMm,
                HeightMaxMm = t.HeightMaxMm,
                VolumeMinMm3 = t.VolumeMinMm3,
                VolumeMaxMm3 = t.VolumeMaxMm3,
                WeightMinG = t.WeightMinG,
                WeightMaxG = t.WeightMaxG,
                CreatedBy = p.CreatedBy,
                Source = "ui"
            });
        }

        foreach (var s in src.Segments)
        {
            _db.ProductSegments.Add(new ProductSegment
            {
                ProductId = p.Id,
                SegmentId = s.SegmentId,
                LengthM = s.LengthM,
                TargetSpeedMps = s.TargetSpeedMps,
                CreatedBy = p.CreatedBy,
                Source = "ui"
            });
        }

        if (src.DensityDefaults is not null)
        {
            _db.ProductDensityDefaults.Add(new ProductDensityDefaults
            {
                ProductId = p.Id,
                DensityP1_GPerCm3 = src.DensityDefaults.DensityP1_GPerCm3,
                DensityP2_GPerCm3 = src.DensityDefaults.DensityP2_GPerCm3,
                DensityP3_GPerCm3 = src.DensityDefaults.DensityP3_GPerCm3,
                CreatedBy = p.CreatedBy,
                Source = "ui"
            });
        }

        // Copy current recipe (if exists)
        var srcRecipe = await _db.ProductRecipes
            .Include(x => x.Ingredients)
            .FirstOrDefaultAsync(x => x.ProductId == src.Id && x.IsCurrent);
        if (srcRecipe is not null)
        {
            var newRecipe = new ProductRecipe
            {
                ProductId = p.Id,
                Version = 1,
                IsCurrent = true,
                CreatedBy = p.CreatedBy,
                Source = "ui"
            };
            _db.ProductRecipes.Add(newRecipe);

            foreach (var ri in srcRecipe.Ingredients)
            {
                _db.RecipeIngredients.Add(new RecipeIngredient
                {
                    RecipeId = newRecipe.Id,
                    IngredientId = ri.IngredientId,
                    Quantity = ri.Quantity,
                    Unit = ri.Unit,
                    CreatedBy = p.CreatedBy,
                    Source = "ui"
                });
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { p.Id });
    }
}
