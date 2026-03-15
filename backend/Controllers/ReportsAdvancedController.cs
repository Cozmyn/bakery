using System.Globalization;
using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("reports")]
[Authorize(Policy = "OperatorOrAdmin")]
public class ReportsAdvancedController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OeeService _oee;

    public ReportsAdvancedController(AppDbContext db, OeeService oee)
    {
        _db = db;
        _oee = oee;
    }

    private static TimeZoneInfo ResolveTz(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static DateTime BucketStartUtc(DateTime tsUtc, int minutes)
    {
        var m = (tsUtc.Minute / minutes) * minutes;
        return new DateTime(tsUtc.Year, tsUtc.Month, tsUtc.Day, tsUtc.Hour, m, 0, DateTimeKind.Utc);
    }

    private static string IsoWeekKey(DateTime local)
    {
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(local, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{local.Year}-W{week:00}";
    }

    private static (string shiftCode, DateTime shiftStartLocal) ShiftOf(DateTime local)
    {
        // Default plant schedule (configurable later): A 06-14, B 14-22, C 22-06
        var t = local.TimeOfDay;
        if (t >= TimeSpan.FromHours(6) && t < TimeSpan.FromHours(14))
        {
            var start = new DateTime(local.Year, local.Month, local.Day, 6, 0, 0, local.Kind);
            return ("A", start);
        }
        if (t >= TimeSpan.FromHours(14) && t < TimeSpan.FromHours(22))
        {
            var start = new DateTime(local.Year, local.Month, local.Day, 14, 0, 0, local.Kind);
            return ("B", start);
        }
        // Shift C spans midnight
        if (t >= TimeSpan.FromHours(22))
        {
            var start = new DateTime(local.Year, local.Month, local.Day, 22, 0, 0, local.Kind);
            return ("C", start);
        }
        // 00:00-06:00 belongs to previous day's shift C
        var prev = local.AddDays(-1);
        var prevStart = new DateTime(prev.Year, prev.Month, prev.Day, 22, 0, 0, local.Kind);
        return ("C", prevStart);
    }

    [HttpGet("period")]
    public async Task<IActionResult> Period([FromQuery] string group = "day", [FromQuery] int days = 30, [FromQuery] string tz = "Europe/Bucharest")
    {
        // Overlap-aware period report:
        // - Buckets are based on local shift/day/week boundaries.
        // - Metrics are aggregated by event timestamps, not by run start.
        days = Math.Clamp(days, 1, 365);
        group = (group ?? "day").ToLowerInvariant();
        var tzInfo = ResolveTz(tz);

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);

        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tzInfo);
        var toLocal = TimeZoneInfo.ConvertTimeFromUtc(toUtc, tzInfo);

        // Preload defect labels
        var defectMap = await _db.DefectTypes.AsNoTracking().Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, x => new { x.Label, x.Category });

        // Preload relevant runs once
        var runs = await _db.Runs.AsNoTracking().Include(r => r.Product)
            .Where(r => (r.StartUtc <= toUtc) && ((r.EndUtc ?? toUtc) >= fromUtc))
            .OrderBy(r => r.StartUtc)
            .Take(10000)
            .ToListAsync();

        // Build bucket intervals
        var intervals = BuildIntervals(group, fromLocal, toLocal, tzInfo);
        var buckets = new Dictionary<string, BucketAcc>();

        foreach (var it in intervals)
        {
            if (it.EndUtc <= fromUtc || it.StartUtc >= toUtc) continue;

            var key = it.Key;
            if (!buckets.TryGetValue(key, out var acc))
            {
                acc = new BucketAcc
                {
                    Key = it.Key,
                    Label = it.Label,
                    BucketStartLocal = it.StartLocal,
                    BucketStartUtc = it.StartUtc
                };
                buckets[key] = acc;
            }

            // Runs overlapping this bucket
            var overlapRuns = runs.Where(r => r.StartUtc < it.EndUtc && (r.EndUtc ?? toUtc) > it.StartUtc).ToList();
            acc.RunCount += overlapRuns.Count;

            // OEE aggregated (time-weighted by planned production time inside the bucket)
            decimal plannedSecSum = 0m;
            decimal oeeWeightedSum = 0m;

            foreach (var run in overlapRuns)
            {
                var now = DateTime.UtcNow;
                var runPlannedEnd = run.ProductionEndUtc ?? (run.EndUtc ?? now);
                var runCountsEnd = run.EndUtc ?? now;

                var winFrom = Max(it.StartUtc, run.StartUtc);
                var winPlannedEnd = Min(it.EndUtc, runPlannedEnd);
                var winCountsEnd = Min(it.EndUtc, runCountsEnd);

                if (winPlannedEnd <= winFrom) continue;

                var plannedSec = (decimal)(winPlannedEnd - winFrom).TotalSeconds;
                if (plannedSec <= 0) continue;

                var w = await _oee.ComputeWindow(run.Id, winFrom, winPlannedEnd, winCountsEnd);
                plannedSecSum += plannedSec;
                oeeWeightedSum += w.TotalOee * plannedSec;

                acc.GiveawayKg += w.Extras.GiveawayKg;
                acc.MixScrapUnits += w.Extras.PrelineScrapUnits;
            }

            if (plannedSecSum > 0)
                acc.OeeSum += (oeeWeightedSum / plannedSecSum);

            // Defects by type in this bucket (event-time)
            var runIds = overlapRuns.Select(r => r.Id).ToList();
            if (runIds.Count > 0)
            {
                var visTotal = await _db.VisualDefectEvents.AsNoTracking()
                    .Where(x => runIds.Contains(x.RunId) && x.TsUtc >= it.StartUtc && x.TsUtc < it.EndUtc)
                    .CountAsync();
                acc.VisTotal += visTotal;

                var defectCounts = await _db.VisualDefectEvents.AsNoTracking()
                    .Where(x => runIds.Contains(x.RunId) && x.IsDefect && x.TsUtc >= it.StartUtc && x.TsUtc < it.EndUtc)
                    .GroupBy(x => x.DefectType)
                    .Select(g => new { Code = g.Key, Count = g.Count() })
                    .ToListAsync();

                foreach (var d in defectCounts)
                {
                    var code = string.IsNullOrWhiteSpace(d.Code) ? "OTHER" : d.Code!;
                    acc.DefectTotal += d.Count;
                    acc.Defects[code] = acc.Defects.TryGetValue(code, out var c) ? (c + d.Count) : d.Count;
                }

                // Output units (use P3 count in this bucket)
                var p3Count = await _db.MeasurementEvents.AsNoTracking()
                    .Where(x => runIds.Contains(x.RunId) && x.Point == PointCode.P3 && x.TsUtc >= it.StartUtc && x.TsUtc < it.EndUtc)
                    .CountAsync();
                acc.OutputUnits += p3Count;

                // Downtime reasons (resolved prompts) — overlap-aware duration
                var prompts = await _db.OperatorPrompts.AsNoTracking()
                    .Where(x => runIds.Contains(x.RunId) && x.Type == "DOWNTIME_REQUIRED" && x.Status == PromptStatus.Resolved && x.ResolvedAtUtc != null)
                    .Where(x => x.TriggeredAtUtc < it.EndUtc && x.ResolvedAtUtc > it.StartUtc)
                    .Select(x => new { x.ReasonCode, x.TriggeredAtUtc, x.ResolvedAtUtc })
                    .ToListAsync();

                foreach (var p in prompts)
                {
                    var reason = string.IsNullOrWhiteSpace(p.ReasonCode) ? "OTHER" : p.ReasonCode!;
                    var s = Max(p.TriggeredAtUtc, it.StartUtc);
                    var e = Min(p.ResolvedAtUtc!.Value, it.EndUtc);
                    var minutes = (decimal)(e - s).TotalMinutes;
                    if (minutes < 0) minutes = 0;
                    acc.DowntimeMinutes[reason] = acc.DowntimeMinutes.TryGetValue(reason, out var mm) ? (mm + minutes) : minutes;
                }
            }
        }

        object MapDef(string code)
        {
            if (defectMap.TryGetValue(code, out var v)) return new { code, label = v.Label, category = v.Category };
            return new { code, label = code, category = "OTHER" };
        }

        var result = buckets.Values.OrderBy(x => x.BucketStartUtc).Select(b => new
        {
            b.Key,
            b.Label,
            bucketStartUtc = b.BucketStartUtc,
            bucketStartLocal = b.BucketStartLocal,
            runCount = b.RunCount,
            oeeAvg = Math.Round(b.OeeSum, 4),
            defectTotal = b.DefectTotal,
            visTotal = b.VisTotal,
            defectRate = b.VisTotal > 0 ? Math.Round((decimal)b.DefectTotal / b.VisTotal, 4) : (b.OutputUnits > 0 ? Math.Round((decimal)b.DefectTotal / b.OutputUnits, 4) : 0m),
            mixScrapUnits = Math.Round(b.MixScrapUnits, 2),
            giveawayKg = Math.Round(b.GiveawayKg, 3),
            topDowntime = b.DowntimeMinutes.OrderByDescending(x => x.Value).Take(3).Select(x => new { code = x.Key, minutes = Math.Round(x.Value, 2) }).ToList(),
            topDefects = b.Defects.OrderByDescending(x => x.Value).Take(3).Select(x => new { code = x.Key, meta = MapDef(x.Key), count = x.Value }).ToList(),
        });

        return Ok(new { tz, group, fromUtc, toUtc, buckets = result });
    }

    private record Interval(string Key, string Label, DateTime StartUtc, DateTime EndUtc, DateTime StartLocal);

    private static List<Interval> BuildIntervals(string group, DateTime fromLocal, DateTime toLocal, TimeZoneInfo tzInfo)
    {
        var list = new List<Interval>();

        DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(local, tzInfo);

        if (group == "week")
        {
            // Monday 00:00 local
            var start = fromLocal.Date;
	            // DayOfWeek is an enum; explicitly cast for stable math.
	            var dow = start.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)start.DayOfWeek;
            start = start.AddDays(-(dow - 1));
            for (var cur = start; cur <= toLocal; cur = cur.AddDays(7))
            {
                var key = IsoWeekKey(cur);
                var end = cur.AddDays(7);
                list.Add(new Interval(key, key, ToUtc(cur), ToUtc(end), cur));
            }
            return list;
        }

        if (group == "shift")
        {
            // A 06-14, B 14-22, C 22-06
            var startDay = fromLocal.Date.AddDays(-1);
            var endDay = toLocal.Date.AddDays(1);
            var starts = new List<DateTime>();
            for (var d = startDay; d <= endDay; d = d.AddDays(1))
            {
                starts.Add(new DateTime(d.Year, d.Month, d.Day, 6, 0, 0, d.Kind));
                starts.Add(new DateTime(d.Year, d.Month, d.Day, 14, 0, 0, d.Kind));
                starts.Add(new DateTime(d.Year, d.Month, d.Day, 22, 0, 0, d.Kind));
            }
            starts = starts.OrderBy(x => x).ToList();

            foreach (var s in starts)
            {
                var e = s.AddHours(8);
                if (e < fromLocal || s > toLocal) continue;
                var code = s.Hour == 6 ? "A" : (s.Hour == 14 ? "B" : "C");
                var key = $"{s:yyyy-MM-dd} {code}";
                var label = $"{s:yyyy-MM-dd} Shift {code}";
                list.Add(new Interval(key, label, ToUtc(s), ToUtc(e), s));
            }
            return list;
        }

        // day
        for (var cur = fromLocal.Date; cur <= toLocal.Date; cur = cur.AddDays(1))
        {
            var key = cur.ToString("yyyy-MM-dd");
            var end = cur.AddDays(1);
            list.Add(new Interval(key, key, ToUtc(cur), ToUtc(end), cur));
        }
        return list;
    }

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;
[HttpGet("pareto/downtime")]
    public async Task<IActionResult> ParetoDowntime([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);

        var prompts = await _db.OperatorPrompts.AsNoTracking()
            .Where(x => x.Type == "DOWNTIME_REQUIRED" && x.Status == PromptStatus.Resolved && x.ResolvedAtUtc != null && x.TriggeredAtUtc >= fromUtc)
            .Select(x => new { x.RunId, x.ReasonCode, x.TriggeredAtUtc, x.ResolvedAtUtc })
            .ToListAsync();

	        var byReason = new Dictionary<string, (decimal min, decimal value)>();
        foreach (var p in prompts)
        {
            var reason = string.IsNullOrWhiteSpace(p.ReasonCode) ? "OTHER" : p.ReasonCode!;
            var minutes = (decimal)((p.ResolvedAtUtc!.Value - p.TriggeredAtUtc).TotalMinutes);
            if (minutes < 0) minutes = 0;

            // value loss: best-effort using run product cost/hour (availability cost)
            var costPerHour = await _db.Runs.AsNoTracking()
                .Where(r => r.Id == p.RunId)
                .Select(r => r.Product!.CostPerHour)
                .FirstOrDefaultAsync();
            var cph = costPerHour ?? 0m;
            var value = cph > 0 ? (minutes / 60m) * cph : 0m;

	            var cur = byReason.TryGetValue(reason, out var v) ? v : (min: 0m, value: 0m);
	            byReason[reason] = (min: cur.min + minutes, value: cur.value + value);
        }

        var items = byReason.OrderByDescending(x => x.Value.min)
            .Select(x => new { code = x.Key, minutes = Math.Round(x.Value.min, 2), cost = Math.Round(x.Value.value, 2) })
            .ToList();

        return Ok(new { fromUtc, toUtc, items });
    }

    [HttpGet("pareto/defects")]
    public async Task<IActionResult> ParetoDefects([FromQuery] int days = 30, [FromQuery] int bucketMinutes = 10)
    {
        days = Math.Clamp(days, 1, 365);
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);

        var defectMap = await _db.DefectTypes.AsNoTracking().Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, x => new { x.Label, x.Category, x.SeverityDefault });

        // precompute seg3 avg speed per bucket
        var enc = await _db.EncoderEvents.AsNoTracking()
            .Where(x => x.TsUtc >= fromUtc && x.TsUtc <= toUtc && x.SegmentId == 3 && x.SpeedMps > 0.05m && !x.IsStopped)
            .Select(x => new { x.TsUtc, x.SpeedMps })
            .ToListAsync();

        var speedByBucket = enc.GroupBy(e => BucketStartUtc(e.TsUtc, bucketMinutes))
            .ToDictionary(g => g.Key, g => g.Average(x => x.SpeedMps));

        var defects = await _db.VisualDefectEvents.AsNoTracking()
            .Where(x => x.IsDefect && x.TsUtc >= fromUtc && x.TsUtc <= toUtc)
            .Select(x => new { x.TsUtc, x.DefectType })
            .ToListAsync();

	        var acc = new Dictionary<string, (int count, decimal speedSum, int speedN)>();
        foreach (var d in defects)
        {
            var code = string.IsNullOrWhiteSpace(d.DefectType) ? "OTHER" : d.DefectType!;
            var b = BucketStartUtc(d.TsUtc, bucketMinutes);
            var hasSpeed = speedByBucket.TryGetValue(b, out var s);
	            var cur = acc.TryGetValue(code, out var v) ? v : (count: 0, speedSum: 0m, speedN: 0);
	            acc[code] = (
	                count: cur.count + 1,
	                speedSum: cur.speedSum + (hasSpeed ? (decimal)s : 0m),
	                speedN: cur.speedN + (hasSpeed ? 1 : 0)
	            );
        }

        var items = acc.OrderByDescending(x => x.Value.count)
            .Select(x => new
            {
                code = x.Key,
                label = defectMap.TryGetValue(x.Key, out var v) ? v.Label : x.Key,
                category = defectMap.TryGetValue(x.Key, out var v2) ? v2.Category : "OTHER",
                severity = defectMap.TryGetValue(x.Key, out var v3) ? v3.SeverityDefault : 1,
                count = x.Value.count,
                avgSpeedSeg3 = x.Value.speedN > 0 ? Math.Round(x.Value.speedSum / x.Value.speedN, 3) : 0m
            })
            .ToList();

        return Ok(new { fromUtc, toUtc, bucketMinutes, items });
    }

    [HttpGet("run/{runId:guid}/timeline")]
    public async Task<IActionResult> Timeline(Guid runId, [FromQuery] int bucketMinutes = 10)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);

        var run = await _db.Runs.AsNoTracking().Include(r => r.Product).FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return NotFound();

        var plannedEnd = run.ProductionEndUtc ?? (run.EndUtc ?? DateTime.UtcNow);
        var countsEnd = run.EndUtc ?? DateTime.UtcNow;

        DateTime BucketStart(DateTime ts)
        {
            var m = (ts.Minute / bucketMinutes) * bucketMinutes;
            return new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, m, 0, DateTimeKind.Utc);
        }

        // prefetch events
        var enc = await _db.EncoderEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.TsUtc >= run.StartUtc && x.TsUtc <= plannedEnd)
            .OrderBy(x => x.TsUtc)
            .Select(x => new { x.TsUtc, x.SegmentId, x.SpeedMps, x.IsStopped })
            .ToListAsync();

        var p1 = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == PointCode.P1 && x.TsUtc >= run.StartUtc && x.TsUtc <= plannedEnd)
            .Select(x => new { x.TsUtc, x.HeightMm })
            .ToListAsync();

        var p2 = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == PointCode.P2 && x.TsUtc >= run.StartUtc && x.TsUtc <= plannedEnd)
            .Select(x => new { x.TsUtc, x.HeightMm })
            .ToListAsync();

        var vis = await _db.VisualDefectEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.IsDefect && x.TsUtc >= run.StartUtc && x.TsUtc <= countsEnd)
            .Select(x => new { x.TsUtc, x.DefectType })
            .ToListAsync();

        var ops = await _db.OperatorEvents.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.TsUtc)
            .Select(x => new { x.TsUtc, x.Type, x.ReasonCode, x.Comment })
            .ToListAsync();

        var bucketStarts = new List<DateTime>();
        for (var t = BucketStart(run.StartUtc); t <= plannedEnd; t = t.AddMinutes(bucketMinutes))
            bucketStarts.Add(t);

        decimal Mean(IEnumerable<decimal> xs) => xs.Any() ? xs.Average() : 0m;

        // Helper: encoder stop minutes per segment in bucket
        decimal StopMinutesForSeg(int segId, DateTime bStart, DateTime bEnd)
        {
            var list = enc.Where(e => e.SegmentId == segId && e.TsUtc >= bStart && e.TsUtc <= bEnd).ToList();
            if (list.Count == 0) return 0m;
            var lastTs = bStart;
            var lastRunning = true;
            decimal stopSec = 0m;
            foreach (var e in list)
            {
                var dt = (decimal)(e.TsUtc - lastTs).TotalSeconds;
                if (dt > 0 && !lastRunning) stopSec += dt;
                lastRunning = (e.SpeedMps > 0.05m) && !e.IsStopped;
                lastTs = e.TsUtc;
            }
            var tail = (decimal)(bEnd - lastTs).TotalSeconds;
            if (tail > 0 && !lastRunning) stopSec += tail;
            return Math.Round(stopSec / 60m, 2);
        }

        var buckets = new List<object>();
        foreach (var bStart in bucketStarts)
        {
            var bEnd = bStart.AddMinutes(bucketMinutes);
            if (bEnd > plannedEnd) bEnd = plannedEnd;

            var seg3Speed = Mean(enc.Where(e => e.SegmentId == 3 && e.TsUtc >= bStart && e.TsUtc < bEnd && e.SpeedMps > 0.05m && !e.IsStopped).Select(x => x.SpeedMps));
            var defectCount = vis.Count(x => x.TsUtc >= bStart && x.TsUtc < bEnd);

            var p1h = Mean(p1.Where(x => x.TsUtc >= bStart && x.TsUtc < bEnd).Select(x => x.HeightMm));
            var p2h = Mean(p2.Where(x => x.TsUtc >= bStart && x.TsUtc < bEnd).Select(x => x.HeightMm));

            buckets.Add(new
            {
                bucketStartUtc = bStart,
                bucketEndUtc = bEnd,
                seg3AvgSpeed = Math.Round(seg3Speed, 3),
                defects = defectCount,
                p2GrowthDeltaHeightMm = Math.Round(p2h - p1h, 2),
                stopMin = new
                {
                    s1 = StopMinutesForSeg(1, bStart, bEnd),
                    s2 = StopMinutesForSeg(2, bStart, bEnd),
                    s3 = StopMinutesForSeg(3, bStart, bEnd),
                    s4 = StopMinutesForSeg(4, bStart, bEnd)
                }
            });
        }

        return Ok(new
        {
            run = new { run.Id, run.Status, run.StartUtc, run.ProductionEndUtc, run.EndUtc, product = new { run.ProductId, code = run.Product!.Code, name = run.Product.Name } },
            bucketMinutes,
            buckets,
            operatorEvents = ops
        });
    }

    [HttpGet("run/{runId:guid}/insights")]
    public async Task<IActionResult> Insights(Guid runId)
    {
        var run = await _db.Runs.AsNoTracking().Include(r => r.Product).FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return NotFound();

        // Proofing compliance
        var min = run.Product!.ProofingMinMinutes ?? 0;
        var max = run.Product.ProofingMaxMinutes ?? int.MaxValue;

        var batches = await _db.Batches.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.BatchNumber)
            .Select(x => new { x.Id, x.BatchNumber, x.ProofingActualMinutes, x.MixedAtUtc, x.AddedToLineAtUtc })
            .ToListAsync();

        var proofing = batches.Select(b => new
        {
            b.Id,
            b.BatchNumber,
            b.ProofingActualMinutes,
            status = b.ProofingActualMinutes is null ? "UNKNOWN" : (b.ProofingActualMinutes < min ? "TOO_SHORT" : (b.ProofingActualMinutes > max ? "TOO_LONG" : "OK"))
        }).ToList();

        // Recipe variance (standard vs actual) based on current recipe
        var recipe = await _db.ProductRecipes.AsNoTracking().Where(x => x.ProductId == run.ProductId && x.IsCurrent)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();

        var standard = new Dictionary<Guid, (decimal qty, string unit)>();
        if (recipe is not null)
        {
            var std = await _db.RecipeIngredients.AsNoTracking().Where(x => x.RecipeId == recipe.Id)
                .Select(x => new { x.IngredientId, x.Quantity, x.Unit })
                .ToListAsync();
            foreach (var s in std) standard[s.IngredientId] = (s.Quantity, s.Unit);
        }

        var ing = await _db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.Id, x => new { x.ItemNumber, x.Code, x.Name, x.DefaultUnit });

        decimal ToKg(decimal qty, string unit)
        {
            unit = unit.ToLowerInvariant();
            if (unit == "kg") return qty;
            if (unit == "g") return qty / 1000m;
            // Data limitation: for v1 we only support kg/g.
            return qty;
        }

        var variance = new List<object>();
        foreach (var b in batches)
        {
            var actual = await _db.BatchRecipeIngredients.AsNoTracking().Where(x => x.BatchId == b.Id)
	                // Model field is Quantity (operator-edited mix sheet); no QuantityActual exists.
	                .Select(x => new { x.IngredientId, x.Quantity, x.Unit })
                .ToListAsync();

            var totalStdKg = 0m;
            var totalActKg = 0m;

            var lines = new List<object>();
            foreach (var a in actual)
            {
                var stdExists = standard.TryGetValue(a.IngredientId, out var s);
                var stdKg = stdExists ? ToKg(s.qty, s.unit) : 0m;
	                var actKg = ToKg(a.Quantity, a.Unit);
                totalStdKg += stdKg;
                totalActKg += actKg;

                var meta = ing.TryGetValue(a.IngredientId, out var m) ? m : new { ItemNumber = 0, Code = "?", Name = "?", DefaultUnit = (string?)"kg" };
                lines.Add(new
                {
                    ingredient = new { meta.ItemNumber, meta.Code, meta.Name },
                    stdKg = Math.Round(stdKg, 3),
                    actKg = Math.Round(actKg, 3),
                    deltaKg = Math.Round(actKg - stdKg, 3)
                });
            }

            variance.Add(new
            {
                batchId = b.Id,
                b.BatchNumber,
                totalStdKg = Math.Round(totalStdKg, 3),
                totalActKg = Math.Round(totalActKg, 3),
                deltaKg = Math.Round(totalActKg - totalStdKg, 3),
                lines
            });
        }

        // Weight calibration stability
	        var samples = await _db.WeightSamples.AsNoTracking().Where(x => x.RunId == runId)
	            .Select(x => x.ComputedKFactor)
	            .ToListAsync();
	        var kVals = samples.Where(x => x.HasValue).Select(x => x!.Value).ToList();
	        var meanK = kVals.Count > 0 ? kVals.Average() : 1m;
	        var stdK = kVals.Count > 1
	            ? (decimal)Math.Sqrt(kVals.Select(x => Math.Pow((double)(x - meanK), 2)).Average())
	            : 0m;

        // Changeover performance (best-effort)
        var nextRun = await _db.Runs.AsNoTracking()
            .Where(x => x.StartUtc > run.StartUtc)
            .OrderBy(x => x.StartUtc)
            .FirstOrDefaultAsync();

        object? changeover = null;
        if (run.ProductionEndUtc is not null && nextRun is not null)
        {
            changeover = new
            {
                fromRunId = run.Id,
                toRunId = nextRun.Id,
                minutes = Math.Round((decimal)(nextRun.StartUtc - run.ProductionEndUtc.Value).TotalMinutes, 2),
                greyZoneSec = run.WipWindowSec
            };
        }

        return Ok(new
        {
            run = new { run.Id, run.Status, run.StartUtc, run.ProductionEndUtc, run.EndUtc, product = new { run.ProductId, code = run.Product!.Code, name = run.Product.Name } },
            proofing = new { min, max, batches = proofing },
            recipeVariance = variance,
	            weightCalibration = new { sampleCount = samples.Count, nonNullCount = kVals.Count, meanK = Math.Round(meanK, 4), stdK = Math.Round(stdK, 4) },
            changeover
        });
    }

    [HttpGet("changeovers")]
    public async Task<IActionResult> Changeovers([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);

        var runs = await _db.Runs.AsNoTracking()
            .Where(x => x.StartUtc >= fromUtc && x.ProductionEndUtc != null)
            .OrderBy(x => x.StartUtc)
            .ToListAsync();

        var list = new List<object>();
        for (int i = 0; i < runs.Count - 1; i++)
        {
            var a = runs[i];
            var b = runs[i + 1];
            if (a.ProductionEndUtc is null) continue;

            var minutes = (decimal)(b.StartUtc - a.ProductionEndUtc.Value).TotalMinutes;
            if (minutes < 0) continue;

            list.Add(new
            {
                fromRunId = a.Id,
                toRunId = b.Id,
                fromEndUtc = a.ProductionEndUtc,
                toStartUtc = b.StartUtc,
                durationMin = Math.Round(minutes, 2),
                greyZoneSec = a.WipWindowSec
            });
        }

        return Ok(new { fromUtc, toUtc, items = list });
    }

    private class BucketAcc
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public DateTime BucketStartUtc { get; set; }
        public DateTime BucketStartLocal { get; set; }
        public int RunCount { get; set; }
        public decimal OeeSum { get; set; }
        public decimal GiveawayKg { get; set; }
        public decimal MixScrapUnits { get; set; }
        public int DefectTotal { get; set; }
        public int VisTotal { get; set; }
        public int OutputUnits { get; set; }
        public Dictionary<string, int> Defects { get; } = new();
        public Dictionary<string, decimal> DowntimeMinutes { get; } = new();
    }
}
