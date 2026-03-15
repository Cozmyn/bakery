using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Industrial OEE computation:
/// - Availability: based on encoder stop/run time (per segment)
/// - Performance: ideal units (running time / ideal cycle time) vs actual measured units
/// - Quality: good units vs total units (includes pre-line scrap from discarded mixes)
/// 
/// Notes:
/// - Counts are computed up to countsEndUtc (includes WIP drain), while planned time is capped at plannedEndUtc
///   (production end marker) to avoid penalizing the drain time.
/// - Segment mapping (best-effort):
///   seg1 -> P1 counts + P1 tolerances; includes pre-line mix scrap
///   seg2 -> P2 counts + P2 tolerances
///   seg3 -> VIS defects vs expected (P2 counts)
///   seg4 -> P3 counts + P3 tolerances
/// </summary>
public class OeeService
{
    private readonly AppDbContext _db;
    public OeeService(AppDbContext db) { _db = db; }

    public record Waterfall(decimal AvailabilityLossMin, decimal PerformanceLossUnits, decimal QualityLossUnits);
    public record SegmentCounts(decimal ActualUnits, decimal IdealUnits, decimal GoodUnits, decimal DefectUnits, decimal OotUnits, decimal PrelineScrapUnits);
    public record SegmentMoney(decimal SpeedLossValue, decimal SpeedLossCost, decimal PrelineScrapValue);
    public record SegmentOee(int SegmentId, decimal Availability, decimal Performance, decimal Quality, decimal Oee, Waterfall Waterfall, SegmentCounts Counts, SegmentMoney Monetization);
    public record ProcessLoss(decimal AvgWeightP1_g, decimal AvgWeightP3_g, decimal Delta_g);
    public record Extras(decimal GiveawayKg, decimal PrelineScrapUnits, decimal PrelineScrapValue, ProcessLoss ProcessLoss);
    public record WindowInfo(DateTime FromUtc, DateTime PlannedEndUtc, DateTime CountsEndUtc);
    public record OeeWindowResult(bool Running, Guid RunId, WindowInfo Window, decimal TotalOee, List<SegmentOee> Segments, Extras Extras);
    public record BucketResult(DateTime BucketStartUtc, DateTime BucketEndUtc, List<SegmentOee> Segments);
    public record OeeRunResult(bool Ok, Guid RunId, Guid ProductId, object Times, OeeWindowResult Totals, int BucketMinutes, List<BucketResult> Buckets);

    public async Task<OeeRunResult> ComputeRun(Guid runId, int bucketMinutes)
    {
        var run = await _db.Runs.AsNoTracking().Include(r => r.Product).FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null) return new OeeRunResult(false, Guid.Empty, Guid.Empty, new { }, new OeeWindowResult(false, Guid.Empty, new WindowInfo(DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow), 0m, new(), new Extras(0, 0, 0, new ProcessLoss(0, 0, 0))), bucketMinutes, new());

        var now = DateTime.UtcNow;
        var plannedEnd = run.ProductionEndUtc ?? (run.EndUtc ?? now);
        var countsEnd = run.EndUtc ?? now;

        var totals = await ComputeWindow(runId, run.StartUtc, plannedEnd, countsEnd);
        var buckets = await ComputeBuckets(run, bucketMinutes, plannedEnd, countsEnd);

        var times = new
        {
            startUtc = run.StartUtc,
            productionEndUtc = run.ProductionEndUtc,
            endUtc = run.EndUtc,
            plannedEndUtc = plannedEnd,
            countsEndUtc = countsEnd
        };

        return new OeeRunResult(true, run.Id, run.ProductId, times, totals, bucketMinutes, buckets);
    }

    public async Task<OeeWindowResult> ComputeWindow(Guid runId, DateTime fromUtc, DateTime plannedEndUtc, DateTime countsEndUtc)
    {
        var run = await _db.Runs.AsNoTracking().Include(r => r.Product).FirstOrDefaultAsync(x => x.Id == runId);
        if (run is null)
            return new OeeWindowResult(false, Guid.Empty, new WindowInfo(fromUtc, plannedEndUtc, countsEndUtc), 0m, new(), new Extras(0, 0, 0, new ProcessLoss(0, 0, 0)));

        var product = run.Product;
        var idealCt = Math.Max(0.1m, (decimal)(product?.IdealCycleTimeSec ?? 2));
        var valuePerUnit = product?.ValuePerUnit ?? product?.CostPerUnit ?? 0m;
        var costPerHour = product?.CostPerHour ?? 0m;

        // Tolerances
        var tolerances = await _db.ProductTolerances.AsNoTracking()
            .Where(x => x.ProductId == run.ProductId)
            .ToDictionaryAsync(x => x.Point);

        // Segment config for travel-time alignment (P2 -> VIS)
        var segments = await _db.ProductSegments.AsNoTracking()
            .Where(x => x.ProductId == run.ProductId)
            .ToDictionaryAsync(x => x.SegmentId);

        var encSpeeds23 = await _db.EncoderEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.TsUtc >= fromUtc && x.TsUtc <= plannedEndUtc && (x.SegmentId == 2 || x.SegmentId == 3))
            .Select(x => new { x.SegmentId, x.SpeedMps, x.IsStopped })
            .ToListAsync();

        decimal AvgSpeed(int segId)
        {
            var vals = encSpeeds23.Where(e => e.SegmentId == segId && e.SpeedMps > 0.05m && !e.IsStopped).Select(e => e.SpeedMps).ToList();
            if (vals.Count > 0) return vals.Average();
            if (segments.TryGetValue(segId, out var seg) && seg.TargetSpeedMps > 0.05m) return seg.TargetSpeedMps;
            return 0.6m;
        }

        int TravelSecP2ToVis()
        {
            var t = 0m;
            if (segments.TryGetValue(2, out var s2)) t += s2.LengthM / AvgSpeed(2);
            if (segments.TryGetValue(3, out var s3)) t += s3.LengthM / AvgSpeed(3);
            return (int)Math.Round(t);
        }

        var travelP2VisSec = TravelSecP2ToVis();

        // Pre-line scrap from discarded mixes
        var prelineScrapUnits = await _db.BatchWasteEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.WasteType == "MIX_SCRAP" && x.TsUtc >= fromUtc && x.TsUtc <= countsEndUtc)
            .SumAsync(x => (decimal?)x.EquivalentUnits) ?? 0m;
        var prelineScrapValue = await _db.BatchWasteEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.WasteType == "MIX_SCRAP" && x.TsUtc >= fromUtc && x.TsUtc <= countsEndUtc)
            .SumAsync(x => (decimal?)x.ValueLoss) ?? 0m;

        // Segment computations
        var segs = new List<SegmentOee>();
        decimal totalOeeAvg = 0m;

        for (int segId = 1; segId <= 4; segId++)
        {
            var avail = await Availability(runId, segId, fromUtc, plannedEndUtc);
            var runningSec = avail.runningSec;
            var stopSec = avail.stopSec;

            var idealUnits = runningSec <= 0 ? 0m : (runningSec / idealCt);

            // Mapping counts
            var map = SegmentMap(segId);
            var actualUnits = await CountUnits(runId, map.pointForCount, fromUtc, countsEndUtc);

            var perf = idealUnits <= 0 ? 1m : Clamp01(actualUnits / idealUnits);
            var perfLossUnits = Math.Max(0m, idealUnits - actualUnits);
            var speedLossValue = Math.Round(perfLossUnits * valuePerUnit, 2);
            var speedLossCost = costPerHour > 0 ? Math.Round(((perfLossUnits * idealCt) / 3600m) * costPerHour, 2) : 0m;

            // Quality
            decimal goodUnits;
            decimal totalUnitsForQuality;
            decimal qualityLossUnits;
            decimal defectCount = 0;
            decimal ootCount = 0;

            if (segId == 3)
            {
                // VIS counts (OK + defects) aligned by travel time (seg2+seg3). If OK events are missing, fallback to expected from P2.
                var dFrom = fromUtc.AddSeconds(travelP2VisSec);
                var dTo = countsEndUtc.AddSeconds(travelP2VisSec);

                var visCounts = await _db.VisualDefectEvents.AsNoTracking()
                    .Where(x => x.RunId == runId && x.TsUtc >= dFrom && x.TsUtc <= dTo)
                    .GroupBy(_ => 1)
                    .Select(g => new { Total = g.Count(), Defects = g.Sum(x => x.IsDefect ? 1 : 0) })
                    .FirstOrDefaultAsync();

                var visTotal = visCounts?.Total ?? 0;
                defectCount = visCounts?.Defects ?? 0;
                if (visTotal > 0 && defectCount > visTotal) visTotal = (int)defectCount;

                totalUnitsForQuality = visTotal > 0 ? visTotal : actualUnits;
                goodUnits = Math.Max(0m, totalUnitsForQuality - defectCount);
                qualityLossUnits = Math.Max(0m, defectCount);
            }

            else
            {
                if (!tolerances.TryGetValue(map.pointForQuality, out var tol))
                {
                    tol = null;
                }

                // Good = within tolerance
                goodUnits = await CountGood(runId, map.pointForQuality, tol, fromUtc, countsEndUtc);
                ootCount = Math.Max(0m, actualUnits - goodUnits);

                // Include pre-line scrap in seg1
                if (segId == 1)
                {
                    totalUnitsForQuality = actualUnits + prelineScrapUnits;
                    qualityLossUnits = (actualUnits - goodUnits) + prelineScrapUnits;
                }
                else
                {
                    totalUnitsForQuality = actualUnits;
                    qualityLossUnits = actualUnits - goodUnits;
                }
            }

            var quality = totalUnitsForQuality <= 0 ? 1m : Clamp01(goodUnits / totalUnitsForQuality);
            var availability = (runningSec + stopSec) <= 0 ? 1m : Clamp01(runningSec / (runningSec + stopSec));
            var oee = Math.Round(availability * perf * quality, 4);
            totalOeeAvg += oee;

            var wf = new Waterfall(Math.Round(stopSec / 60m, 2), Math.Round(perfLossUnits, 2), Math.Round(qualityLossUnits, 2));
            var counts = new SegmentCounts(Math.Round(actualUnits, 2), Math.Round(idealUnits, 2), Math.Round(goodUnits, 2), Math.Round(defectCount, 2), Math.Round(ootCount, 2), segId == 1 ? Math.Round(prelineScrapUnits, 2) : 0m);
            var money = new SegmentMoney(speedLossValue, speedLossCost, segId == 1 ? Math.Round(prelineScrapValue, 2) : 0m);
            segs.Add(new SegmentOee(segId, Math.Round(availability, 4), Math.Round(perf, 4), Math.Round(quality, 4), oee, wf, counts, money));
        }

        totalOeeAvg = Math.Round(totalOeeAvg / 4m, 4);

        // Giveaway estimate at P3: grams above upper weight tolerance
        decimal giveawayG = 0m;
        if (tolerances.TryGetValue(PointCode.P3, out var tolP3) && tolP3.WeightMaxG is not null)
        {
            var maxG = tolP3.WeightMaxG.Value;
            giveawayG = await _db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == runId && x.Point == PointCode.P3 && x.TsUtc >= fromUtc && x.TsUtc <= countsEndUtc)
                .Select(x => x.EstimatedWeightG > maxG ? (x.EstimatedWeightG - maxG) : 0m)
                .SumAsync();
        }

        // Process loss (separate KPI): avg weight P1 vs avg weight P3 (estimated)
        var p1Avg = await AvgWeight(runId, PointCode.P1, fromUtc, countsEndUtc);
        var p3Avg = await AvgWeight(runId, PointCode.P3, fromUtc, countsEndUtc);

        var extras = new Extras(
            GiveawayKg: Math.Round(giveawayG / 1000m, 3),
            PrelineScrapUnits: Math.Round(prelineScrapUnits, 2),
            PrelineScrapValue: Math.Round(prelineScrapValue, 2),
            ProcessLoss: new ProcessLoss(Math.Round(p1Avg, 2), Math.Round(p3Avg, 2), Math.Round(p1Avg - p3Avg, 2))
        );

        return new OeeWindowResult(true, runId, new WindowInfo(fromUtc, plannedEndUtc, countsEndUtc), totalOeeAvg, segs, extras);
    }

    private async Task<List<BucketResult>> ComputeBuckets(Run run, int bucketMinutes, DateTime plannedEndUtc, DateTime countsEndUtc)
    {
        static DateTime BucketStart(DateTime ts, int minutes)
        {
            var m = (ts.Minute / minutes) * minutes;
            return new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, m, 0, DateTimeKind.Utc);
        }

        var from = run.StartUtc;
        var to = plannedEndUtc;
        if (to < from) to = from;

        var buckets = new List<BucketResult>();
        for (var t = BucketStart(from, bucketMinutes); t < to.AddSeconds(1); t = t.AddMinutes(bucketMinutes))
        {
            var bStart = t;
            var bEnd = t.AddMinutes(bucketMinutes);
            if (bEnd > to) bEnd = to;
            var w = await ComputeWindow(run.Id, bStart, bEnd, countsEndUtc);
            buckets.Add(new BucketResult(bStart, bEnd, w.Segments));
        }

        return buckets;
    }

    private static (PointCode pointForCount, PointCode pointForQuality) SegmentMap(int segId)
    {
        return segId switch
        {
            1 => (PointCode.P1, PointCode.P1),
            2 => (PointCode.P2, PointCode.P2),
            3 => (PointCode.P2, PointCode.P2),
            4 => (PointCode.P3, PointCode.P3),
            _ => (PointCode.P3, PointCode.P3)
        };
    }

    private async Task<(decimal runningSec, decimal stopSec)> Availability(Guid runId, int segmentId, DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc) return (0m, 0m);

        var enc = await _db.EncoderEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.SegmentId == segmentId && x.TsUtc >= fromUtc && x.TsUtc <= toUtc)
            .OrderBy(x => x.TsUtc)
            .Select(x => new { x.TsUtc, x.SpeedMps, x.IsStopped })
            .ToListAsync();

        // If no encoder data, assume running
        if (enc.Count == 0)
        {
            var sec = (decimal)(toUtc - fromUtc).TotalSeconds;
            return (sec, 0m);
        }

        var lastTs = fromUtc;
        var lastRunning = true;
        decimal runSec = 0m;
        decimal stopSec = 0m;
        foreach (var e in enc)
        {
            var dt = (decimal)(e.TsUtc - lastTs).TotalSeconds;
            if (dt > 0)
            {
                if (lastRunning) runSec += dt; else stopSec += dt;
            }
            lastRunning = (e.SpeedMps > 0.05m) && !e.IsStopped;
            lastTs = e.TsUtc;
        }

        var tail = (decimal)(toUtc - lastTs).TotalSeconds;
        if (tail > 0)
        {
            if (lastRunning) runSec += tail; else stopSec += tail;
        }

        return (runSec, stopSec);
    }

    private async Task<decimal> CountUnits(Guid runId, PointCode point, DateTime fromUtc, DateTime toUtc)
    {
        return await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == point && x.TsUtc >= fromUtc && x.TsUtc <= toUtc)
            .CountAsync();
    }

    private async Task<decimal> CountGood(Guid runId, PointCode point, ProductTolerance? tol, DateTime fromUtc, DateTime toUtc)
    {
        if (tol is null)
            return await CountUnits(runId, point, fromUtc, toUtc);

        // Guard against partially configured tolerances (should not happen for runnable products)
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
            .Where(x => x.RunId == runId && x.Point == point && x.TsUtc >= fromUtc && x.TsUtc <= toUtc)
            .Where(x =>
                x.WidthMm >= wMin && x.WidthMm <= wMax &&
                x.LengthMm >= lMin && x.LengthMm <= lMax &&
                x.HeightMm >= hMin && x.HeightMm <= hMax &&
                x.VolumeMm3 >= vMin && x.VolumeMm3 <= vMax &&
                x.EstimatedWeightG >= wtMin && x.EstimatedWeightG <= wtMax)
            .CountAsync();
    }

    private async Task<decimal> AvgWeight(Guid runId, PointCode point, DateTime fromUtc, DateTime toUtc)
    {
        var weights = await _db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == point && x.TsUtc >= fromUtc && x.TsUtc <= toUtc)
            .Select(x => (decimal?)x.EstimatedWeightG)
            .ToListAsync();
        if (weights.Count == 0) return 0m;
        return weights.Where(x => x.HasValue).Average() ?? 0m;
    }

    private static decimal Clamp01(decimal x) => x < 0m ? 0m : (x > 1m ? 1m : x);
}
