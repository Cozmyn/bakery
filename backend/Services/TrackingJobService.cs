using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Minimal tracking implementation for ETAPA 3:
/// - P1↔P2 per piece (row/pos) with high confidence
/// - CohortId filled for measurement events (minute bucket)
/// P2↔VIS and VIS↔P3 remain cohort-first with confidence MED/LOW in later stages.
/// </summary>
public class TrackingJobService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrackingJobService> _logger;

    public TrackingJobService(IServiceScopeFactory scopeFactory, ILogger<TrackingJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Tick(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TrackingJobService tick failed");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task Tick(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.Runs.AsNoTracking()
            .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
            .OrderBy(x => x.Status == RunStatus.Running ? 0 : 1)
            .ThenByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync(ct);
        if (run is null) return;

        var since = DateTime.UtcNow.AddMinutes(-15);

        // 1) fill CohortId on recent measurement events (idempotent)
        var recent = await db.MeasurementEvents
            .Where(x => x.RunId == run.Id && x.TsUtc >= since && x.CohortId == null)
            .Take(2000)
            .ToListAsync(ct);
        if (recent.Count > 0)
        {
            foreach (var e in recent)
                e.CohortId = e.TsUtc.ToString("yyyyMMddHHmm");

            await db.SaveChangesAsync(ct);
        }

        // 2) P1↔P2 links by row/pos in last 10 minutes
        var p1 = await db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P1 && x.TsUtc >= since && x.RowIndex != null && x.PosInRow != null)
            .Select(x => new { x.PieceUid, x.RowIndex, x.PosInRow, x.TsUtc })
            .ToListAsync(ct);
        if (p1.Count == 0) return;

        var p2 = await db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P2 && x.TsUtc >= since && x.RowIndex != null && x.PosInRow != null)
            .Select(x => new { x.PieceUid, x.RowIndex, x.PosInRow, x.TsUtc })
            .ToListAsync(ct);
        if (p2.Count == 0) return;

        // Build lookup for P2 by row/pos
        var p2Lookup = p2.GroupBy(x => (x.RowIndex!.Value, x.PosInRow!.Value))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(z => z.TsUtc).First());

        // Existing links in window (avoid N+1)
        var existingPairs = await db.PieceLinks.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.LinkedAtUtc >= since)
            .Select(x => new { x.FromPieceUid, x.ToPieceUid })
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingPairs.Select(x => x.FromPieceUid + "|" + x.ToPieceUid));

        // Find missing links
        var candidates = new List<PieceLink>();
        foreach (var a in p1)
        {
            var key = (a.RowIndex!.Value, a.PosInRow!.Value);
            if (!p2Lookup.TryGetValue(key, out var b)) continue;

            if (existingSet.Contains(a.PieceUid + "|" + b.PieceUid)) continue;
            candidates.Add(new PieceLink
            {
                RunId = run.Id,
                FromPoint = "P1",
                ToPoint = "P2",
                FromPieceUid = a.PieceUid,
                ToPieceUid = b.PieceUid,
                Confidence = 0.98m,
                LinkedAtUtc = DateTime.UtcNow,
                CreatedBy = "system",
                Source = "system"
            });
        }

        if (candidates.Count > 0)
        {
            // Bulk insert without duplicates; ignore if racing
            db.PieceLinks.AddRange(candidates.Take(500));
            try { await db.SaveChangesAsync(ct); } catch { /* ignore */ }
        }
    }
}
