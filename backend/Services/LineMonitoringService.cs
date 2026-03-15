using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Creates mandatory operator prompts (downtime reasons, changeover questions, belt-empty reasons)
/// based on events already ingested. No PLC assumptions; uses encoder + P1 events.
/// </summary>
public class LineMonitoringService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LineMonitoringService> _logger;

    public LineMonitoringService(IServiceScopeFactory scopeFactory, ILogger<LineMonitoringService> logger)
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
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LineMonitoringService tick failed");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task Tick(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.Runs.AsNoTracking().Where(x => x.Status == RunStatus.Running).OrderByDescending(x => x.StartUtc).FirstOrDefaultAsync(ct);
        if (run is null) return;

        var now = DateTime.UtcNow;
        var threshold = 60;

        // Determine line state from encoder events.
        // NOTE: we must detect >60s stops, so we cannot rely on a 10s window.
        var lookback = now.AddMinutes(-30);
        var enc = await db.EncoderEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.TsUtc >= lookback)
            .OrderByDescending(x => x.TsUtc)
            .Take(5000)
            .Select(x => new { x.TsUtc, x.SegmentId, x.SpeedMps, x.IsStopped })
            .ToListAsync(ct);

        var lastEnc = enc.Take(100).ToList();
        var isMoving = lastEnc.Any(e => e.SpeedMps > 0.05m && !e.IsStopped);
        var isStopped = lastEnc.Count > 0 && !isMoving;

        // Last P1 activity
        var p1Last = await db.MeasurementEvents.AsNoTracking()
            .Where(x => x.RunId == run.Id && x.Point == PointCode.P1)
            .OrderByDescending(x => x.TsUtc)
            .Select(x => (DateTime?)x.TsUtc)
            .FirstOrDefaultAsync(ct);

        // ---- DOWNTIME prompt
        if (isStopped)
        {
            // Best-effort: stopStartedAt = last time ANY segment was moving (speed>0.05) before now.
            // If we have no moving event in lookback, fall back to run start.
            var lastMoving = enc.FirstOrDefault(e => e.SpeedMps > 0.05m && !e.IsStopped);
            var stopStartedAt = lastMoving?.TsUtc ?? run.StartUtc;
            var durSec = (now - stopStartedAt).TotalSeconds;
            if (durSec >= threshold)
            {
                var payload = $"{{\"stopStartedAtUtc\":\"{stopStartedAt:O}\",\"durationSec\":{(int)durSec}}}";
                await EnsurePrompt(db, run.Id, "DOWNTIME_REQUIRED", stopStartedAt, threshold, payload: payload, ct);
            }
        }

        // ---- Changeover question on P1 gap (frontier)
        if (p1Last.HasValue)
        {
            var gapSec = (now - p1Last.Value).TotalSeconds;
            if (gapSec >= threshold)
            {
                // Only ask changeover if we are NOT currently forcing downtime reason.
                var hasDowntimePrompt = await db.OperatorPrompts.AsNoTracking().AnyAsync(x => x.RunId == run.Id && x.Type == "DOWNTIME_REQUIRED" && x.Status == PromptStatus.Open, ct);
                if (!hasDowntimePrompt)
                {
                    await EnsurePrompt(db, run.Id, "CHANGEOVER_QUESTION", p1Last.Value, threshold,
                        payload: $"{{\"lastP1Utc\":\"{p1Last.Value:O}\"}}", ct);
                }
            }
        }

        // ---- Belt-empty reason: conveyor moving but no P1 pieces for >60s and no changeover confirmed
        if (isMoving && p1Last.HasValue)
        {
            var gapSec = (now - p1Last.Value).TotalSeconds;
            if (gapSec >= threshold)
            {
                var hasChangeoverPrompt = await db.OperatorPrompts.AsNoTracking().AnyAsync(x => x.RunId == run.Id && x.Type == "CHANGEOVER_QUESTION" && x.Status == PromptStatus.Open, ct);
                if (!hasChangeoverPrompt)
                {
                    await EnsurePrompt(db, run.Id, "BELT_EMPTY_REQUIRED", p1Last.Value, threshold,
                        payload: $"{{\"lastP1Utc\":\"{p1Last.Value:O}\",\"moving\":true}}", ct);
                }
            }
        }
    }

    private static async Task EnsurePrompt(AppDbContext db, Guid runId, string type, DateTime triggeredAtUtc, int thresholdSec, string payload, CancellationToken ct)
    {
        var exists = await db.OperatorPrompts.AnyAsync(x => x.RunId == runId && x.Type == type && x.Status == PromptStatus.Open, ct);
        if (exists) return;

        db.OperatorPrompts.Add(new OperatorPrompt
        {
            RunId = runId,
            Type = type,
            Status = PromptStatus.Open,
            TriggeredAtUtc = triggeredAtUtc,
            ThresholdSec = thresholdSec,
            PayloadJson = payload,
            CreatedBy = "system",
            Source = "system"
        });
        await db.SaveChangesAsync(ct);
    }
}
