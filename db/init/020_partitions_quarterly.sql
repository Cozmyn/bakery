-- Calendar-quarter partition creation for event tables.
-- Creates default partition + previous quarter + next 12 quarters (3 years).

CREATE OR REPLACE FUNCTION quarter_start(d date)
RETURNS date
LANGUAGE sql
IMMUTABLE
AS $$
  SELECT date_trunc('quarter', d::timestamp)::date;
$$;

CREATE OR REPLACE FUNCTION ensure_quarter_partition(parent_table text, q_start date)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
  q_end date := (q_start + interval '3 months')::date;
  yr int := EXTRACT(YEAR FROM q_start);
  q  int := EXTRACT(QUARTER FROM q_start);
  part_name text := format('%s_y%sq%s', parent_table, yr, q);
BEGIN
  EXECUTE format(
    'CREATE TABLE IF NOT EXISTS %I PARTITION OF %I FOR VALUES FROM (%L) TO (%L);',
    part_name, parent_table, q_start::text, q_end::text
  );
END;
$$;

CREATE OR REPLACE FUNCTION ensure_quarter_partitions(parent_table text, base_q date, quarters_ahead int)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
  i int;
  q_start date := quarter_start(base_q);
BEGIN
  -- default partition as safety net
  EXECUTE format('CREATE TABLE IF NOT EXISTS %I PARTITION OF %I DEFAULT;', parent_table || '_default', parent_table);

  FOR i IN 0..quarters_ahead LOOP
    PERFORM ensure_quarter_partition(parent_table, (q_start + (i * interval '3 months'))::date);
  END LOOP;
END;
$$;

DO $$
DECLARE
  base_q date := (quarter_start(current_date) - interval '3 months')::date;
BEGIN
  PERFORM ensure_quarter_partitions('MeasurementEvents',   base_q, 12);
  PERFORM ensure_quarter_partitions('VisualDefectEvents', base_q, 12);
  PERFORM ensure_quarter_partitions('EncoderEvents',       base_q, 12);
  PERFORM ensure_quarter_partitions('OperatorEvents',      base_q, 12);
END $$;
