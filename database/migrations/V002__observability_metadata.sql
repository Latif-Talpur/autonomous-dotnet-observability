-- Apply after ERP_ErrorManagement_SQLite_v1.sql in the integrated deployment.
-- Monitoring and recovery use these metadata tables in the same release.
PRAGMA foreign_keys = ON;
BEGIN TRANSACTION;

CREATE TABLE monitored_instance (
  instance_id TEXT PRIMARY KEY,
  application_id TEXT NOT NULL REFERENCES application(application_id),
  environment_id TEXT NOT NULL REFERENCES environment(environment_id),
  instance_key TEXT NOT NULL,
  host_id TEXT NOT NULL,
  process_name TEXT NOT NULL,
  process_id INTEGER,
  runtime_kind TEXT NOT NULL CHECK (runtime_kind IN ('NET_FRAMEWORK', 'NET', 'OTHER')),
  started_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NOT NULL,
  UNIQUE(application_id, environment_id, instance_key)
);

CREATE TABLE monitoring_rule (
  rule_id TEXT PRIMARY KEY,
  application_id TEXT REFERENCES application(application_id),
  environment_id TEXT REFERENCES environment(environment_id),
  metric_name TEXT NOT NULL,
  comparator TEXT NOT NULL CHECK (comparator IN ('GT', 'GTE', 'LT', 'LTE')),
  threshold_value REAL NOT NULL,
  evaluation_window_seconds INTEGER NOT NULL CHECK (evaluation_window_seconds > 0),
  capture_window_minutes INTEGER NOT NULL DEFAULT 20 CHECK (capture_window_minutes BETWEEN 1 AND 60),
  dump_max_count INTEGER NOT NULL DEFAULT 2 CHECK (dump_max_count BETWEEN 0 AND 10),
  dump_min_interval_seconds INTEGER NOT NULL DEFAULT 300 CHECK (dump_min_interval_seconds >= 0),
  cooldown_minutes INTEGER NOT NULL DEFAULT 60 CHECK (cooldown_minutes >= 0),
  enabled INTEGER NOT NULL DEFAULT 0 CHECK (enabled IN (0,1)),
  CHECK (environment_id IS NULL OR application_id IS NOT NULL)
);

CREATE TABLE monitoring_incident (
  incident_id TEXT PRIMARY KEY,
  instance_id TEXT NOT NULL REFERENCES monitored_instance(instance_id),
  rule_id TEXT NOT NULL REFERENCES monitoring_rule(rule_id),
  error_definition_id TEXT REFERENCES error_definition(error_definition_id),
  ticket_id TEXT REFERENCES ticket(ticket_id),
  correlation_id TEXT,
  status TEXT NOT NULL CHECK (status IN ('OPEN','INVESTIGATING','RECOVERED','CLOSED')),
  observed_value REAL NOT NULL,
  triggered_at_utc TEXT NOT NULL,
  ended_at_utc TEXT,
  assessment TEXT
);

CREATE TABLE diagnostic_session (
  session_id TEXT PRIMARY KEY,
  incident_id TEXT NOT NULL REFERENCES monitoring_incident(incident_id),
  started_at_utc TEXT NOT NULL,
  expires_at_utc TEXT NOT NULL,
  stopped_at_utc TEXT,
  log_level TEXT,
  trace_profile TEXT,
  pre_trigger_window_seconds INTEGER NOT NULL DEFAULT 0 CHECK (pre_trigger_window_seconds >= 0),
  resource_budget_bytes INTEGER NOT NULL CHECK (resource_budget_bytes > 0),
  status TEXT NOT NULL CHECK (status IN ('ACTIVE','COMPLETED','FAILED','CANCELLED')),
  CHECK (expires_at_utc > started_at_utc)
);

CREATE UNIQUE INDEX ux_diagnostic_session_active ON diagnostic_session(incident_id) WHERE status = 'ACTIVE';

CREATE TABLE diagnostic_artifact (
  artifact_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL REFERENCES diagnostic_session(session_id),
  incident_id TEXT NOT NULL REFERENCES monitoring_incident(incident_id),
  artifact_type TEXT NOT NULL CHECK (artifact_type IN ('LOG_BUNDLE','ETW_TRACE','NET_TRACE','MEMORY_DUMP','GC_DUMP','OTHER')),
  storage_uri TEXT NOT NULL,
  checksum TEXT NOT NULL,
  size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
  captured_at_utc TEXT NOT NULL,
  retention_until_utc TEXT NOT NULL,
  access_classification TEXT NOT NULL CHECK (access_classification IN ('SUPPORT','RESTRICTED'))
);

CREATE TABLE recommendation (
  recommendation_id TEXT PRIMARY KEY,
  incident_id TEXT NOT NULL REFERENCES monitoring_incident(incident_id),
  type TEXT NOT NULL,
  target_kind TEXT NOT NULL,
  target_id TEXT NOT NULL,
  rationale TEXT NOT NULL,
  confidence REAL CHECK (confidence IS NULL OR confidence BETWEEN 0 AND 1),
  evidence_summary TEXT,
  state TEXT NOT NULL CHECK (state IN ('PROPOSED','APPROVED','REJECTED','SUPERSEDED')),
  created_at_utc TEXT NOT NULL,
  reviewed_by TEXT,
  reviewed_at_utc TEXT
);

CREATE TABLE remediation_action (
  action_id TEXT PRIMARY KEY,
  recommendation_id TEXT NOT NULL REFERENCES recommendation(recommendation_id),
  incident_id TEXT NOT NULL REFERENCES monitoring_incident(incident_id),
  idempotency_key TEXT NOT NULL UNIQUE,
  action_type TEXT NOT NULL CHECK (action_type IN ('RESTART_INSTANCE','OPEN_CIRCUIT','CLOSE_CIRCUIT','OTHER')),
  target_id TEXT NOT NULL,
  requested_by TEXT NOT NULL,
  approved_by TEXT,
  execution_status TEXT NOT NULL CHECK (execution_status IN ('PENDING','APPROVED','REJECTED','RUNNING','SUCCEEDED','FAILED','ROLLED_BACK')),
  started_at_utc TEXT,
  ended_at_utc TEXT,
  before_metrics TEXT,
  after_metrics TEXT,
  outcome TEXT,
  cooldown_until_utc TEXT,
  rollback_status TEXT
);

CREATE TABLE incident_event (
  event_id TEXT PRIMARY KEY,
  incident_id TEXT NOT NULL REFERENCES monitoring_incident(incident_id),
  event_type TEXT NOT NULL,
  event_at_utc TEXT NOT NULL,
  actor TEXT NOT NULL,
  summary TEXT NOT NULL,
  artifact_id TEXT REFERENCES diagnostic_artifact(artifact_id),
  action_id TEXT REFERENCES remediation_action(action_id)
);

CREATE INDEX ix_monitored_instance_last_seen ON monitored_instance(application_id, environment_id, last_seen_at_utc DESC);
CREATE INDEX ix_monitoring_incident_instance_time ON monitoring_incident(instance_id, triggered_at_utc DESC);
CREATE INDEX ix_diagnostic_artifact_session_time ON diagnostic_artifact(session_id, captured_at_utc DESC);
CREATE INDEX ix_recommendation_incident ON recommendation(incident_id, created_at_utc DESC);
CREATE INDEX ix_remediation_action_incident ON remediation_action(incident_id, started_at_utc DESC);
CREATE INDEX ix_incident_event_time ON incident_event(incident_id, event_at_utc);

CREATE TRIGGER protect_incident_event_update BEFORE UPDATE ON incident_event
BEGIN SELECT RAISE(ABORT, 'incident event is append-only'); END;
CREATE TRIGGER protect_incident_event_delete BEFORE DELETE ON incident_event
BEGIN SELECT RAISE(ABORT, 'incident event is append-only'); END;

INSERT INTO schema_version(version, description, applied_at_utc, applied_by)
VALUES (2, 'Monitoring and recovery incident metadata', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'schema script');
COMMIT;
