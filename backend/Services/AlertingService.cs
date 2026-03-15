using System.Text.Json;
using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Generates persistent alerts (severity/ACK/snooze) based on available data.
/// Notes:
/// - VIS only sends defect events (no total count). We estimate defect rate using P2 expected units and travel-time alignment.
/// - P1/P2 are row-aligned; for drift we use time-window means (not per-row) to keep it robust.
/// </summary>
public class AlertingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertingService> _logger;

    public AlertingService(IServiceScopeFactory scopeFactory, ILogger<AlertingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to allow app startup
        await Task.Delay(1500, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Re-activate expired snoozes
                var now = DateTime.UtcNow;
                var expiredSnoozes = await db.Alerts
                    .Where(a => a.Status == AlertStatus.Snoozed && a.SnoozedUntilUtc != null && a.SnoozedUntilUtc <= now)
                    .ToListAsync(stoppingToken);
                foreach (var a in expiredSnoozes)
                {
                    a.Status = AlertStatus.Active;
                    a.SnoozedUntilUtc = null;
                    a.UpdatedAtUtc = now;
                    a.UpdatedBy = "system";
                }
                if (expiredSnoozes.Count > 0) await db.SaveChangesAsync(stoppingToken);

                // Monitor active runs
                var runs = await db.Runs.AsNoTracking()
                    .Where(r => r.Status == RunStatus.Running || r.Status == RunStatus.Draining)
                    .OrderByDescending(r => r.StartUtc)
                    .Take(3)
                    .ToListAsync(stoppingToken);

                foreach (var run in runs)
                {
                    await GenerateForRun(db, run.Id, stoppingToken);
                }

                // Auto-close stale alerts (best-effort)
                await AutoCloseStale(db, stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alerting loop error");
            }

            await Task.Delay(10_000, stoppingToken);
        }
    }

    private static DateTime FloorTo(DateTime utc, int minutes)
    {
        var m = (utc.Minute / minutes) * minutes;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, m, 0, DateTimeKind.Utc);
    }

    private async Task GenerateForRun(AppDbContext db, Guid runId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // 1) Mandatory prompts become alerts
        var openPrompts = await db.OperatorPrompts.AsNoTracking()
            .Where(p => p.RunId == runId && p.Status == PromptStatus.Open)
            .OrderByDescending(p => p.TriggeredAtUtc)
            .ToListAsync(ct);

        foreach (var p in openPrompts)
        {
            if (p.Type == "DOWNTIME_REQUIRED")
            {
                await UpsertAlert(db, new Alert
                {
                    RunId = runId,
                    Type = "DOWNTIME",
                    Severity = AlertSeverity.Critical,
                    Title = "Downtime reason required",
                    Message = "Line has been stopped > 60s. Operator must select a downtime reason.",
                    TriggeredAtUtc = p.TriggeredAtUtc,
                    DedupeKey = $"{runId}|DOWNTIME|{p.Id}",
                    MetadataJson = JsonSerializer.Serialize(new { promptId = p.Id, promptType = p.Type })
                }, ct);
            }
            if (p.Type == "CHANGEOVER_QUESTION")
            {
                await UpsertAlert(db, new Alert
                {
                    RunId = runId,
                    Type = "CHANGEOVER",
                    Severity = AlertSeverity.Warning,
                    Title = "Possible changeover",
                    Message = "No P1 pieces for 60s. Confirm if production ended or product changed.",
                    TriggeredAtUtc = p.TriggeredAtUtc,
                    DedupeKey = $"{runId}|CHANGEOVER|{p.Id}",
                    MetadataJson = JsonSerializer.Serialize(new { promptId = p.Id, promptType = p.Type })
                }, ct);
            }
        }

        // 2) Spike detection (10-min buckets)
        // Use last full 10-min bucket to avoid partial noise.
        var bucketEnd = FloorTo(now, 10);
        var bucketStart = bucketEnd.AddMinutes(-10);

        if (bucketEnd <= bucketStart) return;

        // Expected units from P2
        var expectedP2 = await db.MeasurementEvents.AsNoTracking()
            .Where(e => e.RunId == runId && e.Point == PointCode.P2 && e.TsUtc >= bucketStart && e.TsUtc < bucketEnd)
            .CountAsync(ct);

        if (expectedP2 >= 30) // avoid tiny-sample noise
        {
            var travelSec = await ComputeTravelSecondsP2ToVis(db, runId, bucketStart, bucketEnd, ct);
            var visFrom = bucketStart.AddSeconds(travelSec);
            var visTo = bucketEnd.AddSeconds(travelSec);

            var visCounts = await db.VisualDefectEvents.AsNoTracking()
                .Where(v => v.RunId == runId && v.TsUtc >= visFrom && v.TsUtc < visTo)
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Defects = g.Sum(x => x.IsDefect ? 1 : 0) })
                .FirstOrDefaultAsync(ct);

            var visTotal = visCounts?.Total ?? 0;
            var defects = visCounts?.Defects ?? 0;

            // Prefer true VIS totals; fallback to expected from P2 when VIS totals are missing/too small.
            var denom = visTotal >= 30 ? visTotal : expectedP2;
            var rateEstimated = visTotal < 30;
            var defectRate = denom > 0 ? ((decimal)defects / (decimal)denom) : 0m;

            // Baseline over last 60 minutes (6 buckets)
            var rates = new List<decimal>();
            for (int i = 1; i <= 6; i++)
            {
                var s = bucketStart.AddMinutes(-10 * i);
                var e = bucketEnd.AddMinutes(-10 * i);
                var exp = await db.MeasurementEvents.AsNoTracking()
                    .Where(x => x.RunId == runId && x.Point == PointCode.P2 && x.TsUtc >= s && x.TsUtc < e)
                    .CountAsync(ct);
                if (exp < 30) continue;
                var tr = await ComputeTravelSecondsP2ToVis(db, runId, s, e, ct);
                var df = s.AddSeconds(tr);
                var dt = e.AddSeconds(tr);
                var vc = await db.VisualDefectEvents.AsNoTracking()
                    .Where(x => x.RunId == runId && x.TsUtc >= df && x.TsUtc < dt)
                    .GroupBy(_ => 1)
                    .Select(g => new { Total = g.Count(), Defects = g.Sum(z => z.IsDefect ? 1 : 0) })
                    .FirstOrDefaultAsync(ct);

                var vTot = vc?.Total ?? 0;
                var dcnt = vc?.Defects ?? 0;
                var denom2 = vTot >= 30 ? vTot : exp;
                if (denom2 > 0) rates.Add((decimal)dcnt / (decimal)denom2);
            }

            var (mean, std) = MeanStd(rates);
            var threshold = std > 0 ? (mean + 2m * std) : (mean + 0.02m);

            if (defectRate >= threshold && defectRate >= 0.03m)
            {
                var sev = defectRate >= 0.08m ? AlertSeverity.Critical : AlertSeverity.Warning;
                await UpsertAlert(db, new Alert
                {
                    RunId = runId,
                    Type = "DEFECT_SPIKE",
                    Severity = sev,
                    Title = "Defect spike (VIS)",
                    Message = rateEstimated
                        ? $"Defect rate {Math.Round(defectRate * 100m, 1)}% in last 10 minutes (estimated; VIS total count not available, used P2 expected + travel time)."
                        : $"Defect rate {Math.Round(defectRate * 100m, 1)}% in last 10 minutes (measured; VIS counts all pieces).",
                    TriggeredAtUtc = bucketStart,
                    DedupeKey = $"{runId}|DEFECT_SPIKE|{bucketStart:yyyyMMddHHmm}",
                    MetadataJson = JsonSerializer.Serialize(new { bucketStartUtc = bucketStart, bucketEndUtc = bucketEnd, defectRate = defectRate, defects, visTotal, expected = expectedP2, rateEstimated, mean, std, threshold })
                }, ct);
            }

            // Drift (P2 growth delta on height)
            var p1h = await MeanHeight(db, runId, PointCode.P1, bucketStart, bucketEnd, ct);
            var p2h = await MeanHeight(db, runId, PointCode.P2, bucketStart, bucketEnd, ct);
            var delta = p2h - p1h;

            var deltas = new List<decimal>();
            for (int i = 1; i <= 6; i++)
            {
                var s = bucketStart.AddMinutes(-10 * i);
                var e = bucketEnd.AddMinutes(-10 * i);
                var a = await MeanHeight(db, runId, PointCode.P1, s, e, ct);
                var b = await MeanHeight(db, runId, PointCode.P2, s, e, ct);
                // keep only meaningful buckets
                if (a == 0m && b == 0m) continue;
                deltas.Add(b - a);
            }
            var (m2, s2) = MeanStd(deltas);
            var driftThr = s2 > 0 ? 2m * s2 : 5m;
            if (Math.Abs(delta - m2) >= driftThr && Math.Abs(delta) > 0.001m)
            {
                await UpsertAlert(db, new Alert
                {
                    RunId = runId,
                    Type = "P2_GROWTH_DRIFT",
                    Severity = AlertSeverity.Warning,
                    Title = "P2 growth drift",
                    Message = $"P2−P1 height delta is {Math.Round(delta, 1)}mm in last 10 minutes (baseline {Math.Round(m2, 1)}±{Math.Round(s2, 1)}).",
                    TriggeredAtUtc = bucketStart,
                    DedupeKey = $"{runId}|P2_GROWTH_DRIFT|{bucketStart:yyyyMMddHHmm}",
                    MetadataJson = JsonSerializer.Serialize(new { bucketStartUtc = bucketStart, bucketEndUtc = bucketEnd, deltaHeightMm = delta, mean = m2, std = s2 })
                }, ct);
            }
        }

        // 3) Mix scrap event
        var scrap = await db.BatchWasteEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.WasteType == "MIX_SCRAP" && x.TsUtc >= bucketStart && x.TsUtc < now)
            .OrderByDescending(x => x.TsUtc)
            .FirstOrDefaultAsync(ct);
        if (scrap != null)
        {
            await UpsertAlert(db, new Alert
            {
                RunId = runId,
                Type = "MIX_SCRAP",
                Severity = AlertSeverity.Warning,
                Title = "Mix discarded",
                Message = $"Mix scrap recorded: {Math.Round(scrap.AmountKg, 2)} kg (≈ {Math.Round(scrap.EquivalentUnits, 1)} units).",
                TriggeredAtUtc = scrap.TsUtc,
                DedupeKey = $"{runId}|MIX_SCRAP|{scrap.Id}",
                MetadataJson = JsonSerializer.Serialize(new { batchId = scrap.BatchId, amountKg = scrap.AmountKg, equivalentUnits = scrap.EquivalentUnits, valueLoss = scrap.ValueLoss })
            }, ct);
        }
    }

    private static async Task<decimal> MeanHeight(AppDbContext db, Guid runId, PointCode point, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var vals = await db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Point == point && x.TsUtc >= fromUtc && x.TsUtc < toUtc)
            .Select(x => x.HeightMm)
            .ToListAsync(ct);
        return vals.Count > 0 ? vals.Average() : 0m;
    }

    private static (decimal mean, decimal std) MeanStd(List<decimal> xs)
    {
        if (xs.Count == 0) return (0m, 0m);
        var mean = xs.Average();
        if (xs.Count == 1) return (mean, 0m);
        var variance = xs.Select(x => (double)(x - mean) * (double)(x - mean)).Average();
        return (mean, (decimal)Math.Sqrt(variance));
    }

    private static async Task<int> ComputeTravelSecondsP2ToVis(AppDbContext db, Guid runId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Best-effort travel based on seg2+seg3 lengths and avg encoder speeds.
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return 45;

        var segments = await db.ProductSegments.AsNoTracking()
            .Where(s => s.ProductId == run.ProductId && (s.SegmentId == 2 || s.SegmentId == 3))
            .ToDictionaryAsync(x => x.SegmentId, ct);

        var enc = await db.EncoderEvents.AsNoTracking()
            .Where(e => e.RunId == runId && e.TsUtc >= fromUtc && e.TsUtc <= toUtc && (e.SegmentId == 2 || e.SegmentId == 3))
            .Select(e => new { e.SegmentId, e.SpeedMps, e.IsStopped })
            .ToListAsync(ct);

        decimal AvgSpeed(int segId)
        {
            var vals = enc.Where(x => x.SegmentId == segId && x.SpeedMps > 0.05m && !x.IsStopped).Select(x => x.SpeedMps).ToList();
            if (vals.Count > 0) return vals.Average();
            if (segments.TryGetValue(segId, out var seg) && seg.TargetSpeedMps > 0.05m) return seg.TargetSpeedMps;
            return 0.6m;
        }

        decimal t = 0m;
        if (segments.TryGetValue(2, out var s2)) t += s2.LengthM / AvgSpeed(2);
        if (segments.TryGetValue(3, out var s3)) t += s3.LengthM / AvgSpeed(3);
        return (int)Math.Round(t);
    }

    private static async Task UpsertAlert(AppDbContext db, Alert candidate, CancellationToken ct)
    {
        // If same dedupe already exists (active/acked/snoozed), do not create a duplicate.
        if (!string.IsNullOrWhiteSpace(candidate.DedupeKey))
        {
            var existing = await db.Alerts
                .Where(a => a.DedupeKey == candidate.DedupeKey && a.Status != AlertStatus.Closed)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // If snoozed but severity escalates, keep snooze but update message/metadata
                existing.Title = candidate.Title;
                existing.Message = candidate.Message;
                existing.Severity = candidate.Severity;
                existing.MetadataJson = candidate.MetadataJson;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                existing.UpdatedBy = "system";
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        candidate.CreatedBy = "system";
        candidate.Source = "system";
        db.Alerts.Add(candidate);
        await db.SaveChangesAsync(ct);
    }

    private static async Task AutoCloseStale(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stale = await db.Alerts
            .Where(a => a.Status != AlertStatus.Closed && a.TriggeredAtUtc < now.AddMinutes(-45))
            .ToListAsync(ct);

        foreach (var a in stale)
        {
            // Keep ACKed alerts visible longer; still close after 45m to avoid clutter.
            a.Status = AlertStatus.Closed;
            a.ClosedAtUtc = now;
            a.UpdatedAtUtc = now;
            a.UpdatedBy = "system";
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
