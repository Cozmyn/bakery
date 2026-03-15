using System.Text.Json;
using System.Security.Cryptography;
using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

public class AnalyticsBucketService
{
    private readonly AppDbContext _db;
    private readonly JsonSerializerOptions _json;

    // EF Core cannot project tuple literals inside expression trees (CS8143).
    // Use a simple DTO for P3 event projections.
    private sealed class P3EventRow
    {
        public Guid Id { get; set; }
        public decimal W { get; set; }
        public decimal L { get; set; }
        public decimal H { get; set; }
        public decimal Wt { get; set; }
        public decimal VolMm3 { get; set; }
    }

    public AnalyticsBucketService(AppDbContext db)
    {
        _db = db;
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private static DateTime FloorToRunBucket(DateTime runStartUtc, DateTime tsUtc, int minutes)
    {
        // Buckets aligned to run start (StartRun+0..N*minutes)
        var deltaMin = (tsUtc - runStartUtc).TotalMinutes;
        if (deltaMin < 0) deltaMin = 0;
        var idx = (int)Math.Floor(deltaMin / minutes);
        return runStartUtc.AddMinutes(idx * minutes);
    }

    private static int? TryReadSeed(string json)
    {
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("seed", out var s) && s.ValueKind == JsonValueKind.Number)
                return s.GetInt32();
        }
        catch { }
        return null;
    }

    private static int StableRandomPos(Guid measurementId, int seed, int positions)
    {
        // Stable pseudo-random position per measurement (prevents jitter as new measurements arrive).
        Span<byte> bytes = stackalloc byte[16];
        measurementId.TryWriteBytes(bytes);
        unchecked
        {
            uint h = (uint)seed;
            foreach (var b in bytes)
                h = (h * 16777619u) ^ b;
            return (int)(h % (uint)positions) + 1;
        }
    }

    public async Task<List<AnalyticsBucket20>> EnsureBuckets20(Guid runId, int bucketMinutes = 20)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 5, 60);

        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null) return new List<AnalyticsBucket20>();

        // Determine positions (line slots) from P1 data.
        // EF Core (Npgsql) does not translate DefaultIfEmpty() in scalar Max() projections reliably.
        // Max() ignores NULLs in SQL, so compute nullable max and coalesce.
        var maxPos = (await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == PointCode.P1)
            .MaxAsync(x => x.PosInRow)) ?? 0;
        var positions = Math.Max(1, maxPos);

        // Per-product travel-time heuristic (VIS -> P3)
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == run.ProductId);
        var visToP3Minutes = Math.Clamp(product?.VisToP3Minutes ?? 35, 1, 24 * 60);

        var from = run.StartUtc;
        var to = (run.EndUtc ?? DateTime.UtcNow);

        // Build candidate bucket starts based on P1 row timestamps (cheap).
        var p1Rows = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == PointCode.P1 && x.RowIndex != null && x.TsUtc >= from && x.TsUtc <= to)
            .GroupBy(x => x.RowIndex)
            .Select(g => g.Min(x => x.TsUtc))
            .ToListAsync();

        if (p1Rows.Count == 0) return new List<AnalyticsBucket20>();

        var bucketStarts = p1Rows
            .Select(ts => FloorToRunBucket(run.StartUtc, ts, bucketMinutes))
            .Distinct()
            .OrderBy(ts => ts)
            .ToList();

        // Persist buckets including the current in-progress bucket.
        // IsFinal is set only when run is closed and the corresponding downstream windows are expected to have completed.
        var now = DateTime.UtcNow;
        var allowUntil = (run.EndUtc ?? now);

        // Upsert per bucket.
        foreach (var bStart in bucketStarts)
        {
            var bEnd = bStart.AddMinutes(bucketMinutes);

            var existing = await _db.AnalyticsBuckets20.FirstOrDefaultAsync(x => x.RunId == runId && x.BucketStartUtc == bStart);
            // If already final and run is closed, we can skip recompute.
            if (existing is not null && existing.IsFinal && run.EndUtc is not null)
                continue;

            // Pull P1 events in bucket.
            var p1Events = await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == runId && x.Point == PointCode.P1 && x.PosInRow != null)
                .Where(x => x.TsUtc >= bStart && x.TsUtc < bEnd)
                .Select(x => new { pos = x.PosInRow!.Value, x.PieceSeqIndex, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
                .ToListAsync();

            if (p1Events.Count == 0)
            {
                // Nothing in this bucket; don't persist empty buckets.
                continue;
            }

            var p1StartPiece = p1Events.Min(x => x.PieceSeqIndex);
            var p1EndPiece = p1Events.Max(x => x.PieceSeqIndex);
            var p1Count = p1Events.Count;

            static decimal Mean(IEnumerable<decimal> xs) => xs.Any() ? xs.Average() : 0m;

            var p1PerPos = Enumerable.Range(1, positions)
                .Select(pos =>
                {
                    var xs = p1Events.Where(e => e.pos == pos).ToList();
                    return new
                    {
                        pos,
                        widthMm = Math.Round(Mean(xs.Select(e => e.WidthMm)), 2),
                        lengthMm = Math.Round(Mean(xs.Select(e => e.LengthMm)), 2),
                        heightMm = Math.Round(Mean(xs.Select(e => e.HeightMm)), 2),
                        weightG = Math.Round(Mean(xs.Select(e => e.EstimatedWeightG)), 2),
                        volumeL = Math.Round(Mean(xs.Select(e => (decimal)e.VolumeMm3)) / 1_000_000m, 4),
                        count = xs.Count
                    };
                })
                .ToList();

            // Pull P2 by strict counted piece range.
            var p2Events = await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == runId && x.Point == PointCode.P2 && x.PosInRow != null)
                .Where(x => x.PieceSeqIndex >= p1StartPiece && x.PieceSeqIndex <= p1EndPiece)
                .Select(x => new { pos = x.PosInRow!.Value, x.TsUtc, x.WidthMm, x.LengthMm, x.HeightMm, x.VolumeMm3, x.EstimatedWeightG })
                .ToListAsync();

            var p2Count = p2Events.Count;

            var p2FirstUtc = p2Events.Count > 0 ? p2Events.Min(x => x.TsUtc) : (DateTime?)null;
            var p2LastUtc = p2Events.Count > 0 ? p2Events.Max(x => x.TsUtc) : (DateTime?)null;

            var p2PerPos = Enumerable.Range(1, positions)
                .Select(pos =>
                {
                    var xs = p2Events.Where(e => e.pos == pos).ToList();
                    return new
                    {
                        pos,
                        widthMm = Math.Round(Mean(xs.Select(e => e.WidthMm)), 2),
                        lengthMm = Math.Round(Mean(xs.Select(e => e.LengthMm)), 2),
                        heightMm = Math.Round(Mean(xs.Select(e => e.HeightMm)), 2),
                        weightG = Math.Round(Mean(xs.Select(e => e.EstimatedWeightG)), 2),
                        volumeL = Math.Round(Mean(xs.Select(e => (decimal)e.VolumeMm3)) / 1_000_000m, 4),
                        count = xs.Count
                    };
                })
                .ToList();

            // VIS: pull all events in strict piece range for timing and pareto.
            var visAll = await _db.VisualDefectEvents.AsNoTracking()
                .Where(x => x.RunId == runId)
                .Where(x => x.PieceSeqIndex >= p1StartPiece && x.PieceSeqIndex <= p1EndPiece)
                .Select(x => new { x.TsUtc, x.IsDefect, x.DefectType })
                .ToListAsync();

            var visFirstUtc = visAll.Count > 0 ? visAll.Min(x => x.TsUtc) : (DateTime?)null;
            var visLastUtc = visAll.Count > 0 ? visAll.Max(x => x.TsUtc) : (DateTime?)null;

            var visDefects = visAll.Where(x => x.IsDefect)
                .GroupBy(x => x.DefectType)
                .Select(g => new { defectType = g.Key, count = g.Count() })
                .ToList();

            var totalDefects = visDefects.Sum(x => x.count);
            var pareto = visDefects
                .OrderByDescending(x => x.count)
                .ThenBy(x => x.defectType)
                .Select(x => new { x.defectType, x.count, pct = totalDefects > 0 ? Math.Round((decimal)x.count / totalDefects, 4) : 0m })
                .ToList();

            // P3: time window derived from VIS for this bucket (no tolerance inside buckets).
            DateTime? p3StartUtc = null;
            DateTime? p3EndUtc = null;

            List<P3EventRow> p3Events;
            if (visFirstUtc.HasValue && visLastUtc.HasValue)
            {
                p3StartUtc = visFirstUtc.Value.AddMinutes(visToP3Minutes);
                p3EndUtc = visLastUtc.Value.AddMinutes(visToP3Minutes);

                p3Events = await _db.MeasurementEvents.AsNoTracking()
                    .Where(x => x.RunId == runId && x.Point == PointCode.P3)
                    .Where(x => x.TsUtc >= p3StartUtc.Value && x.TsUtc <= p3EndUtc.Value)
                    .OrderBy(x => x.TsUtc)
                    .Select(x => new P3EventRow
                    {
                        Id = x.Id,
                        W = x.WidthMm,
                        L = x.LengthMm,
                        H = x.HeightMm,
                        Wt = x.EstimatedWeightG,
                        VolMm3 = x.VolumeMm3
                    })
                    .ToListAsync();
            }
            else
            {
                p3Events = new List<P3EventRow>();
            }

            // Seed is stored in P3Json to keep stable across refreshes and for reporting.
            var seed = existing is not null ? TryReadSeed(existing.P3Json) : null;
            var p3Seed = seed ?? RandomNumberGenerator.GetInt32(1, int.MaxValue);

            var sums = new (decimal w, decimal l, decimal h, decimal wt, decimal volMm3, int c)[positions + 1];
            foreach (var e in p3Events)
            {
                var pos = StableRandomPos(e.Id, p3Seed, positions);
                sums[pos].w += e.W;
                sums[pos].l += e.L;
                sums[pos].h += e.H;
                sums[pos].wt += e.Wt;
                sums[pos].volMm3 += e.VolMm3;
                sums[pos].c += 1;
            }

            // Overall means as fallback for empty positions.
            var overallW = Mean(p3Events.Select(x => x.W));
            var overallL = Mean(p3Events.Select(x => x.L));
            var overallH = Mean(p3Events.Select(x => x.H));
            var overallWt = Mean(p3Events.Select(x => x.Wt));
            var overallVolL = Mean(p3Events.Select(x => x.VolMm3)) / 1_000_000m;

            var p3PerPos = Enumerable.Range(1, positions)
                .Select(pos =>
                {
                    var c = sums[pos].c;
                    if (c <= 0)
                    {
                        return new
                        {
                            pos,
                            widthMm = Math.Round(overallW, 2),
                            lengthMm = Math.Round(overallL, 2),
                            heightMm = Math.Round(overallH, 2),
                            weightG = Math.Round(overallWt, 2),
                            volumeL = Math.Round(overallVolL, 4),
                            count = 0
                        };
                    }

                    return new
                    {
                        pos,
                        widthMm = Math.Round(sums[pos].w / c, 2),
                        lengthMm = Math.Round(sums[pos].l / c, 2),
                        heightMm = Math.Round(sums[pos].h / c, 2),
                        weightG = Math.Round(sums[pos].wt / c, 2),
                        volumeL = Math.Round((sums[pos].volMm3 / c) / 1_000_000m, 4),
                        count = c
                    };
                })
                .ToList();

            var p1Json = JsonSerializer.Serialize(new
            {
                bucketStartUtc = bStart,
                bucketEndUtc = bEnd,
                positions,
                perPos = p1PerPos,
                pieceStart = p1StartPiece,
                pieceEnd = p1EndPiece,
                pieceCount = p1Count
            }, _json);

            var p2Json = JsonSerializer.Serialize(new
            {
                positions,
                perPos = p2PerPos,
                pieceCount = p2Count,
                p2FirstUtc,
                p2LastUtc
            }, _json);

            var visJson = JsonSerializer.Serialize(new
            {
                pieceStart = p1StartPiece,
                pieceEnd = p1EndPiece,
                visFirstUtc,
                visLastUtc,
                totalDefects,
                pareto
            }, _json);

            var p3Json = JsonSerializer.Serialize(new
            {
                // P3 window is derived from VIS for this bucket
                p3WindowStartUtc = p3StartUtc,
                p3WindowEndUtc = p3EndUtc,
                positions,
                seed = p3Seed,
                perPos = p3PerPos,
                sourceCount = p3Events.Count
            }, _json);

            // Mark final only when run is closed and this bucket should have fully materialized.
            var isFinal = false;
            if (run.EndUtc is not null)
            {
                // If we have a VIS-derived P3 window, require that to be <= run.EndUtc; else bucket end <= run.EndUtc.
                var needsUntil = p3EndUtc ?? bEnd;
                isFinal = needsUntil <= run.EndUtc.Value;
            }

            if (existing is null)
            {
                existing = new AnalyticsBucket20
                {
                    RunId = runId,
                    BucketStartUtc = bStart,
                    BucketEndUtc = bEnd,
                    Positions = positions,
                    P1StartPieceSeq = p1StartPiece,
                    P1EndPieceSeq = p1EndPiece,
                    P1PieceCount = p1Count,
                    P2PieceCount = p2Count,
                    IsFinal = isFinal,
                    P1Json = p1Json,
                    P2Json = p2Json,
                    VisParetoJson = visJson,
                    P3Json = p3Json,
                    CreatedBy = "system",
                    Source = "analytics"
                };
                _db.AnalyticsBuckets20.Add(existing);
            }
            else
            {
                existing.BucketEndUtc = bEnd;
                existing.Positions = positions;
                existing.P1StartPieceSeq = p1StartPiece;
                existing.P1EndPieceSeq = p1EndPiece;
                existing.P1PieceCount = p1Count;
                existing.P2PieceCount = p2Count;
                existing.IsFinal = isFinal;
                existing.P1Json = p1Json;
                existing.P2Json = p2Json;
                existing.VisParetoJson = visJson;
                existing.P3Json = p3Json;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                existing.UpdatedBy = "system";
                existing.Source = "analytics";
            }

            await _db.SaveChangesAsync();
        }

        // Return all persisted buckets for this run (ordered).
        return await _db.AnalyticsBuckets20.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.BucketStartUtc)
            .ToListAsync();
    }

    public async Task<List<object>> GetLastBuckets20(Guid runId, int maxBuckets = 12)
    {
        maxBuckets = Math.Clamp(maxBuckets, 1, 200);
        await EnsureBuckets20(runId, 20);

        var buckets = await _db.AnalyticsBuckets20.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.BucketStartUtc)
            .Take(maxBuckets)
            .ToListAsync();

        // Return parsed json for convenience.
        var list = new List<object>();
        foreach (var b in buckets.OrderByDescending(x => x.BucketStartUtc))
        {
            var p1 = JsonSerializer.Deserialize<JsonElement>(b.P1Json, _json);
            var p2 = JsonSerializer.Deserialize<JsonElement>(b.P2Json, _json);
            var vis = JsonSerializer.Deserialize<JsonElement>(b.VisParetoJson, _json);
            var p3 = JsonSerializer.Deserialize<JsonElement>(b.P3Json, _json);
            list.Add(new
            {
                b.Id,
                b.BucketStartUtc,
                b.BucketEndUtc,
                b.Positions,
                b.P1StartPieceSeq,
                b.P1EndPieceSeq,
                b.P1PieceCount,
                b.P2PieceCount,
                b.IsFinal,
                p1,
                p2,
                vis,
                p3
            });
        }

        return list;
    }

    public async Task<List<object>> GetAllBuckets20(Guid runId)
    {
        await EnsureBuckets20(runId, 20);
        var buckets = await _db.AnalyticsBuckets20.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.BucketStartUtc)
            .ToListAsync();

        var list = new List<object>();
        foreach (var b in buckets)
        {
            var p1 = JsonSerializer.Deserialize<JsonElement>(b.P1Json, _json);
            var p2 = JsonSerializer.Deserialize<JsonElement>(b.P2Json, _json);
            var vis = JsonSerializer.Deserialize<JsonElement>(b.VisParetoJson, _json);
            var p3 = JsonSerializer.Deserialize<JsonElement>(b.P3Json, _json);
            list.Add(new
            {
                b.Id,
                b.BucketStartUtc,
                b.BucketEndUtc,
                b.Positions,
                b.P1StartPieceSeq,
                b.P1EndPieceSeq,
                b.P1PieceCount,
                b.P2PieceCount,
                b.IsFinal,
                p1,
                p2,
                vis,
                p3
            });
        }

        return list;
    }
}
