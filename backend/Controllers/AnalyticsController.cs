using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("analytics")]
[Authorize(Policy = "OperatorOrAdmin")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AnalyticsBucketService _buckets;
    public AnalyticsController(AppDbContext db, AnalyticsBucketService buckets) { _db = db; _buckets = buckets; }

    private static DateTime FloorToBucket(DateTime tsUtc, int minutes)
    {
        var m = (tsUtc.Minute / minutes) * minutes;
        return new DateTime(tsUtc.Year, tsUtc.Month, tsUtc.Day, tsUtc.Hour, m, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Subpage 1: "Per pâine" (P1->P2), side-by-side, aligned by (PosInRow + PieceSeqIndex). Rolling max 30.
    /// A new P1 instance is sampled every 3 minutes using the earliest complete P1 row in that window.
    /// </summary>
    [HttpGet("per-bread")]
    public async Task<IActionResult> PerBread([FromQuery] Guid? runId = null, [FromQuery] int sampleEveryMinutes = 3, [FromQuery] int maxInstances = 30)
    {
        sampleEveryMinutes = Math.Clamp(sampleEveryMinutes, 1, 30);
        maxInstances = Math.Clamp(maxInstances, 5, 60);

        Run? run;
        if (runId.HasValue)
        {
            run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId.Value);
        }
        else
        {
            run = await _db.Runs.AsNoTracking()
                .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
                .OrderByDescending(x => x.StartUtc)
                .FirstOrDefaultAsync();
        }

        if (run is null) return Ok(new { running = false, instances = Array.Empty<object>() });

        var productId = run.ProductId;
        var tolP1 = await _db.ProductTolerances.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId && x.Point == PointCode.P1);
        var tolP2 = await _db.ProductTolerances.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId && x.Point == PointCode.P2);

        // Collect P1 rows (by RowIndex) with their start timestamps.
        var to = (run.EndUtc ?? DateTime.UtcNow).AddMinutes(1);
        var p1Rows = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P1 && x.RowIndex != null && x.TsUtc >= run.StartUtc && x.TsUtc <= to)
            .GroupBy(x => x.RowIndex)
            .Select(g => new { RowIndex = g.Key!.Value, StartUtc = g.Min(x => x.TsUtc), Count = g.Count(), MaxPos = g.Max(x => x.PosInRow ?? 0) })
            .OrderBy(x => x.StartUtc)
            .ToListAsync();

        if (p1Rows.Count == 0) return Ok(new { running = true, runId = run.Id, instances = Array.Empty<object>() });

        // Sample one row at most every sampleEveryMinutes.
        var sampled = new List<(int rowIndex, DateTime startUtc, int maxPos)>();
        DateTime? last = null;
        foreach (var r in p1Rows)
        {
            if (last is null || r.StartUtc >= last.Value.AddMinutes(sampleEveryMinutes))
            {
                sampled.Add((r.RowIndex, r.StartUtc, Math.Max(1, r.MaxPos)));
                last = r.StartUtc;
            }
        }

        // Keep rolling maxInstances (latest).
        if (sampled.Count > maxInstances)
            sampled = sampled.Skip(sampled.Count - maxInstances).ToList();

        var rowIndexes = sampled.Select(x => x.rowIndex).ToList();

        // Pull all P1 pieces for sampled rows.
        var p1Pieces = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P1 && x.RowIndex != null && rowIndexes.Contains(x.RowIndex.Value))
            .Select(x => new { x.RowIndex, pos = x.PosInRow, x.PieceSeqIndex, x.TsUtc, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
            .ToListAsync();

        // Determine positions from sampled rows.
        var positions = Math.Max(1, p1Pieces.Max(x => x.pos ?? 0));

        // Pull matching P2 by counted indices (strict). Keep small by limiting to the set of pieceSeq indices.
        var pieceSeqs = p1Pieces.Select(x => x.PieceSeqIndex).Distinct().ToList();
        var p2Pieces = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P2 && pieceSeqs.Contains(x.PieceSeqIndex))
            .Select(x => new { x.PieceSeqIndex, pos = x.PosInRow, x.TsUtc, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
            .ToListAsync();

        var instances = new List<object>();
        foreach (var s in sampled)
        {
            var row = p1Pieces.Where(x => x.RowIndex == s.rowIndex).ToList();
            if (row.Count == 0) continue;

            // Build per-position cells.
            var cells = new List<object>();
            DateTime? p2Time = null;
            for (int pos = 1; pos <= positions; pos++)
            {
                var p1 = row.FirstOrDefault(x => (x.pos ?? 0) == pos);
                if (p1 is null)
                {
                    cells.Add(new { pos, p1 = (object?)null, p2 = (object?)null });
                    continue;
                }

                var p2 = p2Pieces.FirstOrDefault(x => x.PieceSeqIndex == p1.PieceSeqIndex);
                if (p2 is not null)
                    p2Time = p2Time is null ? p2.TsUtc : (p2.TsUtc < p2Time.Value ? p2.TsUtc : p2Time.Value);

                cells.Add(new
                {
                    pos,
                    pieceSeqIndex = p1.PieceSeqIndex,
                    p1 = new
                    {
                        widthMm = Math.Round(p1.WidthMm, 2),
                        lengthMm = Math.Round(p1.LengthMm, 2),
                        heightMm = Math.Round(p1.HeightMm, 2),
                        weightG = Math.Round(p1.EstimatedWeightG, 2),
                        volumeL = Math.Round((decimal)p1.VolumeMm3 / 1_000_000m, 4)
                    },
                    p2 = p2 is null ? null : new
                    {
                        widthMm = Math.Round(p2.WidthMm, 2),
                        lengthMm = Math.Round(p2.LengthMm, 2),
                        heightMm = Math.Round(p2.HeightMm, 2),
                        weightG = Math.Round(p2.EstimatedWeightG, 2),
                        volumeL = Math.Round((decimal)p2.VolumeMm3 / 1_000_000m, 4)
                    }
                });
            }

            instances.Add(new
            {
                rowIndex = s.rowIndex,
                p1TimeUtc = s.startUtc,
                p2TimeUtc = p2Time,
                positions,
                cells
            });
        }

        return Ok(new
        {
            running = true,
            runId = run.Id,
            positions,
            tolerances = new
            {
                p1 = tolP1,
                p2 = tolP2
            },
            instances
        });
    }

    /// <summary>
    /// Subpage 2: 20-minute buckets (rolling 12) with per-position P1/P2 means, VIS Pareto over the same counted pieces,
    /// and P3 time-based "amalgam" distributed randomly over positions (persisted for reporting).
    /// </summary>
    [HttpGet("buckets20")]
    public async Task<IActionResult> Buckets20([FromQuery] Guid? runId = null, [FromQuery] int maxBuckets = 12)
    {
        maxBuckets = Math.Clamp(maxBuckets, 1, 50);

        Run? run;
        if (runId.HasValue)
        {
            run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId.Value);
        }
        else
        {
            run = await _db.Runs.AsNoTracking()
                .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
                .OrderByDescending(x => x.StartUtc)
                .FirstOrDefaultAsync();
        }

        if (run is null) return Ok(new { running = false, buckets = Array.Empty<object>() });

        var buckets = await _buckets.GetLastBuckets20(run.Id, maxBuckets);

        // Include tolerances so the UI can underline/flag values that exceed min/max.
        var tolP1 = await _db.ProductTolerances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == run.ProductId && x.Point == PointCode.P1);
        var tolP2 = await _db.ProductTolerances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == run.ProductId && x.Point == PointCode.P2);
        var tolP3 = await _db.ProductTolerances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == run.ProductId && x.Point == PointCode.P3);

        return Ok(new
        {
            running = true,
            runId = run.Id,
            bucketMinutes = 20,
            maxBuckets,
            tolerances = new
            {
                p1 = tolP1,
                p2 = tolP2,
                p3 = tolP3,
            },
            buckets
        });
    }

    /// <summary>
    /// Real chart data for Live View: current run P3 volume (L) + constant lines for Avg last 10 and Best last 10.
    /// "Best" = lowest out-of-tolerance rate at P3 within last 10 completed runs of same product.
    /// </summary>
    [HttpGet("p3-volume")]
    public async Task<IActionResult> P3Volume()
    {
        var run = await _db.Runs.AsNoTracking().Include(r => r.Product)
            .Where(x => x.Status == RunStatus.Running)
            .OrderByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync();
        if (run is null) return Ok(new { running = false, points = Array.Empty<object>() });

        var productId = run.ProductId;

        // Current series (last 60 pieces)
        var current = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P3)
            .OrderByDescending(x => x.TsUtc)
            .Take(60)
            .Select(x => new { x.VolumeMm3 })
            .ToListAsync();
        current.Reverse();

        // Last 10 completed runs for same product
        var last10 = await _db.Runs.AsNoTracking()
            .Where(x => x.ProductId == productId && x.Status != RunStatus.Running && x.EndUtc != null)
            .OrderByDescending(x => x.EndUtc)
            .Take(10)
            .Select(x => x.Id)
            .ToListAsync();

        decimal avg10 = 0m;
        decimal best10 = 0m;

        // Tolerance mid target (if defined)
        var tol = await _db.ProductTolerances.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId && x.Point == PointCode.P3);
        var targetMidVolMm3 = (tol?.VolumeMinMm3 is not null && tol?.VolumeMaxMm3 is not null)
            ? (tol!.VolumeMinMm3!.Value + tol.VolumeMaxMm3!.Value) / 2m
            : (decimal?)null;

        if (last10.Count > 0)
        {
            var stats = new List<(Guid runId, decimal meanVolL, decimal ootRate)>();
            foreach (var rid in last10)
            {
                var vols = await _db.MeasurementEvents.AsNoTracking()
                    .Where(x => x.RunId == rid && x.Point == PointCode.P3)
                    .Select(x => x.VolumeMm3)
                    .ToListAsync();
                if (vols.Count == 0) continue;

                var meanMm3 = vols.Average();
                var meanL = (decimal)meanMm3 / 1_000_000m;

                decimal ootRate = 0m;
                if (tol?.VolumeMinMm3 is not null && tol?.VolumeMaxMm3 is not null)
                {
                    var oot = vols.Count(v => v < tol.VolumeMinMm3.Value || v > tol.VolumeMaxMm3.Value);
                    ootRate = Math.Round((decimal)oot / vols.Count, 4);
                }

                stats.Add((rid, meanL, ootRate));
            }

            if (stats.Count > 0)
            {
                avg10 = Math.Round(stats.Average(s => s.meanVolL), 4);

                // Best = min oot; tie-breaker: closest to target mid volume
                var best = stats
                    .OrderBy(s => s.ootRate)
                    .ThenBy(s => targetMidVolMm3 is null ? 0m : Math.Abs((s.meanVolL * 1_000_000m) - targetMidVolMm3.Value))
                    .First();
                best10 = Math.Round(best.meanVolL, 4);
            }
        }

        var points = current.Select((x, i) => new
        {
            idx = i + 1,
            current = Math.Round((decimal)x.VolumeMm3 / 1_000_000m, 4),
            avg10,
            best10
        }).ToList();

        return Ok(new { running = true, runId = run.Id, points });
    }

    /// <summary>
    /// Heatmap-oriented rollup (10-minute buckets by default), sample is based on ROWS (not pieces):
    /// - selects first N rows at P1 inside each bucket (each row can contain 6–14+ pieces)
    /// - aggregates P1 and P2 dimensions across all pieces in the sampled rows
    /// - estimates VIS good vs defect counts using speed+distance arrival window (segments 2+3)
    /// - aggregates P3 dimensions (post-freeze) using speed+distance arrival window (segments 2+3+4)
    /// Notes:
    /// - VIS stores only defects; "good" is estimated as (expected pieces from P2 rows) - defects in window.
    /// - This is best-effort correlation per the project's strategy.
    /// </summary>
    [HttpGet("heatmap/line")]
    public async Task<IActionResult> HeatmapLine([FromQuery] Guid? runId = null, [FromQuery] int bucketMinutes = 10, [FromQuery] int sampleRows = 20)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);
        sampleRows = Math.Clamp(sampleRows, 5, 200);

        Run? run;
        if (runId.HasValue)
        {
            run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId.Value);
        }
        else
        {
            run = await _db.Runs.AsNoTracking()
                .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
                .OrderByDescending(x => x.StartUtc)
                .FirstOrDefaultAsync();
        }

        if (run is null) return Ok(new { running = false, buckets = Array.Empty<object>() });

        // Product config used for speed+distance mapping
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == run.ProductId);
        var segments = await _db.ProductSegments.AsNoTracking()
            .Where(x => x.ProductId == run.ProductId)
            .ToDictionaryAsync(x => x.SegmentId, x => x);

        // Pull P1 ROWS for the run (bounded by run time). For active run, up to now.
        var from = run.StartUtc;
        var to = (run.EndUtc ?? DateTime.UtcNow).AddMinutes(1);

        var p1Rows = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P1 && x.RowIndex != null && x.TsUtc >= from && x.TsUtc <= to)
            .GroupBy(x => x.RowIndex)
            .Select(g => new { RowIndex = g.Key!.Value, StartUtc = g.Min(x => x.TsUtc) })
            .OrderBy(x => x.StartUtc)
            .ToListAsync();

        if (p1Rows.Count == 0) return Ok(new { running = true, runId = run.Id, buckets = Array.Empty<object>() });

        // Build bucket -> first N pieces
        static DateTime BucketStart(DateTime ts, int minutes)
        {
            var m = (ts.Minute / minutes) * minutes;
            return new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, m, 0, DateTimeKind.Utc);
        }

        var bucketRows = new Dictionary<DateTime, List<int>>();
        foreach (var r in p1Rows)
        {
            var b = BucketStart(r.StartUtc, bucketMinutes);
            if (!bucketRows.TryGetValue(b, out var list))
            {
                list = new List<int>();
                bucketRows[b] = list;
            }
            if (list.Count < sampleRows)
                list.Add(r.RowIndex);
        }

        decimal Mean(IEnumerable<decimal> xs) => xs.Any() ? xs.Average() : 0m;

        var buckets = new List<object>();
        foreach (var kv in bucketRows.OrderBy(x => x.Key))
        {
            var bStart = kv.Key;
            var rows = kv.Value;

            // Speed snapshot for this bucket (industrial-friendly: avoids per-row DB queries)
            var bEnd = bStart.AddMinutes(bucketMinutes);
            var enc = await _db.EncoderEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.TsUtc >= bStart && x.TsUtc < bEnd && (x.SegmentId == 2 || x.SegmentId == 3 || x.SegmentId == 4))
                .Select(x => new { x.SegmentId, x.SpeedMps })
                .ToListAsync();

            decimal BucketSpeed(int segId)
            {
                var vals = enc.Where(e => e.SegmentId == segId && e.SpeedMps > 0.05m).Select(e => e.SpeedMps).ToList();
                if (vals.Count > 0) return vals.Average();
                if (segments.TryGetValue(segId, out var seg) && seg.TargetSpeedMps > 0.05m) return seg.TargetSpeedMps;
                return 0.6m;
            }

            int TravelSec(params int[] segIds)
            {
                decimal total = 0m;
                foreach (var sid in segIds)
                {
                    if (!segments.TryGetValue(sid, out var seg)) continue;
                    var v = BucketSpeed(sid);
                    total += seg.LengthM / v;
                }
                return (int)Math.Round(total);
            }

            // Pull P1/P2 pieces for selected rows
            var p1 = await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Point == PointCode.P1 && x.RowIndex != null && rows.Contains(x.RowIndex.Value))
                .Select(x => new { x.RowIndex, x.TsUtc, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
                .ToListAsync();

            var p2 = await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id && x.Point == PointCode.P2 && x.RowIndex != null && rows.Contains(x.RowIndex.Value))
                .Select(x => new { x.RowIndex, x.TsUtc, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
                .ToListAsync();

            var p1w = Mean(p1.Select(x => x.WidthMm));
            var p1l = Mean(p1.Select(x => x.LengthMm));
            var p1h = Mean(p1.Select(x => x.HeightMm));
            var p1volL = Mean(p1.Select(x => x.VolumeMm3)) / 1_000_000m;
            var p1wt = Mean(p1.Select(x => x.EstimatedWeightG));

            var p2w = Mean(p2.Select(x => x.WidthMm));
            var p2l = Mean(p2.Select(x => x.LengthMm));
            var p2h = Mean(p2.Select(x => x.HeightMm));
            var p2volL = Mean(p2.Select(x => x.VolumeMm3)) / 1_000_000m;
            var p2wt = Mean(p2.Select(x => x.EstimatedWeightG));

            // VIS + P3 time windows derived from P2 rows (speed+distance)
            // For each row: determine a representative timestamp at P2 (min ts) and estimated row duration.
            var idealCt = (product?.IdealCycleTimeSec is not null && product.IdealCycleTimeSec > 0)
                ? (decimal)product.IdealCycleTimeSec
                : 0.5m;

            var perRow = p2
                .GroupBy(x => x.RowIndex!.Value)
                .Select(g => new
                {
                    RowIndex = g.Key,
                    TsP2 = g.Min(x => x.TsUtc),
                    Count = g.Count(),
                    RowWindowSec = Math.Max(6m, g.Count() * idealCt) // rough
                })
                .ToList();

            DateTime? visStart = null, visEnd = null, p3Start = null, p3End = null;
            var expectedPieces = perRow.Sum(r => r.Count);

            foreach (var r in perRow)
            {
                var tVis = r.TsP2.AddSeconds(TravelSec(2, 3));
                var tP3 = r.TsP2.AddSeconds(TravelSec(2, 3, 4));

                var rowVisStart = tVis.AddSeconds(-2);
                var rowVisEnd = tVis.AddSeconds((double)r.RowWindowSec + 2);
                var rowP3Start = tP3.AddSeconds(-2);
                var rowP3End = tP3.AddSeconds((double)r.RowWindowSec + 2);

                visStart = visStart is null ? rowVisStart : (rowVisStart < visStart ? rowVisStart : visStart);
                visEnd = visEnd is null ? rowVisEnd : (rowVisEnd > visEnd ? rowVisEnd : visEnd);
                p3Start = p3Start is null ? rowP3Start : (rowP3Start < p3Start ? rowP3Start : p3Start);
                p3End = p3End is null ? rowP3End : (rowP3End > p3End ? rowP3End : p3End);
            }

            var defectCount = 0;
            var visTotal = 0;
            var visEstimated = true;

            if (visStart is not null && visEnd is not null)
            {
                // Camera counts ALL pieces (OK + defects). If OK events are not available, we fallback to expectedPieces and mark as Estimated.
                var visCounts = await _db.VisualDefectEvents.AsNoTracking()
                    .Where(x => x.RunId == run.Id && x.TsUtc >= visStart && x.TsUtc <= visEnd)
                    .GroupBy(_ => 1)
                    .Select(g => new { Total = g.Count(), Defects = g.Sum(x => x.IsDefect ? 1 : 0) })
                    .FirstOrDefaultAsync();

                visTotal = visCounts?.Total ?? 0;
                defectCount = visCounts?.Defects ?? 0;
                visEstimated = visTotal <= 0;
                if (visTotal > 0 && defectCount > visTotal) visTotal = defectCount; // safety clamp
            }

            var denom = visTotal > 0 ? visTotal : expectedPieces;
            var goodCount = denom > 0 ? Math.Max(0, denom - defectCount) : 0;
            var defectRate = denom > 0 ? Math.Round((decimal)defectCount / denom, 4) : 0m;
var p3Count = 0;
            decimal p3w = 0, p3l = 0, p3h = 0, p3volL = 0, p3wt = 0;
            if (p3Start is not null && p3End is not null)
            {
                var p3 = await _db.MeasurementEvents.AsNoTracking()
                    .Where(x => x.RunId == run.Id && x.Point == PointCode.P3 && x.TsUtc >= p3Start && x.TsUtc <= p3End)
                    .Select(x => new { x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
                    .ToListAsync();

                p3Count = p3.Count;
                p3w = Mean(p3.Select(x => x.WidthMm));
                p3l = Mean(p3.Select(x => x.LengthMm));
                p3h = Mean(p3.Select(x => x.HeightMm));
                p3volL = Mean(p3.Select(x => x.VolumeMm3)) / 1_000_000m;
                p3wt = Mean(p3.Select(x => x.EstimatedWeightG));
            }

            buckets.Add(new
            {
                bucketStartUtc = bStart,
                sampleRows = rows.Count,
                p1Pieces = p1.Count,
                p2Pieces = p2.Count,
                p1 = new { widthMm = Math.Round(p1w, 2), lengthMm = Math.Round(p1l, 2), heightMm = Math.Round(p1h, 2), volumeL = Math.Round(p1volL, 4), weightG = Math.Round(p1wt, 2) },
                p2 = new { widthMm = Math.Round(p2w, 2), lengthMm = Math.Round(p2l, 2), heightMm = Math.Round(p2h, 2), volumeL = Math.Round(p2volL, 4), weightG = Math.Round(p2wt, 2) },
                delta12 = new { widthMm = Math.Round(p2w - p1w, 2), lengthMm = Math.Round(p2l - p1l, 2), heightMm = Math.Round(p2h - p1h, 2), volumeL = Math.Round(p2volL - p1volL, 4), weightG = Math.Round(p2wt - p1wt, 2) },
                vis = new
                {
                    expectedPieces,
                    totalPieces = visTotal,
                    estimated = visEstimated,
                    defectCount,
                    goodCount,
                    defectRate,
                    windowStartUtc = visStart,
                    windowEndUtc = visEnd
                },
                p3 = new
                {
                    count = p3Count,
                    widthMm = Math.Round(p3w, 2),
                    lengthMm = Math.Round(p3l, 2),
                    heightMm = Math.Round(p3h, 2),
                    volumeL = Math.Round(p3volL, 4),
                    weightG = Math.Round(p3wt, 2),
                    windowStartUtc = p3Start,
                    windowEndUtc = p3End
                }
            });
        }

        return Ok(new { running = true, runId = run.Id, bucketMinutes, sampleRows, buckets });
    }
}
