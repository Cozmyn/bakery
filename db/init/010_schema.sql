-- Core schema (fresh DB). Designed for PostgreSQL 16.
-- IMPORTANT: We create the heavy event tables as PARTITIONED by TsUtc (calendar-quarter partitions are created in 020_*).

-- USERS
CREATE TABLE IF NOT EXISTS "Users" (
  "Id" uuid PRIMARY KEY,
  "Email" varchar(200) NOT NULL,
  "PasswordHash" text NOT NULL,
  "Role" integer NOT NULL,
  "IsActive" boolean NOT NULL DEFAULT true,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");


-- AUDIT LOGS (compliance, immutable)
CREATE TABLE IF NOT EXISTS "AuditLogs" (
  "Id" uuid PRIMARY KEY,
  "TsUtc" timestamptz NOT NULL,
  "Method" varchar(16) NOT NULL,
  "Path" varchar(300) NOT NULL,
  "Action" varchar(600) NOT NULL,
  "UserEmail" varchar(200) NULL,
  "UserRole" varchar(30) NULL,
  "EntityType" varchar(80) NULL,
  "EntityId" varchar(80) NULL,
  "IpAddress" varchar(64) NULL,
  "StatusCode" integer NOT NULL,
  "DetailJson" text NULL
);
CREATE INDEX IF NOT EXISTS "IX_AuditLogs_TsUtc" ON "AuditLogs" ("TsUtc");
CREATE INDEX IF NOT EXISTS "IX_AuditLogs_UserEmail_TsUtc" ON "AuditLogs" ("UserEmail","TsUtc");
CREATE INDEX IF NOT EXISTS "IX_AuditLogs_EntityType_TsUtc" ON "AuditLogs" ("EntityType","TsUtc");

-- MASTER: DOWNTIME REASONS
CREATE TABLE IF NOT EXISTS "DowntimeReasons" (
  "Id" uuid PRIMARY KEY,
  "Code" varchar(40) NOT NULL,
  "Label" varchar(120) NOT NULL,
  "Category" varchar(60) NOT NULL,
  "IsOneTap" boolean NOT NULL DEFAULT true,
  "SortOrder" integer NOT NULL DEFAULT 100,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_DowntimeReasons_Code" ON "DowntimeReasons" ("Code");
CREATE INDEX IF NOT EXISTS "IX_DowntimeReasons_SortOrder" ON "DowntimeReasons" ("SortOrder");

-- MASTER: DEFECT TYPES
CREATE TABLE IF NOT EXISTS "DefectTypes" (
  "Id" uuid PRIMARY KEY,
  "Code" varchar(40) NOT NULL,
  "Label" varchar(120) NOT NULL,
  "Category" varchar(40) NOT NULL,
  "SortOrder" integer NOT NULL DEFAULT 100,
  "IsActive" boolean NOT NULL DEFAULT true,
  "SeverityDefault" integer NOT NULL DEFAULT 2,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_DefectTypes_Code" ON "DefectTypes" ("Code");
CREATE INDEX IF NOT EXISTS "IX_DefectTypes_SortOrder" ON "DefectTypes" ("SortOrder");

-- MASTER: INGREDIENTS
CREATE TABLE IF NOT EXISTS "Ingredients" (
  "Id" uuid PRIMARY KEY,
  "ItemNumber" integer NOT NULL,
  "Code" varchar(40) NOT NULL,
  "Name" varchar(200) NOT NULL,
  "DefaultUnit" varchar(16) NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Ingredients_Code" ON "Ingredients" ("Code");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Ingredients_ItemNumber" ON "Ingredients" ("ItemNumber");

-- MASTER: PRODUCTS
CREATE TABLE IF NOT EXISTS "Products" (
  "Id" uuid PRIMARY KEY,
  "Code" varchar(50) NOT NULL,
  "Name" varchar(200) NOT NULL,
  "IsActive" boolean NOT NULL DEFAULT true,
  "PublishedAtUtc" timestamptz NULL,
  "CostPerUnit" numeric NULL,
  "ValuePerUnit" numeric NULL,
  "CostPerHour" numeric NULL,
  "IdealCycleTimeSec" integer NULL,
  "TargetSpeedDefaultMps" numeric NULL,
  "ProofingMinMinutes" integer NULL,
  "ProofingMaxMinutes" integer NULL,
  "NominalUnitWeightG_P3" numeric NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Products_Code" ON "Products" ("Code");

-- PRODUCTS: DENSITY DEFAULTS (1:1)
CREATE TABLE IF NOT EXISTS "ProductDensityDefaults" (
  "ProductId" uuid PRIMARY KEY,
  "DensityP1_GPerCm3" numeric NOT NULL,
  "DensityP2_GPerCm3" numeric NOT NULL,
  "DensityP3_GPerCm3" numeric NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_ProductDensityDefaults_Products" FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE CASCADE
);

-- PRODUCTS: TOLERANCES
CREATE TABLE IF NOT EXISTS "ProductTolerances" (
  "Id" uuid PRIMARY KEY,
  "ProductId" uuid NOT NULL,
  "Point" integer NOT NULL,
  "WidthMinMm" numeric NULL,
  "WidthMaxMm" numeric NULL,
  "LengthMinMm" numeric NULL,
  "LengthMaxMm" numeric NULL,
  "HeightMinMm" numeric NULL,
  "HeightMaxMm" numeric NULL,
  "VolumeMinMm3" numeric NULL,
  "VolumeMaxMm3" numeric NULL,
  "WeightMinG" numeric NULL,
  "WeightMaxG" numeric NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_ProductTolerances_Products" FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_ProductTolerances_Product_Point" ON "ProductTolerances" ("ProductId", "Point");

-- PRODUCTS: SEGMENTS
CREATE TABLE IF NOT EXISTS "ProductSegments" (
  "Id" uuid PRIMARY KEY,
  "ProductId" uuid NOT NULL,
  "SegmentId" integer NOT NULL,
  "LengthM" numeric NOT NULL,
  "TargetSpeedMps" numeric NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_ProductSegments_Products" FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductSegments_Product_Segment" ON "ProductSegments" ("ProductId", "SegmentId");

-- PRODUCTS: RECIPES (versioned)
CREATE TABLE IF NOT EXISTS "ProductRecipes" (
  "Id" uuid PRIMARY KEY,
  "ProductId" uuid NOT NULL,
  "Version" integer NOT NULL DEFAULT 1,
  "IsCurrent" boolean NOT NULL DEFAULT true,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_ProductRecipes_Products" FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_ProductRecipes_Product_Version" ON "ProductRecipes" ("ProductId", "Version");

-- RECIPES: INGREDIENT LINES
CREATE TABLE IF NOT EXISTS "RecipeIngredients" (
  "Id" uuid PRIMARY KEY,
  "RecipeId" uuid NOT NULL,
  "IngredientId" uuid NOT NULL,
  "Quantity" numeric NOT NULL,
  "Unit" varchar(16) NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_RecipeIngredients_Recipes" FOREIGN KEY ("RecipeId") REFERENCES "ProductRecipes"("Id") ON DELETE CASCADE,
  CONSTRAINT "FK_RecipeIngredients_Ingredients" FOREIGN KEY ("IngredientId") REFERENCES "Ingredients"("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_RecipeIngredients_Recipe_Ingredient" ON "RecipeIngredients" ("RecipeId", "IngredientId");

-- RUNS
CREATE TABLE IF NOT EXISTS "Runs" (
  "Id" uuid PRIMARY KEY,
  "ProductId" uuid NOT NULL,
  "StartUtc" timestamptz NOT NULL,
  "ProductionEndUtc" timestamptz NULL,
  "EndUtc" timestamptz NULL,
  "Status" integer NOT NULL,
  "WipWindowSec" integer NOT NULL DEFAULT 900,
  "GreyZoneStartUtc" timestamptz NULL,
  "GreyZoneEndUtc" timestamptz NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_Runs_Products" FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_Runs_ProductId" ON "Runs" ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_Runs_StartUtc" ON "Runs" ("StartUtc");

-- BATCHES
CREATE TABLE IF NOT EXISTS "Batches" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NOT NULL,
  "BatchNumber" integer NOT NULL,
  "MixedAtUtc" timestamptz NULL,
  "AddedToLineAtUtc" timestamptz NULL,
  "Status" integer NOT NULL,
  "Disposition" integer NOT NULL,
  "DiscardedAtUtc" timestamptz NULL,
  "DiscardAmountKg" numeric NULL,
  "DiscardReasonCode" text NULL,
  "DiscardComment" text NULL,
  "ProofingActualMinutes" integer NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_Batches_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Batches_Run_BatchNumber" ON "Batches" ("RunId", "BatchNumber");

-- BATCH MIX SHEET (operator editable)
CREATE TABLE IF NOT EXISTS "BatchRecipeIngredients" (
  "Id" uuid PRIMARY KEY,
  "BatchId" uuid NOT NULL,
  "IngredientId" uuid NOT NULL,
  "Quantity" numeric NOT NULL,
  "Unit" varchar(16) NOT NULL,
  "IsAdded" boolean NOT NULL DEFAULT false,
  "IsRemoved" boolean NOT NULL DEFAULT false,
  "ReasonCode" text NULL,
  "Comment" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_BatchRecipeIngredients_Batches" FOREIGN KEY ("BatchId") REFERENCES "Batches"("Id") ON DELETE CASCADE,
  CONSTRAINT "FK_BatchRecipeIngredients_Ingredients" FOREIGN KEY ("IngredientId") REFERENCES "Ingredients"("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_BatchRecipeIngredients_Batch_Ingredient" ON "BatchRecipeIngredients" ("BatchId", "IngredientId");

-- RUN OVERRIDES (densities)
CREATE TABLE IF NOT EXISTS "RunOverrides" (
  "RunId" uuid PRIMARY KEY,
  "DensityP1_GPerCm3" numeric NULL,
  "DensityP2_GPerCm3" numeric NULL,
  "DensityP3_GPerCm3" numeric NULL,
  "ReasonCode" text NULL,
  "Comment" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_RunOverrides_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE
);

-- WEIGHT SAMPLES
CREATE TABLE IF NOT EXISTS "WeightSamples" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NOT NULL,
  "Point" integer NOT NULL,
  "SampledAtUtc" timestamptz NOT NULL,
  "PiecesCount" integer NOT NULL,
  "WeightsGJson" text NOT NULL,
  "ComputedKFactor" numeric NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_WeightSamples_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_WeightSamples_Run_Point" ON "WeightSamples" ("RunId", "Point");

-- BATCH WASTE EVENTS (mix scrap etc.)
CREATE TABLE IF NOT EXISTS "BatchWasteEvents" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NOT NULL,
  "BatchId" uuid NOT NULL,
  "TsUtc" timestamptz NOT NULL,
  "WasteType" varchar(50) NOT NULL,
  "AmountKg" numeric NOT NULL,
  "EquivalentUnits" numeric NOT NULL,
  "ValueLoss" numeric NOT NULL,
  "ReasonCode" text NULL,
  "Comment" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_BatchWasteEvents_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE,
  CONSTRAINT "FK_BatchWasteEvents_Batches" FOREIGN KEY ("BatchId") REFERENCES "Batches"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_BatchWasteEvents_Run_Ts" ON "BatchWasteEvents" ("RunId", "TsUtc");

-- OPERATOR PROMPTS
CREATE TABLE IF NOT EXISTS "OperatorPrompts" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NOT NULL,
  "Type" varchar(60) NOT NULL,
  "Status" integer NOT NULL,
  "TriggeredAtUtc" timestamptz NOT NULL,
  "ThresholdSec" integer NOT NULL DEFAULT 60,
  "PayloadJson" text NOT NULL DEFAULT '{}',
  "ResolvedAtUtc" timestamptz NULL,
  "ResolvedBy" text NULL,
  "ResolutionCode" text NULL,
  "ReasonCode" text NULL,
  "Comment" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_OperatorPrompts_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_OperatorPrompts_Run_Type_Status" ON "OperatorPrompts" ("RunId", "Type", "Status");

-- PIECE LINKS
CREATE TABLE IF NOT EXISTS "PieceLinks" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NOT NULL,
  "FromPoint" varchar(8) NOT NULL,
  "ToPoint" varchar(8) NOT NULL,
  "FromPieceUid" varchar(120) NOT NULL,
  "ToPieceUid" varchar(120) NOT NULL,
  "Confidence" numeric NOT NULL,
  "LinkedAtUtc" timestamptz NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_PieceLinks_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PieceLinks_Run_From_To" ON "PieceLinks" ("RunId", "FromPieceUid", "ToPieceUid");

-- ALERTS (persistent)
CREATE TABLE IF NOT EXISTS "Alerts" (
  "Id" uuid PRIMARY KEY,
  "RunId" uuid NULL,
  "Type" varchar(60) NOT NULL,
  "Title" varchar(120) NOT NULL,
  "Message" varchar(600) NOT NULL,
  "Severity" integer NOT NULL,
  "Status" integer NOT NULL,
  "TriggeredAtUtc" timestamptz NOT NULL,
  "SnoozedUntilUtc" timestamptz NULL,
  "AcknowledgedByEmail" varchar(200) NULL,
  "AcknowledgedAtUtc" timestamptz NULL,
  "ClosedAtUtc" timestamptz NULL,
  "DedupeKey" varchar(160) NULL,
  "MetadataJson" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL,
  CONSTRAINT "FK_Alerts_Runs" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS "IX_Alerts_RunId_Type_Status" ON "Alerts" ("RunId", "Type", "Status");
CREATE INDEX IF NOT EXISTS "IX_Alerts_DedupeKey" ON "Alerts" ("DedupeKey");

-- =====================
-- EVENT TABLES (PARTITIONED BY TsUtc)
-- NOTE: We do NOT create PK/UNIQUE constraints on Id alone because Postgres requires partition key to be part of a UNIQUE constraint.
-- Id is a UUID, so collisions are practically impossible.

CREATE TABLE IF NOT EXISTS "MeasurementEvents" (
  "Id" uuid NOT NULL,
  "RunId" uuid NOT NULL,
  "Point" integer NOT NULL,
  "TsUtc" timestamptz NOT NULL,
  "CohortId" varchar(32) NULL,
  "RowIndex" integer NULL,
  "PosInRow" integer NULL,
  "PieceSeqIndex" integer NOT NULL,
  "WidthMm" numeric NOT NULL,
  "LengthMm" numeric NOT NULL,
  "HeightMm" numeric NOT NULL,
  "VolumeMm3" numeric NOT NULL,
  "EstimatedWeightG" numeric NOT NULL,
  "WeightConfidence" integer NOT NULL,
  "SourceDeviceId" varchar(50) NOT NULL,
  "PieceUid" varchar(120) NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
) PARTITION BY RANGE ("TsUtc");

CREATE TABLE IF NOT EXISTS "VisualDefectEvents" (
  "Id" uuid NOT NULL,
  "RunId" uuid NOT NULL,
  "TsUtc" timestamptz NOT NULL,
  "IsDefect" boolean NOT NULL,
  "DefectType" varchar(80) NOT NULL,
  "Confidence" numeric NOT NULL,
  "ImageTokenId" text NULL,
  "CohortHintId" text NULL,
  "PieceSeqIndex" integer NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
) PARTITION BY RANGE ("TsUtc");

CREATE TABLE IF NOT EXISTS "EncoderEvents" (
  "Id" uuid NOT NULL,
  "RunId" uuid NOT NULL,
  "SegmentId" integer NOT NULL,
  "TsUtc" timestamptz NOT NULL,
  "SpeedMps" numeric NOT NULL,
  "IsStopped" boolean NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
) PARTITION BY RANGE ("TsUtc");

CREATE TABLE IF NOT EXISTS "OperatorEvents" (
  "Id" uuid NOT NULL,
  "RunId" uuid NOT NULL,
  "TsUtc" timestamptz NOT NULL,
  "Type" varchar(60) NOT NULL,
  "ReasonCode" varchar(60) NULL,
  "Comment" text NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
) PARTITION BY RANGE ("TsUtc");

-- Persisted analytics rollups (20-minute buckets) for reporting
CREATE TABLE IF NOT EXISTS "AnalyticsBuckets20" (
  "Id" uuid NOT NULL,
  "RunId" uuid NOT NULL,
  "BucketStartUtc" timestamptz NOT NULL,
  "BucketEndUtc" timestamptz NOT NULL,
  "Positions" integer NOT NULL,
  "P1StartPieceSeq" integer NOT NULL,
  "P1EndPieceSeq" integer NOT NULL,
  "P1PieceCount" integer NOT NULL,
  "P2PieceCount" integer NOT NULL,
  "IsFinal" boolean NOT NULL,
  "P1Json" jsonb NOT NULL,
  "P2Json" jsonb NOT NULL,
  "VisParetoJson" jsonb NOT NULL,
  "P3Json" jsonb NOT NULL,
  "CreatedAtUtc" timestamptz NOT NULL,
  "CreatedBy" text NULL,
  "UpdatedAtUtc" timestamptz NULL,
  "UpdatedBy" text NULL,
  "Source" text NOT NULL,
  "DataStamp" text NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AnalyticsBuckets20_Run_Bucket" ON "AnalyticsBuckets20" ("RunId", "BucketStartUtc");
CREATE INDEX IF NOT EXISTS "IX_AnalyticsBuckets20_Run" ON "AnalyticsBuckets20" ("RunId");
CREATE INDEX IF NOT EXISTS "IX_AnalyticsBuckets20_Bucket" ON "AnalyticsBuckets20" ("BucketStartUtc");

-- Helpful indexes on parent (non-unique); per-partition indexes created in 030_*.
CREATE INDEX IF NOT EXISTS "IX_MeasurementEvents_Run_Point_Ts" ON "MeasurementEvents" ("RunId", "Point", "TsUtc");
CREATE INDEX IF NOT EXISTS "IX_MeasurementEvents_Ts" ON "MeasurementEvents" ("TsUtc");
CREATE INDEX IF NOT EXISTS "IX_MeasurementEvents_PieceUid" ON "MeasurementEvents" ("PieceUid");

CREATE INDEX IF NOT EXISTS "IX_VisualDefectEvents_Run_Ts" ON "VisualDefectEvents" ("RunId", "TsUtc");
CREATE INDEX IF NOT EXISTS "IX_VisualDefectEvents_Defect_Ts" ON "VisualDefectEvents" ("DefectType", "TsUtc");
CREATE INDEX IF NOT EXISTS "IX_VisualDefectEvents_Run_Piece" ON "VisualDefectEvents" ("RunId", "PieceSeqIndex");

CREATE INDEX IF NOT EXISTS "IX_EncoderEvents_Run_Segment_Ts" ON "EncoderEvents" ("RunId", "SegmentId", "TsUtc");
CREATE INDEX IF NOT EXISTS "IX_EncoderEvents_Segment_Ts" ON "EncoderEvents" ("SegmentId", "TsUtc");

CREATE INDEX IF NOT EXISTS "IX_OperatorEvents_Run_Ts" ON "OperatorEvents" ("RunId", "TsUtc");
CREATE INDEX IF NOT EXISTS "IX_OperatorEvents_Type_Ts" ON "OperatorEvents" ("Type", "TsUtc");
