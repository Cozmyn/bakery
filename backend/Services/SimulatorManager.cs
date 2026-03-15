using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

public class SimulatorManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVisImageCache _imgCache;
    private readonly IConfiguration _cfg;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    public bool IsRunning { get; private set; }
    public Guid? CurrentRunId { get; private set; }

    public SimulatorManager(IServiceScopeFactory scopeFactory, IVisImageCache imgCache, IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _imgCache = imgCache;
        _cfg = cfg;
    }

    public async Task<(bool ok, string message, Guid? runId)> StartAsync(Guid productId, string startedBy)
    {
        lock (_lock)
        {
            if (IsRunning) return (false, "Simulator already running", CurrentRunId);
            IsRunning = true;
            _cts = new CancellationTokenSource();
        }

        // Create run + first batch
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = await db.Products.FirstOrDefaultAsync(x => x.Id == productId);
            if (product is null)
            {
                StopInternal();
                return (false, "Product not found", null);
            }

            var run = new Run
            {
                ProductId = product.Id,
                StartUtc = DateTime.UtcNow,
                Status = RunStatus.Running,
                CreatedBy = startedBy,
                Source = "sim"
            };
            db.Runs.Add(run);
            var batch = new Batch
            {
                RunId = run.Id,
                BatchNumber = 1,
                MixedAtUtc = DateTime.UtcNow.AddMinutes(-50),
                AddedToLineAtUtc = DateTime.UtcNow,
                Status = BatchStatus.OnLine,
                CreatedBy = startedBy,
                Source = "sim"
            };
            batch.ProofingActualMinutes = 50;
            db.Batches.Add(batch);

            await db.SaveChangesAsync();

            CurrentRunId = run.Id;
        }

        _task = Task.Run(() => LoopAsync(_cts!.Token), _cts!.Token);
        return (true, "Simulator started", CurrentRunId);
    }

    public (bool ok, string message) Stop(string stoppedBy)
    {
        lock (_lock)
        {
            if (!IsRunning) return (false, "Simulator not running");
            _cts?.Cancel();
        }
        return (true, "Stopping simulator");
    }

    private void StopInternal()
    {
        lock (_lock)
        {
            IsRunning = false;
            _cts?.Cancel();
            _cts = null;
            _task = null;
            CurrentRunId = null;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var rnd = new Random();
        var ttl = _cfg.GetValue<int>("Vis:ImageTtlSeconds", 120);

        // Pending events so P2/VIS/P3 do NOT appear immediately.
        // This keeps the demo behavior closer to reality (P2 and downstream points arrive later).
        var pendingMeasurements = new List<MeasurementEvent>();
        var pendingVis = new List<VisualDefectEvent>();

        // Load mock images once
        var imageFiles = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "defect_burn.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "defect_shape.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "defect_crack.png")
        };
        var images = imageFiles
            .Where(File.Exists)
            .Select(File.ReadAllBytes)
            .ToArray();

        int rowIndex = 0;
        int pieceSeq = 0;
        DateTime? stopUntilUtc = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (CurrentRunId is null)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var run = await db.Runs.FirstAsync(x => x.Id == CurrentRunId.Value, ct);
                if (run.Status != RunStatus.Running)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                // Drift + stops (basic)
                var now = DateTime.UtcNow;

                // Flush any due scheduled events first.
                // (Note: we intentionally keep this simple and time-based for the demo.)
                if (pendingMeasurements.Count > 0)
                {
                    var due = pendingMeasurements.Where(x => x.TsUtc <= now).ToList();
                    if (due.Count > 0)
                    {
                        db.MeasurementEvents.AddRange(due);
                        pendingMeasurements.RemoveAll(x => x.TsUtc <= now);
                    }
                }

                if (pendingVis.Count > 0)
                {
                    var due = pendingVis.Where(x => x.TsUtc <= now).ToList();
                    if (due.Count > 0)
                    {
                        db.VisualDefectEvents.AddRange(due);
                        pendingVis.RemoveAll(x => x.TsUtc <= now);
                    }
                }

                // Occasionally simulate a stop long enough to require downtime reason (>60s)
                if (stopUntilUtc is null && rnd.NextDouble() < 0.02) // ~2% chance per second
                {
                    stopUntilUtc = now.AddSeconds(75);
                }

                var inStop = stopUntilUtc.HasValue && now < stopUntilUtc.Value;
                if (stopUntilUtc.HasValue && now >= stopUntilUtc.Value)
                    stopUntilUtc = null;

                if (inStop)
                {
                    // Emit stopped encoders, no pieces
                    for (int seg = 1; seg <= 4; seg++)
                    {
                        db.EncoderEvents.Add(new EncoderEvent
                        {
                            RunId = run.Id,
                            SegmentId = seg,
                            TsUtc = now,
                            SpeedMps = 0m,
                            IsStopped = true,
                            CreatedBy = "sim",
                            Source = "sim"
                        });
                    }
                    await db.SaveChangesAsync(ct);
                    await Task.Delay(1000, ct);
                    continue;
                }

                // Generate one row P1/P2
                rowIndex++;
                var piecesInRow = 6; // demo: fixed 6 pieces per row

                // Read segment configuration (distance) once per loop
                var segs = await db.ProductSegments.AsNoTracking()
                    .Where(x => x.ProductId == run.ProductId)
                    .ToListAsync(ct);
                decimal Len(int id) => segs.FirstOrDefault(s => s.SegmentId == id)?.LengthM ?? 10m;

                // Encoder events (simple but consistent): choose speeds now; these drive arrival times
                var segSpeed = new Dictionary<int, decimal>();
                for (int seg = 1; seg <= 4; seg++)
                {
                    var speed = 0.75m + (decimal)(rnd.NextDouble() * 0.2);
                    segSpeed[seg] = speed;
                    db.EncoderEvents.Add(new EncoderEvent
                    {
                        RunId = run.Id,
                        SegmentId = seg,
                        TsUtc = now,
                        SpeedMps = speed,
                        IsStopped = speed < 0.05m,
                        CreatedBy = "sim",
                        Source = "sim"
                    });
                }

                int TravelSec(params int[] segIds)
                {
                    decimal total = 0m;
                    foreach (var sid in segIds)
                    {
                        var v = segSpeed.TryGetValue(sid, out var sp) ? sp : 0.8m;
                        if (v < 0.05m) v = 0.8m;
                        var len = Len(sid);
                        total += len / v;
                    }
                    return (int)Math.Round(total);
                }

                var tsP1 = now;
                var tsP2 = now.AddSeconds(30);
                var tsVisBase = tsP2.AddSeconds(TravelSec(2, 3));

                // Get density defaults (fallback 0.6)
                var product = await db.Products.AsNoTracking().FirstAsync(x => x.Id == run.ProductId, ct);
                var density = await db.ProductDensityDefaults.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == run.ProductId, ct);
                var dP1 = density?.DensityP1_GPerCm3 ?? 0.75m;
                var dP2 = density?.DensityP2_GPerCm3 ?? 0.60m;
                var dP3 = density?.DensityP3_GPerCm3 ?? 0.55m;

                for (int pos = 1; pos <= piecesInRow; pos++)
                {
                    pieceSeq++;
                    // base dimensions
                    var w = 70m + (decimal)(rnd.NextDouble() * 6 - 3);
                    var l = 160m + (decimal)(rnd.NextDouble() * 10 - 5);
                    var h1 = 55m + (decimal)(rnd.NextDouble() * 6 - 3);
                    var vol1 = w * l * h1; // mm^3 approx (box)

                    var ew1 = EstimateWeight(vol1, dP1);

                    var p1 = new MeasurementEvent
                    {
                        RunId = run.Id,
                        Point = PointCode.P1,
                        TsUtc = tsP1,
                        RowIndex = rowIndex,
                        PosInRow = pos,
                        PieceSeqIndex = pieceSeq,
                        WidthMm = w,
                        LengthMm = l,
                        HeightMm = h1,
                        VolumeMm3 = vol1,
                        EstimatedWeightG = ew1,
                        WeightConfidence = WeightConfidence.Low,
                        CohortId = tsP1.ToString("yyyyMMddHHmm"),
                        PieceUid = BuildPieceUid(run.Id, "P1", tsP1, pieceSeq),
                        SourceDeviceId = "sim-P1",
                        CreatedBy = "sim",
                        Source = "sim"
                    };

                    // P2 growth
                    var growth = 1.10m + (decimal)(rnd.NextDouble() * 0.06 - 0.03);
                    var h2 = h1 * growth;
                    var vol2 = w * l * h2;
                    var ew2 = EstimateWeight(vol2, dP2);

                    var p2 = new MeasurementEvent
                    {
                        RunId = run.Id,
                        Point = PointCode.P2,
                        TsUtc = tsP2,
                        RowIndex = rowIndex,
                        PosInRow = pos,
                        PieceSeqIndex = pieceSeq,
                        WidthMm = w,
                        LengthMm = l,
                        HeightMm = h2,
                        VolumeMm3 = vol2,
                        EstimatedWeightG = ew2,
                        WeightConfidence = WeightConfidence.Low,
                        CohortId = tsP2.ToString("yyyyMMddHHmm"),
                        PieceUid = BuildPieceUid(run.Id, "P2", tsP2, pieceSeq),
                        SourceDeviceId = "sim-P2",
                        CreatedBy = "sim",
                        Source = "sim"
                    };

                    // P1 appears immediately; P2 is scheduled.
                    db.MeasurementEvents.Add(p1);
                    pendingMeasurements.Add(p2);

                    // VIS inspection (camera counts every piece). Images are disabled for stability.
                    var isDef = rnd.NextDouble() < 0.03; // demo: ~3% defects
                    var defectType = "OK";
                    string? token = null;

                    if (isDef)
                    {
                        // richer defect taxonomy (codes seeded in DefectTypes)
                        var types = new[]
                        {
                            "BURNED","UNDERBAKED","TOO_DARK","TOO_LIGHT","NO_SCORING","UNEVEN_COLOR",
                            "DEFORMED","CRACKED","TORN","COLLAPSED","BURST","FLAT","STUCK","FOREIGN_OBJECT"
                        };
                        defectType = types[rnd.Next(types.Length)];
                        // Intentionally do not generate ImageTokenId.
                    }

                    var tsVis = tsVisBase.AddMilliseconds(rnd.Next(0, 800));
                    pendingVis.Add(new VisualDefectEvent
                    {
                        RunId = run.Id,
                        TsUtc = tsVis,
                        IsDefect = isDef,
                        DefectType = defectType,
                        Confidence = isDef ? (0.85m + (decimal)rnd.NextDouble() * 0.14m) : (0.97m + (decimal)rnd.NextDouble() * 0.02m),
                        ImageTokenId = token,
                        CohortHintId = tsVis.ToString("yyyyMMddHHmm"),
                        PieceSeqIndex = pieceSeq,
                        CreatedBy = "sim",
                        Source = "sim"
                    });

                    // P3 after freeze (shrink slightly)
                    var shrink = 0.98m + (decimal)(rnd.NextDouble() * 0.02 - 0.01);
                    var h3 = h2 * shrink;
                    var vol3 = w * l * h3;
                    var ew3 = EstimateWeight(vol3, dP3);

                    // P3 is time-based from VIS (VIS->P3 minutes is configured per product).
                    var visToP3Min = Math.Clamp(product.VisToP3Minutes ?? 35, 1, 24 * 60);
                    var tsP3 = tsVis.AddMinutes(visToP3Min);

                    pendingMeasurements.Add(new MeasurementEvent
                    {
                        RunId = run.Id,
                        Point = PointCode.P3,
                        TsUtc = tsP3,
                        RowIndex = null,
                        PosInRow = null,
                        PieceSeqIndex = pieceSeq,
                        WidthMm = w,
                        LengthMm = l,
                        HeightMm = h3,
                        VolumeMm3 = vol3,
                        EstimatedWeightG = ew3,
                        WeightConfidence = WeightConfidence.Low,
                        CohortId = tsP3.ToString("yyyyMMddHHmm"),
                        PieceUid = BuildPieceUid(run.Id, "P3", tsP3, pieceSeq),
                        SourceDeviceId = "sim-P3",
                        CreatedBy = "sim",
                        Source = "sim"
                    });
                }

                await db.SaveChangesAsync(ct);

                await Task.Delay(3000, ct); // demo: one row every 3s
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch
        {
            // swallow; simulator should not crash the process in v1
        }
        finally
        {
            // Close run if any
            if (CurrentRunId is Guid runId)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId);
                    if (run is not null)
                    {
                        run.Status = RunStatus.Stopped;
                        run.EndUtc = DateTime.UtcNow;
                        run.UpdatedAtUtc = DateTime.UtcNow;
                        run.UpdatedBy = "sim";
                        await db.SaveChangesAsync();
                    }
                }
                catch { /* ignore */ }
            }

            StopInternal();
        }
    }

    private static string BuildPieceUid(Guid runId, string point, DateTime tsUtc, int idx)
        => $"{runId:N}:{point}:{tsUtc:O}:{idx}";

    private static decimal EstimateWeight(decimal volumeMm3, decimal densityGPerCm3)
    {
        // volume mm3 -> cm3: divide by 1000
        var volumeCm3 = volumeMm3 / 1000m;
        return Math.Round(volumeCm3 * densityGPerCm3, 1);
    }
}
