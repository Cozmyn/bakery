using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("runs")]
[Authorize(Policy = "OperatorOrAdmin")]
public class RunsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RunsController(AppDbContext db) { _db = db; }

    private string Actor() => User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "unknown";

    public record StartRunRequest(Guid ProductId);

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRunRequest req)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.ProductId);
        if (product is null) return NotFound(new { error = "Product not found" });
        if (product.PublishedAtUtc is null) return BadRequest(new { error = "Product not published for production" });

        var existing = await _db.Runs.FirstOrDefaultAsync(x => x.Status == RunStatus.Running);
        if (existing is not null) return BadRequest(new { error = "A run is already running", runId = existing.Id });

        var run = new Run
        {
            ProductId = req.ProductId,
            StartUtc = DateTime.UtcNow,
            Status = RunStatus.Running,
            CreatedBy = Actor(),
            Source = "ui"
        };
        _db.Runs.Add(run);

        await _db.SaveChangesAsync();

        return Ok(new { runId = run.Id });
    }

    [HttpPost("{runId:guid}/stop")]
    public async Task<IActionResult> Stop(Guid runId)
    {
        var run = await _db.Runs.FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return NotFound();

        // "Stop" in UI means "End production" (do NOT close immediately).
        run.Status = RunStatus.Draining;
        run.ProductionEndUtc ??= DateTime.UtcNow;
        run.UpdatedAtUtc = DateTime.UtcNow;
        run.UpdatedBy = Actor();

        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = run.Id,
            TsUtc = DateTime.UtcNow,
            Type = "PRODUCTION_END_REQUESTED",
            ReasonCode = null,
            Comment = null,
            CreatedBy = Actor(),
            Source = "ui"
        });

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{runId:guid}/end-production")]
    public Task<IActionResult> EndProduction(Guid runId) => Stop(runId);

    public record CreateBatchRequest(DateTime? MixedAtUtc, DateTime? AddedToLineAtUtc);

    [HttpPost("{runId:guid}/batches")]
    public async Task<IActionResult> CreateBatch(Guid runId, [FromBody] CreateBatchRequest req)
    {
        var run = await _db.Runs.FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return NotFound();

        var last = await _db.Batches.Where(x => x.RunId == runId).OrderByDescending(x => x.BatchNumber).FirstOrDefaultAsync();
        var nextNum = (last?.BatchNumber ?? 0) + 1;

        var batch = new Batch
        {
            RunId = runId,
            BatchNumber = nextNum,
            MixedAtUtc = req.MixedAtUtc,
            AddedToLineAtUtc = req.AddedToLineAtUtc,
            Status = req.AddedToLineAtUtc.HasValue ? BatchStatus.OnLine : (req.MixedAtUtc.HasValue ? BatchStatus.Proofing : BatchStatus.Planned),
            CreatedBy = Actor(),
            Source = "ui"
        };

        if (batch.MixedAtUtc.HasValue && batch.AddedToLineAtUtc.HasValue)
        {
            batch.ProofingActualMinutes = (int)Math.Round((batch.AddedToLineAtUtc.Value - batch.MixedAtUtc.Value).TotalMinutes);
        }

        _db.Batches.Add(batch);

        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = runId,
            TsUtc = DateTime.UtcNow,
            Type = "BATCH_CREATE",
            ReasonCode = null,
            Comment = $"Batch #{nextNum}",
            CreatedBy = Actor(),
            Source = "ui"
        });

        await _db.SaveChangesAsync();

        return Ok(new { batchId = batch.Id, batchNumber = batch.BatchNumber });
    }

    public record UpdateBatchTimesRequest(DateTime? MixedAtUtc, DateTime? AddedToLineAtUtc, string? ReasonCode, string? Comment);

    [HttpPut("batches/{batchId:guid}/times")]
    public async Task<IActionResult> UpdateBatchTimes(Guid batchId, [FromBody] UpdateBatchTimesRequest req)
    {
        var batch = await _db.Batches.FirstOrDefaultAsync(x => x.Id == batchId);
        if (batch is null) return NotFound();

        batch.MixedAtUtc = req.MixedAtUtc;
        batch.AddedToLineAtUtc = req.AddedToLineAtUtc;

        if (batch.MixedAtUtc.HasValue && batch.AddedToLineAtUtc.HasValue)
            batch.ProofingActualMinutes = (int)Math.Round((batch.AddedToLineAtUtc.Value - batch.MixedAtUtc.Value).TotalMinutes);

        // status update heuristic
        batch.Status = batch.AddedToLineAtUtc.HasValue ? BatchStatus.OnLine : (batch.MixedAtUtc.HasValue ? BatchStatus.Proofing : BatchStatus.Planned);

        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedBy = Actor();
        batch.Source = "ui";
        batch.DataStamp = Guid.NewGuid().ToString("N");

        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = batch.RunId,
            TsUtc = DateTime.UtcNow,
            Type = "BATCH_TIMES_UPDATE",
            ReasonCode = req.ReasonCode,
            Comment = req.Comment,
            CreatedBy = Actor(),
            Source = "ui"
        });

        await _db.SaveChangesAsync();
        return Ok();
    }

    public record DiscardBatchRequest(decimal AmountKg, bool IsPartial, string ReasonCode, string? Comment);

    [HttpPost("batches/{batchId:guid}/discard")]
    public async Task<IActionResult> DiscardBatch(Guid batchId, [FromBody] DiscardBatchRequest req)
    {
        var batch = await _db.Batches.Include(x => x.Run).FirstOrDefaultAsync(x => x.Id == batchId);
        if (batch is null) return NotFound();
        if (batch.Run is null) return BadRequest(new { error = "Run not found for batch" });

        // Prevent double-discard (idempotency / audit correctness)
        if (batch.Disposition is BatchDisposition.Discarded or BatchDisposition.PartiallyDiscarded || batch.DiscardedAtUtc is not null)
            return Conflict(new { error = "Batch is already discarded" });

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batch.Run.ProductId);
        if (product is null) return BadRequest(new { error = "Product not found" });
        if (product.NominalUnitWeightG_P3 is null || product.NominalUnitWeightG_P3 <= 0)
            return BadRequest(new { error = "Product nominal P3 weight is missing" });

        // Discard always scraps the full batch amount (recipe total). Partial discard is not supported.
        if (req.IsPartial)
            return BadRequest(new { error = "Partial discard is not supported; discard scraps the full recipe amount" });

        var recipe = await _db.ProductRecipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => r.ProductId == batch.Run.ProductId && r.IsCurrent)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync();
        if (recipe is null || recipe.Ingredients.Count == 0)
            return BadRequest(new { error = "Product has no current recipe" });

        decimal ToKg(decimal qty, string? unit)
        {
            var u = (unit ?? "kg").Trim().ToLowerInvariant();
            return u switch
            {
                "kg" => qty,
                "g" => qty / 1000m,
                _ => throw new InvalidOperationException($"Unsupported unit '{unit}' in recipe")
            };
        }

        decimal recipeTotalKg;
        try
        {
            recipeTotalKg = recipe.Ingredients.Sum(i => ToKg(i.Quantity, i.Unit));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        recipeTotalKg = Math.Round(recipeTotalKg, 3);
        if (recipeTotalKg <= 0)
            return BadRequest(new { error = "Recipe total is zero" });

        batch.Disposition = BatchDisposition.Discarded;
        batch.DiscardedAtUtc = DateTime.UtcNow;
        batch.DiscardAmountKg = recipeTotalKg;
        batch.DiscardReasonCode = req.ReasonCode;
        batch.DiscardComment = req.Comment;
        batch.Status = BatchStatus.Closed;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedBy = Actor();
        batch.Source = "ui";
        batch.DataStamp = Guid.NewGuid().ToString("N");

        // D8 locked: kg -> equivalent_units via nominal P3 weight
        var equivalentUnits = (recipeTotalKg * 1000m) / product.NominalUnitWeightG_P3.Value;
        equivalentUnits = Math.Round(equivalentUnits, 2);

        // D9 locked: value_loss = equivalent_units * cost_per_unit (fallback value_per_unit, else 0)
        var unitValue = product.CostPerUnit ?? product.ValuePerUnit ?? 0m;
        var valueLoss = Math.Round(equivalentUnits * unitValue, 2);

        _db.BatchWasteEvents.Add(new BatchWasteEvent
        {
            RunId = batch.RunId,
            BatchId = batch.Id,
            TsUtc = DateTime.UtcNow,
            WasteType = "MIX_SCRAP",
            AmountKg = recipeTotalKg,
            EquivalentUnits = equivalentUnits,
            ValueLoss = valueLoss,
            ReasonCode = req.ReasonCode,
            Comment = req.Comment,
            CreatedBy = Actor(),
            Source = "ui"
        });

        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = batch.RunId,
            TsUtc = DateTime.UtcNow,
            Type = "BATCH_DISCARDED",
            ReasonCode = req.ReasonCode,
            Comment = req.Comment,
            CreatedBy = Actor(),
            Source = "ui"
        });

        await _db.SaveChangesAsync();

        return Ok(new { amountKg = recipeTotalKg, equivalentUnits, valueLoss });
    }

    [HttpGet("{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId)
    {
        var run = await _db.Runs
            .AsNoTracking()
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return NotFound();

        var batches = await _db.Batches
            .AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.BatchNumber)
            .Select(x => new
            {
                x.Id,
                x.BatchNumber,
                x.Status,
                x.Disposition,
                x.MixedAtUtc,
                x.AddedToLineAtUtc,
                x.ProofingActualMinutes,
                x.DiscardAmountKg,
                x.DiscardReasonCode
            })
            .ToListAsync();

        return Ok(new
        {
            run.Id,
            run.Status,
            run.StartUtc,
            run.ProductionEndUtc,
            run.EndUtc,
            product = new { run.ProductId, run.Product!.Code, run.Product.Name },
            batches
        });
    }
}
