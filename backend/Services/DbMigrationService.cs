using Bakery.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

/// <summary>
/// Lightweight DB migration runner for this project.
/// - Does not delete data.
/// - Creates missing tables/indexes required by newer versions.
/// - Partitioning: best-effort. If event tables are already partitioned, we ensure future partitions exist.
///   If they are not partitioned (legacy dev DB), we do NOT attempt an in-place conversion.
/// </summary>
public static class DbMigrationService
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        // Ensure core schema exists for dev (only creates schema if empty DB)
        await db.Database.EnsureCreatedAsync();

        // ---- Lightweight idempotent schema extensions (analytics alignment)
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE IF EXISTS \"VisualDefectEvents\" ADD COLUMN IF NOT EXISTS \"PieceSeqIndex\" integer NOT NULL DEFAULT 0;");

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_VisualDefectEvents_Run_Piece\" ON \"VisualDefectEvents\" (\"RunId\", \"PieceSeqIndex\");");

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE IF EXISTS \"Products\" ADD COLUMN IF NOT EXISTS \"VisToP3Minutes\" integer NULL;");

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
");

        // Create Alerts table if missing (ETAPA 6)
        await db.Database.ExecuteSqlRawAsync(@"
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = 'Alerts'
  ) THEN
    CREATE TABLE ""Alerts"" (
      ""Id"" uuid PRIMARY KEY,
      ""RunId"" uuid NULL,
      ""Type"" varchar(60) NOT NULL,
      ""Title"" varchar(120) NOT NULL,
      ""Message"" varchar(600) NOT NULL,
      ""Severity"" integer NOT NULL,
      ""Status"" integer NOT NULL,
      ""TriggeredAtUtc"" timestamptz NOT NULL,
      ""SnoozedUntilUtc"" timestamptz NULL,
      ""AcknowledgedByEmail"" varchar(200) NULL,
      ""AcknowledgedAtUtc"" timestamptz NULL,
      ""ClosedAtUtc"" timestamptz NULL,
      ""DedupeKey"" varchar(160) NULL,
      ""MetadataJson"" text NULL,
      ""CreatedAtUtc"" timestamptz NOT NULL,
      ""CreatedBy"" text NULL,
      ""UpdatedAtUtc"" timestamptz NULL,
      ""UpdatedBy"" text NULL,
      ""Source"" text NOT NULL,
      ""DataStamp"" text NOT NULL
    );
  END IF;
END $$;
");

        
        // Create AuditLogs table if missing (Admin UI: Audit & Compliance)
        await db.Database.ExecuteSqlRawAsync(@"
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = 'AuditLogs'
  ) THEN
    CREATE TABLE ""AuditLogs"" (
      ""Id"" uuid PRIMARY KEY,
      ""TsUtc"" timestamptz NOT NULL,
      ""Method"" varchar(16) NOT NULL,
      ""Path"" varchar(300) NOT NULL,
      ""Action"" varchar(600) NOT NULL,
      ""UserEmail"" varchar(200) NULL,
      ""UserRole"" varchar(30) NULL,
      ""EntityType"" varchar(80) NULL,
      ""EntityId"" varchar(80) NULL,
      ""IpAddress"" varchar(64) NULL,
      ""StatusCode"" integer NOT NULL,
      ""DetailJson"" text NULL
    );
  END IF;
END $$;
");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_TsUtc"" ON ""AuditLogs"" (""TsUtc"");
CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_UserEmail_TsUtc"" ON ""AuditLogs"" (""UserEmail"", ""TsUtc"");
CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_EntityType_TsUtc"" ON ""AuditLogs"" (""EntityType"", ""TsUtc"");
");


// Indexes (IF NOT EXISTS supported)
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS ""IX_Alerts_RunId_Type_Status"" ON ""Alerts"" (""RunId"", ""Type"", ""Status"");
CREATE INDEX IF NOT EXISTS ""IX_Alerts_DedupeKey"" ON ""Alerts"" (""DedupeKey"");
");

        // Partition maintenance (best-effort): create next calendar-quarter partitions only if tables are already partitioned.
        // For industrial deployments, create a fresh DB with partitioned event tables from the start.
        await EnsureQuarterPartitions(db, "MeasurementEvents", "TsUtc");
        await EnsureQuarterPartitions(db, "VisualDefectEvents", "TsUtc");
        await EnsureQuarterPartitions(db, "EncoderEvents", "TsUtc");
        await EnsureQuarterPartitions(db, "OperatorEvents", "TsUtc");
    }

    private static async Task EnsureQuarterPartitions(AppDbContext db, string table, string tsColumn)
    {
        var now = DateTime.UtcNow;
        var qStartMonth = ((now.Month - 1) / 3) * 3 + 1;
        var qStart = new DateTime(now.Year, qStartMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseQ = qStart.AddMonths(-3);

        // One DO block per table to avoid errors on non-partitioned legacy DBs.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DO $$");
        sb.AppendLine("DECLARE part boolean;");
        sb.AppendLine("BEGIN");
        sb.AppendLine($@"  SELECT EXISTS(
    SELECT 1
    FROM pg_partitioned_table pt
    JOIN pg_class c ON c.oid = pt.partrelid
    WHERE c.relname = '{table}'
  ) INTO part;");

        sb.AppendLine("  IF NOT part THEN");
        sb.AppendLine("    RETURN;");
        sb.AppendLine("  END IF;");
        sb.AppendLine("");

        // Ensure default partition exists
        sb.AppendLine($@"  EXECUTE format('CREATE TABLE IF NOT EXISTS %I PARTITION OF %I DEFAULT;',
    '{table}_default', '{table}');");

        // Ensure previous quarter + next 8 quarters (~2 years). Names: <table>_yYYYYqQ
        for (int i = 0; i < 9; i++)
        {
            var a = baseQ.AddMonths(i * 3);
            var b = a.AddMonths(3);
            var q = ((a.Month - 1) / 3) + 1;
            var name = $"{table}_y{a.Year}q{q}";

            sb.AppendLine($@"  EXECUTE format('CREATE TABLE IF NOT EXISTS %I PARTITION OF %I FOR VALUES FROM (TIMESTAMPTZ %L) TO (TIMESTAMPTZ %L);',
    '{name}', '{table}', '{a:yyyy-MM-dd}', '{b:yyyy-MM-dd}');");

            sb.AppendLine($@"  EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %I (%I);',
    'IX_{name}_ts', '{name}', '{tsColumn}');");
        }

        sb.AppendLine("END $$;");

        await db.Database.ExecuteSqlRawAsync(sb.ToString());
    }
}
