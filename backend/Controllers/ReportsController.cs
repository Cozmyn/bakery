using System.Text;
using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

/// <summary>
/// Reporting endpoints (no raw image retention):
/// - List runs
/// - Run report with OEE per segment + losses + waste
/// - CSV export (bucketed OEE/losses)
/// </summary>
[ApiController]
[Route("reports")]
[Authorize(Policy = "OperatorOrAdmin")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OeeService _oee;
    private readonly AnalyticsBucketService _buckets;

    public ReportsController(AppDbContext db, OeeService oee, AnalyticsBucketService buckets)
    {
        _db = db;
        _oee = oee;
        _buckets = buckets;
    }

    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] int days = 7)
    {
        days = Math.Clamp(days, 1, 365);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);

        var runs = await _db.Runs.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.StartUtc >= from)
            .OrderByDescending(r => r.StartUtc)
            .Take(200)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.StartUtc,
                r.ProductionEndUtc,
                r.EndUtc,
                product = new { r.ProductId, code = r.Product!.Code, name = r.Product.Name }
            })
            .ToListAsync();

        // Add lightweight KPIs per run (counts + defect + mix scrap)
        var list = new List<object>();
        foreach (var r in runs)
        {
            var p3Count = await _db.MeasurementEvents.AsNoTracking().CountAsync(x => x.RunId == r.Id && x.Point == PointCode.P3);
            var visTotal = await _db.VisualDefectEvents.AsNoTracking().CountAsync(x => x.RunId == r.Id);
            var defects = await _db.VisualDefectEvents.AsNoTracking().CountAsync(x => x.RunId == r.Id && x.IsDefect);
            var visGood = Math.Max(0, visTotal - defects);
            var visDefectRate = visTotal > 0 ? Math.Round((decimal)defects / visTotal, 4) : 0m;
            var mixScrapUnits = await _db.BatchWasteEvents.AsNoTracking()
                .Where(x => x.RunId == r.Id && x.WasteType == "MIX_SCRAP")
                .SumAsync(x => (decimal?)x.EquivalentUnits) ?? 0m;

            list.Add(new
            {
                r.Id,
                r.Status,
                r.StartUtc,
                r.ProductionEndUtc,
                r.EndUtc,
                r.product,
                kpi = new { p3Count, visTotal, defects, visGood, visDefectRate, mixScrapUnits = Math.Round(mixScrapUnits, 2) }
            });
        }

        return Ok(new { fromUtc = from, toUtc = to, runs = list });
    }

    [HttpGet("run/{runId:guid}")]
    public async Task<IActionResult> Run(Guid runId, [FromQuery] int bucketMinutes = 10)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);

        var run = await _db.Runs.AsNoTracking().Include(r => r.Product).FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null) return NotFound(new { error = "Run not found" });

        var productId = run.ProductId;
        var tolerances = await _db.ProductTolerances.AsNoTracking()
            .Where(x => x.ProductId == productId)
            .ToDictionaryAsync(x => x.Point);

        // Counts
        var p1Count = await _db.MeasurementEvents.AsNoTracking().CountAsync(x => x.RunId == runId && x.Point == PointCode.P1);
        var p2Count = await _db.MeasurementEvents.AsNoTracking().CountAsync(x => x.RunId == runId && x.Point == PointCode.P2);
        var p3Count = await _db.MeasurementEvents.AsNoTracking().CountAsync(x => x.RunId == runId && x.Point == PointCode.P3);
        var visTotal = await _db.VisualDefectEvents.AsNoTracking().CountAsync(x => x.RunId == runId);
        var defects = await _db.VisualDefectEvents.AsNoTracking().CountAsync(x => x.RunId == runId && x.IsDefect);
        var visGood = Math.Max(0, visTotal - defects);
        var visDefectRate = visTotal > 0 ? Math.Round((decimal)defects / visTotal, 4) : 0m;

        // OOT counts per point (dimensions + volume + weight)
        async Task<int> Oot(PointCode p)
        {
            if (!tolerances.TryGetValue(p, out var tol)) return 0;
            var wMin = tol.WidthMinMm ?? decimal.MinValue;
            var wMax = tol.WidthMaxMm ?? decimal.MaxValue;
            var lMin = tol.LengthMinMm ?? decimal.MinValue;
            var lMax = tol.LengthMaxMm ?? decimal.MaxValue;
            var hMin = tol.HeightMinMm ?? decimal.MinValue;
            var hMax = tol.HeightMaxMm ?? decimal.MaxValue;
            var vMin = tol.VolumeMinMm3 ?? decimal.MinValue;
            var vMax = tol.VolumeMaxMm3 ?? decimal.MaxValue;
            var wtMin = tol.WeightMinG ?? decimal.MinValue;
            var wtMax = tol.WeightMaxG ?? decimal.MaxValue;
            return await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == runId && x.Point == p)
                .Where(x =>
                    x.WidthMm < wMin || x.WidthMm > wMax ||
                    x.LengthMm < lMin || x.LengthMm > lMax ||
                    x.HeightMm < hMin || x.HeightMm > hMax ||
                    x.VolumeMm3 < vMin || x.VolumeMm3 > vMax ||
                    x.EstimatedWeightG < wtMin || x.EstimatedWeightG > wtMax)
                .CountAsync();
        }

        var p1Oot = await Oot(PointCode.P1);
        var p2Oot = await Oot(PointCode.P2);
        var p3Oot = await Oot(PointCode.P3);

        // Waste: mix scrap
        var mixScrap = await _db.BatchWasteEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.WasteType == "MIX_SCRAP")
            .GroupBy(_ => 1)
            .Select(g => new
            {
                units = g.Sum(x => x.EquivalentUnits),
                value = g.Sum(x => x.ValueLoss),
                kg = g.Sum(x => x.AmountKg)
            })
            .FirstOrDefaultAsync() ?? new { units = 0m, value = 0m, kg = 0m };

        var batches = await _db.Batches.AsNoTracking()
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
                x.DiscardedAtUtc,
                x.DiscardAmountKg,
                x.DiscardReasonCode
            })
            .ToListAsync();

        // OEE + bucketed losses
        var oee = await _oee.ComputeRun(runId, bucketMinutes);

        return Ok(new
        {
            run = new
            {
                run.Id,
                run.Status,
                run.StartUtc,
                run.ProductionEndUtc,
                run.EndUtc,
                product = new { run.ProductId, code = run.Product!.Code, name = run.Product.Name }
            },
            counts = new { p1Count, p2Count, p3Count, visTotal, defects, visGood, visDefectRate },
            oot = new { p1 = p1Oot, p2 = p2Oot, p3 = p3Oot },
            waste = new { mixScrapUnits = Math.Round(mixScrap.units, 2), mixScrapValue = Math.Round(mixScrap.value, 2), mixScrapKg = Math.Round(mixScrap.kg, 3) },
            batches,
            oee
        });
    }

    [HttpGet("run/{runId:guid}/csv")]
    public async Task<IActionResult> RunCsv(Guid runId, [FromQuery] int bucketMinutes = 10)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);
        var report = await _oee.ComputeRun(runId, bucketMinutes);
        if (!report.Ok) return NotFound(new { error = "Run not found" });

        var sb = new StringBuilder();
        sb.AppendLine("bucket_start_utc,bucket_end_utc,segment,availability,performance,quality,oee,availability_loss_min,performance_loss_units,quality_loss_units");

        foreach (var b in report.Buckets)
        {
            foreach (var s in b.Segments)
            {
                sb.AppendLine($"{b.BucketStartUtc:O},{b.BucketEndUtc:O},{s.SegmentId},{s.Availability},{s.Performance},{s.Quality},{s.Oee},{s.Waterfall.AvailabilityLossMin},{s.Waterfall.PerformanceLossUnits},{s.Waterfall.QualityLossUnits}");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"run_{runId}_oee.csv");
    }

    /// <summary>
    /// Report: all persisted 20-minute analytics buckets for a run (used after production ends).
    /// </summary>
    [HttpGet("run/{runId:guid}/buckets20")]
    public async Task<IActionResult> RunBuckets20(Guid runId)
    {
        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null) return NotFound(new { error = "Run not found" });

        var buckets = await _buckets.GetAllBuckets20(runId);
        return Ok(new { runId, bucketMinutes = 20, buckets });
    }
}
