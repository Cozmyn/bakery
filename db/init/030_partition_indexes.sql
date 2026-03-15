-- Ensure indexes exist on each existing partition (quarterly).
-- We keep this idempotent.

DO $$
DECLARE
  r record;
BEGIN
  -- MeasurementEvents partitions
  FOR r IN SELECT inhrelid::regclass AS child FROM pg_inherits WHERE inhparent = '"MeasurementEvents"'::regclass
  LOOP
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("RunId", "Point", "TsUtc");',
      replace(r.child::text, '"', '') || '_run_point_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("TsUtc");',
      replace(r.child::text, '"', '') || '_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("PieceUid");',
      replace(r.child::text, '"', '') || '_pieceuid_idx', r.child);
  END LOOP;

  -- VisualDefectEvents partitions
  FOR r IN SELECT inhrelid::regclass AS child FROM pg_inherits WHERE inhparent = '"VisualDefectEvents"'::regclass
  LOOP
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("RunId", "TsUtc");',
      replace(r.child::text, '"', '') || '_run_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("RunId", "PieceSeqIndex");',
      replace(r.child::text, '"', '') || '_run_piece_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("DefectType", "TsUtc");',
      replace(r.child::text, '"', '') || '_defect_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("TsUtc");',
      replace(r.child::text, '"', '') || '_ts_idx', r.child);
  END LOOP;

  -- EncoderEvents partitions
  FOR r IN SELECT inhrelid::regclass AS child FROM pg_inherits WHERE inhparent = '"EncoderEvents"'::regclass
  LOOP
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("RunId", "SegmentId", "TsUtc");',
      replace(r.child::text, '"', '') || '_run_seg_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("SegmentId", "TsUtc");',
      replace(r.child::text, '"', '') || '_seg_ts_idx', r.child);
  END LOOP;

  -- OperatorEvents partitions
  FOR r IN SELECT inhrelid::regclass AS child FROM pg_inherits WHERE inhparent = '"OperatorEvents"'::regclass
  LOOP
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("RunId", "TsUtc");',
      replace(r.child::text, '"', '') || '_run_ts_idx', r.child);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %s ("Type", "TsUtc");',
      replace(r.child::text, '"', '') || '_type_ts_idx', r.child);
  END LOOP;
END $$;
