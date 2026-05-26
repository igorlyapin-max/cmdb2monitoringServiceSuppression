-- PostgreSQL schema used by monitoring-ui-api conversion-config-store.
-- The service can create these tables automatically, but keeping the DDL here
-- makes DBA review and controlled migrations possible.

CREATE SCHEMA IF NOT EXISTS monitoring_ui;

CREATE TABLE IF NOT EXISTS monitoring_ui.conversion_config_documents (
  version integer PRIMARY KEY,
  etag text NOT NULL,
  prefix text NOT NULL DEFAULT '',
  rule_documents jsonb NOT NULL,
  template_documents jsonb NOT NULL,
  manifest jsonb NOT NULL,
  saved_at timestamptz NOT NULL,
  writer text NOT NULL,
  change_type text NOT NULL DEFAULT '',
  reason text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS monitoring_ui.conversion_config_materialization_jobs (
  job_id text PRIMARY KEY,
  idempotency_key text NOT NULL UNIQUE,
  status text NOT NULL,
  request_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  result_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  attempts integer NOT NULL DEFAULT 0,
  locked_by text NOT NULL DEFAULT '',
  locked_at timestamptz NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS monitoring_ui.conversion_config_materialized_dimensions (
  layer text NOT NULL,
  template_id text NOT NULL,
  dimension_key text NOT NULL,
  dimension_value text NOT NULL DEFAULT '',
  source_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  config_version integer NULL,
  first_seen_at timestamptz NOT NULL DEFAULT now(),
  last_seen_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (layer, template_id, dimension_key)
);

CREATE TABLE IF NOT EXISTS monitoring_ui.conversion_config_locks (
  lock_name text PRIMARY KEY,
  owner text NOT NULL,
  lock_reason text NOT NULL DEFAULT '',
  locked_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NULL
);

CREATE TABLE IF NOT EXISTS monitoring_ui.conversion_config_audit (
  event_id text PRIMARY KEY,
  saved_at timestamptz NOT NULL,
  actor text NOT NULL,
  change_type text NOT NULL,
  reason text NOT NULL DEFAULT '',
  previous_version integer NULL,
  previous_etag text NOT NULL DEFAULT '',
  version integer NULL,
  etag text NOT NULL DEFAULT '',
  storage_folder text NOT NULL DEFAULT '',
  payload jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS conversion_config_audit_saved_at_idx
  ON monitoring_ui.conversion_config_audit (saved_at DESC);

CREATE INDEX IF NOT EXISTS conversion_config_materialization_jobs_status_idx
  ON monitoring_ui.conversion_config_materialization_jobs (status, updated_at DESC);

CREATE INDEX IF NOT EXISTS conversion_config_materialized_dimensions_version_idx
  ON monitoring_ui.conversion_config_materialized_dimensions (config_version);
