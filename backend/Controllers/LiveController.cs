using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("live")]
[Authorize(Policy = "OperatorOrAdmin")]
public class LiveController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IVisImageCache _img;

    public LiveController(AppDbContext db, IVisImageCache img)
    {
        _db = db;
        _img = img;
    }

    [HttpGet("current-run")]
    public async Task<IActionResult> CurrentRun()
    {
        // Prefer RUNNING; fallback to DRAINING (end production but still counting downstream)
        var run = await _db.Runs
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
            .OrderBy(x => x.Status == RunStatus.Running ? 0 : 1)
            .ThenByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync();

        if (run is null) return Ok(new { running = false });

        var batches = await _db.Batches
            .AsNoTracking()
            .Where(x => x.RunId == run.Id)
            .OrderBy(x => x.BatchNumber)
            .Select(x => new
            {
                x.Id,
                x.BatchNumber,
                x.Status,
                x.Disposition,
                x.MixedAtUtc,
                x.AddedToLineAtUtc,
                x.ProofingActualMinutes
            })
            .ToListAsync();

        return Ok(new
        {
            running = true,
            run = new
            {
                runId = run.Id,
                run.StartUtc,
                run.Status,
                run.ProductionEndUtc,
                product = new { run.ProductId, run.Product!.Code, run.Product.Name },
                batches
            }
        });
    }

    [HttpGet("points/{point}")]
    public async Task<IActionResult> PointSnapshot(string point)
    {
        if (!Enum.TryParse<PointCode>(point, true, out var p))
            return BadRequest(new { error = "Invalid point" });

        var run = await _db.Runs.AsNoTracking()
            .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
            .OrderBy(x => x.Status == RunStatus.Running ? 0 : 1)
            .ThenByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync();
        if (run is null) return Ok(new { running = false, point = p.ToString() });

        var since = DateTime.UtcNow.AddMinutes(-2);

        if (p is PointCode.P1 or PointCode.P2)
        {
            var lastRowIndex = await _db.MeasurementEvents
                .AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Point == p && x.RowIndex != null && x.TsUtc >= since)
                .OrderByDescending(x => x.RowIndex)
                .Select(x => x.RowIndex)
                .FirstOrDefaultAsync();

            var lastRow = await _db.MeasurementEvents
                .AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Point == p && x.RowIndex == lastRowIndex && x.TsUtc >= since)
                .OrderBy(x => x.PosInRow)
                .Take(14)
                .Select(x => new
                {
                    x.PieceUid,
                    x.RowIndex,
                    x.PosInRow,
                    x.WidthMm,
                    x.LengthMm,
                    x.HeightMm,
                    x.VolumeMm3,
                    x.EstimatedWeightG,
                    x.WeightConfidence,
                    x.CohortId,
                    x.TsUtc
                })
                .ToListAsync();

            return Ok(new { running = true, runId = run.Id, point = p.ToString(), pieces = lastRow });
        }
        else
        {
            var lastPieces = await _db.MeasurementEvents
                .AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Point == p && x.TsUtc >= since)
                .OrderByDescending(x => x.TsUtc)
                .Take(25)
                .Select(x => new
                {
                    x.PieceUid,
                    x.WidthMm,
                    x.LengthMm,
                    x.HeightMm,
                    x.VolumeMm3,
                    x.EstimatedWeightG,
                    x.WeightConfidence,
                    x.TsUtc
                })
                .ToListAsync();

            return Ok(new { running = true, runId = run.Id, point = p.ToString(), pieces = lastPieces });
        }
    }

    [HttpGet("vis/defects")]
    public async Task<IActionResult> VisDefects([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var run = await _db.Runs.AsNoTracking()
            .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
            .OrderBy(x => x.Status == RunStatus.Running ? 0 : 1)
            .ThenByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync();
        if (run is null) return Ok(new { running = false, defects = Array.Empty<object>() });

        var since = DateTime.UtcNow.AddMinutes(-10);
        var defects = await _db.VisualDefectEvents
            .AsNoTracking()
            .Where(x => x.RunId == run.Id && x.IsDefect && x.TsUtc >= since)
            .OrderByDescending(x => x.TsUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.TsUtc,
                x.DefectType,
                x.Confidence,
                x.ImageTokenId
            })
            .ToListAsync();

        return Ok(new { running = true, runId = run.Id, defects });
    }

    [HttpGet("images/{token}")]
    [Authorize(Policy = "OperatorOrAdmin")] // token is short-lived TTL, no history
    public async Task<IActionResult> GetImage(string token)
    {
        var bytes = await _img.GetAsync(token);
        if (bytes is null) return NotFound();
        return File(bytes, "image/png", enableRangeProcessing: false);
    }

    // Lightweight realtime for VIS + prompts (SSE). Keeps frontend simple.
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        while (!ct.IsCancellationRequested)
        {
            var run = await _db.Runs.AsNoTracking()
                .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
                .OrderBy(x => x.Status == RunStatus.Running ? 0 : 1)
                .ThenByDescending(x => x.StartUtc)
                .FirstOrDefaultAsync(ct);
            if (run is null)
            {
                await WriteSse("run", new { running = false }, ct);
                await Task.Delay(1000, ct);
                continue;
            }

            var since = DateTime.UtcNow.AddMinutes(-10);

            // VIS: camera counts ALL pieces (OK + defects). UI shows images only for defects (live token TTL).
            var visCounts = await _db.VisualDefectEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.TsUtc >= since)
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Defects = g.Sum(x => x.IsDefect ? 1 : 0) })
                .FirstOrDefaultAsync(ct);

            var visTotal = visCounts?.Total ?? 0;
            var visDefects = visCounts?.Defects ?? 0;
            var visGood = Math.Max(0, visTotal - visDefects);
            var visDefectRate = visTotal > 0 ? Math.Round((decimal)visDefects / visTotal, 4) : 0m;

            var defects = await _db.VisualDefectEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.IsDefect && x.TsUtc >= since)
                .OrderByDescending(x => x.TsUtc)
                .Take(20)
                .Select(x => new { x.Id, x.TsUtc, x.DefectType, x.Confidence, x.ImageTokenId })
                .ToListAsync(ct);
var prompts = await _db.OperatorPrompts.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Status == PromptStatus.Open)
                .OrderByDescending(x => x.TriggeredAtUtc)
                .Take(5)
                .Select(x => new { x.Id, x.Type, x.TriggeredAtUtc, x.ThresholdSec, x.PayloadJson })
                .ToListAsync(ct);

            var nowUtc = DateTime.UtcNow;
            var alerts = await _db.Alerts.AsNoTracking()
                .Where(a => a.RunId == run.Id && a.Status != AlertStatus.Closed)
                .Where(a => a.Status != AlertStatus.Snoozed || (a.SnoozedUntilUtc != null && a.SnoozedUntilUtc <= nowUtc))
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.TriggeredAtUtc)
                .Take(5)
                .Select(a => new { a.Id, a.Type, a.Title, a.Message, severity = a.Severity.ToString(), status = a.Status.ToString(), a.TriggeredAtUtc, a.SnoozedUntilUtc })
                .ToListAsync(ct);

            await WriteSse("snapshot", new { running = true, runId = run.Id, status = run.Status.ToString(), productionEndUtc = run.ProductionEndUtc, vis = new { total = visTotal, defects = visDefects, good = visGood, defectRate = visDefectRate }, defects, prompts, alerts }, ct);
            await Task.Delay(1000, ct);
        }
    }

    
    private static readonly JsonSerializerOptions _sseJson = new(JsonSerializerDefaults.Web);

private async Task WriteSse(string evt, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, _sseJson);
        await Response.WriteAsync($"event: {evt}\n", ct);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
