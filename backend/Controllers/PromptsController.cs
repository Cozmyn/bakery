using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("prompts")]
[Authorize(Policy = "OperatorOrAdmin")]
public class PromptsController : ControllerBase
{
    private readonly AppDbContext _db;
    public PromptsController(AppDbContext db) { _db = db; }

    private string Actor() => User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "unknown";

    [HttpGet("open")]
    public async Task<IActionResult> Open()
    {
        var run = await _db.Runs.AsNoTracking().Where(x => x.Status == RunStatus.Running).OrderByDescending(x => x.StartUtc).FirstOrDefaultAsync();
        if (run is null) return Ok(new { running = false, prompts = Array.Empty<object>() });

        var prompts = await _db.OperatorPrompts.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Status == PromptStatus.Open)
            .OrderByDescending(x => x.TriggeredAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.TriggeredAtUtc,
                x.ThresholdSec,
                x.PayloadJson
            })
            .ToListAsync();

        return Ok(new { running = true, runId = run.Id, prompts });
    }

    public record ResolvePromptRequest(string? ResolutionCode, string? ReasonCode, string? Comment, Guid? NewProductId);

    [HttpPost("{promptId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid promptId, [FromBody] ResolvePromptRequest req)
    {
        var prompt = await _db.OperatorPrompts.FirstOrDefaultAsync(x => x.Id == promptId);
        if (prompt is null) return NotFound();
        if (prompt.Status != PromptStatus.Open) return Ok(new { ok = true });

        prompt.Status = PromptStatus.Resolved;
        prompt.ResolvedAtUtc = DateTime.UtcNow;
        prompt.ResolvedBy = Actor();
        prompt.ResolutionCode = req.ResolutionCode;
        prompt.ReasonCode = req.ReasonCode;
        prompt.Comment = req.Comment;
        prompt.UpdatedAtUtc = DateTime.UtcNow;
        prompt.UpdatedBy = Actor();
        prompt.Source = "ui";
        prompt.DataStamp = Guid.NewGuid().ToString("N");

        // Audit
        _db.OperatorEvents.Add(new OperatorEvent
        {
            RunId = prompt.RunId,
            TsUtc = DateTime.UtcNow,
            Type = $"PROMPT_RESOLVED:{prompt.Type}",
            ReasonCode = req.ReasonCode ?? req.ResolutionCode,
            Comment = req.Comment,
            CreatedBy = Actor(),
            Source = "ui"
        });

        // CHANGEOVER flow: if YES -> start new run with selected product
        if (prompt.Type == "CHANGEOVER_QUESTION" && string.Equals(req.ResolutionCode, "YES", StringComparison.OrdinalIgnoreCase))
        {
            if (req.NewProductId is null) return BadRequest(new { error = "newProductId required" });

            var oldRun = await _db.Runs.FirstOrDefaultAsync(x => x.Id == prompt.RunId);
            if (oldRun is null) return BadRequest(new { error = "Run not found" });
            if (oldRun.Status != RunStatus.Running) return BadRequest(new { error = "Run not running" });

            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.NewProductId.Value);
            if (product is null) return BadRequest(new { error = "Product not found" });
            if (product.PublishedAtUtc is null) return BadRequest(new { error = "Product not published" });

            // end production for old run, but keep counting while WIP drains
            oldRun.Status = RunStatus.Draining;
            oldRun.ProductionEndUtc ??= DateTime.UtcNow;
            oldRun.GreyZoneStartUtc = DateTime.UtcNow;
            oldRun.GreyZoneEndUtc = DateTime.UtcNow.AddSeconds(oldRun.WipWindowSec);
            oldRun.UpdatedAtUtc = DateTime.UtcNow;
            oldRun.UpdatedBy = Actor();
            oldRun.Source = "ui";
            oldRun.DataStamp = Guid.NewGuid().ToString("N");

            var newRun = new Run
            {
                ProductId = product.Id,
                StartUtc = DateTime.UtcNow,
                Status = RunStatus.Running,
                WipWindowSec = oldRun.WipWindowSec,
                GreyZoneStartUtc = DateTime.UtcNow,
                GreyZoneEndUtc = DateTime.UtcNow.AddSeconds(oldRun.WipWindowSec),
                CreatedBy = Actor(),
                Source = "ui"
            };
            _db.Runs.Add(newRun);

            _db.OperatorEvents.Add(new OperatorEvent
            {
                RunId = oldRun.Id,
                TsUtc = DateTime.UtcNow,
                Type = "CHANGEOVER_CONFIRMED",
                ReasonCode = "YES",
                Comment = $"New product: {product.Code}",
                CreatedBy = Actor(),
                Source = "ui"
            });
            _db.OperatorEvents.Add(new OperatorEvent
            {
                RunId = newRun.Id,
                TsUtc = DateTime.UtcNow,
                Type = "RUN_START_FROM_CHANGEOVER",
                ReasonCode = null,
                Comment = $"From run {oldRun.Id}",
                CreatedBy = Actor(),
                Source = "ui"
            });

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, newRunId = newRun.Id });
        }

        // END production flow (no new product): keep run in DRaining until last piece clears
        if (prompt.Type == "CHANGEOVER_QUESTION" && string.Equals(req.ResolutionCode, "END", StringComparison.OrdinalIgnoreCase))
        {
            var run = await _db.Runs.FirstOrDefaultAsync(x => x.Id == prompt.RunId);
            if (run is null) return BadRequest(new { error = "Run not found" });
            if (run.Status != RunStatus.Running) return Ok(new { ok = true });

            run.Status = RunStatus.Draining;
            run.ProductionEndUtc ??= DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            run.UpdatedBy = Actor();
            run.Source = "ui";
            run.DataStamp = Guid.NewGuid().ToString("N");

            _db.OperatorEvents.Add(new OperatorEvent
            {
                RunId = run.Id,
                TsUtc = DateTime.UtcNow,
                Type = "PRODUCTION_END_FROM_PROMPT",
                ReasonCode = req.ReasonCode,
                Comment = req.Comment,
                CreatedBy = Actor(),
                Source = "ui"
            });

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, draining = true });
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
