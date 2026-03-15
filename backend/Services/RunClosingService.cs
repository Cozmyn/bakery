using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Ensures that when production ends, the run is not immediately closed.
/// Instead it enters DRaining state and keeps counting downstream events
/// until the last piece clears the line (or a safe max drain window).
/// </summary>
public class RunClosingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunClosingService> _logger;

    public RunClosingService(IServiceScopeFactory scopeFactory, ILogger<RunClosingService> logger)
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
                _logger.LogWarning(ex, "RunClosingService tick failed");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task Tick(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var draining = await db.Runs
            .Where(x => x.Status == RunStatus.Draining && x.EndUtc == null)
            .OrderBy(x => x.ProductionEndUtc)
            .Take(10)
            .ToListAsync(ct);

        if (draining.Count == 0) return;

        const int idleSec = 300; // if nothing happens for this long, assume line cleared

        foreach (var run in draining)
        {
            run.ProductionEndUtc ??= now;

            // last activity across points
            var lastMeas = await db.MeasurementEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderByDescending(x => x.TsUtc)
                .Select(x => (DateTime?)x.TsUtc)
                .FirstOrDefaultAsync(ct);

            var lastVis = await db.VisualDefectEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderByDescending(x => x.TsUtc)
                .Select(x => (DateTime?)x.TsUtc)
                .FirstOrDefaultAsync(ct);

            var lastEnc = await db.EncoderEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderByDescending(x => x.TsUtc)
                .Select(x => (DateTime?)x.TsUtc)
                .FirstOrDefaultAsync(ct);

            var lastOp = await db.OperatorEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderByDescending(x => x.TsUtc)
                .Select(x => (DateTime?)x.TsUtc)
                .FirstOrDefaultAsync(ct);

            var last = new[] { lastMeas, lastVis, lastEnc, lastOp, run.ProductionEndUtc }
                .Where(x => x.HasValue)
                .Max()!.Value;

            var forcedClose = (now - run.ProductionEndUtc.Value).TotalSeconds >= run.WipWindowSec;
            var idle = (now - last).TotalSeconds >= idleSec;

            if (forcedClose || idle)
            {
                run.Status = RunStatus.Closed;
                run.EndUtc = last;
                run.UpdatedAtUtc = now;
                run.UpdatedBy = "system";
                run.Source = "system";
                run.DataStamp = Guid.NewGuid().ToString("N");

                db.OperatorEvents.Add(new OperatorEvent
                {
                    RunId = run.Id,
                    TsUtc = now,
                    Type = "RUN_CLOSED_AUTO",
                    ReasonCode = forcedClose ? "WIP_WINDOW_TIMEOUT" : "IDLE",
                    Comment = $"Closed at {last:O}",
                    CreatedBy = "system",
                    Source = "system"
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
