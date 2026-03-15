using Bakery.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Lightweight, idempotent schema upgrades for demo deployments (no EF migrations in this project).
/// Safe to run on every startup.
/// </summary>
public class SchemaEnsureService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchemaEnsureService> _log;

    public SchemaEnsureService(IServiceScopeFactory scopeFactory, ILogger<SchemaEnsureService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // VisualDefectEvents: strict count alignment
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS \"VisualDefectEvents\" ADD COLUMN IF NOT EXISTS \"PieceSeqIndex\" integer NOT NULL DEFAULT 0;",
                cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS \"IX_VisualDefectEvents_Run_Piece\" ON \"VisualDefectEvents\" (\"RunId\", \"PieceSeqIndex\");",
                cancellationToken);

            // Products: analytics travel-time configuration (VIS -> P3)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS \"Products\" ADD COLUMN IF NOT EXISTS \"VisToP3Minutes\" integer NULL;",
                cancellationToken);

            // AnalyticsBuckets20 persisted rollups
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""AnalyticsBuckets20"" (
  ""Id"" uuid NOT NULL,
  ""RunId"" uuid NOT NULL,
  ""BucketStartUtc"" timestamptz NOT NULL,
  ""BucketEndUtc"" timestamptz NOT NULL,
  ""Positions"" integer NOT NULL,
  ""P1StartPieceSeq"" integer NOT NULL,
  ""P1EndPieceSeq"" integer NOT NULL,
  ""P1PieceCount"" integer NOT NULL,
  ""P2PieceCount"" integer NOT NULL,
  ""IsFinal"" boolean NOT NULL,
  ""P1Json"" jsonb NOT NULL,
  ""P2Json"" jsonb NOT NULL,
  ""VisParetoJson"" jsonb NOT NULL,
  ""P3Json"" jsonb NOT NULL,
  ""CreatedAtUtc"" timestamptz NOT NULL,
  ""CreatedBy"" text NULL,
  ""UpdatedAtUtc"" timestamptz NULL,
  ""UpdatedBy"" text NULL,
  ""Source"" text NOT NULL,
  ""DataStamp"" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""UX_AnalyticsBuckets20_Run_Bucket"" ON ""AnalyticsBuckets20"" (""RunId"", ""BucketStartUtc"");
CREATE INDEX IF NOT EXISTS ""IX_AnalyticsBuckets20_Run"" ON ""AnalyticsBuckets20"" (""RunId"");
CREATE INDEX IF NOT EXISTS ""IX_AnalyticsBuckets20_Bucket"" ON ""AnalyticsBuckets20"" (""BucketStartUtc"");
", cancellationToken);

            _log.LogInformation("[SchemaEnsure] OK");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[SchemaEnsure] failed (continuing)");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
