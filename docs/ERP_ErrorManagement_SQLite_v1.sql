-- ERP Error Management, SQLite schema v1.
-- Execute on a dedicated SQLite file. Every connection must enable foreign_keys.
PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at_utc TEXT NOT NULL,
    applied_by TEXT NOT NULL
);

CREATE TABLE application (
    application_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1))
);

CREATE TABLE environment (
    environment_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL
);

CREATE TABLE severity (
    severity_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    rank INTEGER NOT NULL UNIQUE CHECK (rank > 0)
);

CREATE TABLE error_category (
    category_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    default_severity_id TEXT REFERENCES severity(severity_id)
);

CREATE TABLE support_queue (
    queue_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1))
);

CREATE TABLE ticket_status (
    status_id TEXT PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    display_order INTEGER NOT NULL,
    is_terminal INTEGER NOT NULL DEFAULT 0 CHECK (is_terminal IN (0, 1))
);

-- Fingerprint is a stable, versioned hash produced by the shared application core.
CREATE TABLE error_definition (
    error_definition_id TEXT PRIMARY KEY,
    application_id TEXT NOT NULL REFERENCES application(application_id),
    environment_id TEXT NOT NULL REFERENCES environment(environment_id),
    fingerprint TEXT NOT NULL,
    fingerprint_version INTEGER NOT NULL DEFAULT 1 CHECK (fingerprint_version > 0),
    layer TEXT NOT NULL,
    category_id TEXT REFERENCES error_category(category_id),
    severity_id TEXT REFERENCES severity(severity_id),
    exception_type TEXT,
    normalized_message TEXT NOT NULL,
    first_occurred_at_utc TEXT NOT NULL,
    last_occurred_at_utc TEXT NOT NULL,
    occurrence_count INTEGER NOT NULL DEFAULT 0 CHECK (occurrence_count >= 0),
    UNIQUE (application_id, environment_id, fingerprint_version, fingerprint)
);

-- event_id allows safe retries. The public error_reference identifies this occurrence.
CREATE TABLE error_occurrence (
    error_occurrence_id TEXT PRIMARY KEY,
    event_id TEXT NOT NULL UNIQUE,
    error_definition_id TEXT NOT NULL REFERENCES error_definition(error_definition_id),
    error_reference TEXT NOT NULL UNIQUE,
    correlation_id TEXT NOT NULL,
    occurred_at_utc TEXT NOT NULL,
    received_at_utc TEXT NOT NULL,
    user_id TEXT,
    tenant_id TEXT,
    application_version TEXT,
    module TEXT,
    screen TEXT,
    component TEXT,
    endpoint TEXT,
    http_status INTEGER CHECK (http_status IS NULL OR http_status BETWEEN 100 AND 599),
    stack_trace TEXT,
    diagnostic_payload TEXT,
    db_provider TEXT,
    db_error_code TEXT,
    db_procedure TEXT,
    db_line_number INTEGER,
    is_timeout INTEGER NOT NULL DEFAULT 0 CHECK (is_timeout IN (0, 1)),
    is_expected INTEGER NOT NULL DEFAULT 0 CHECK (is_expected IN (0, 1)),
    is_transient INTEGER NOT NULL DEFAULT 0 CHECK (is_transient IN (0, 1)),
    client_version TEXT,
    client_ip_hash TEXT
);

CREATE TABLE sla_policy (
    sla_policy_id TEXT PRIMARY KEY,
    queue_id TEXT NOT NULL REFERENCES support_queue(queue_id),
    severity_id TEXT NOT NULL REFERENCES severity(severity_id),
    response_minutes INTEGER NOT NULL CHECK (response_minutes > 0),
    resolution_minutes INTEGER NOT NULL CHECK (resolution_minutes > 0),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    UNIQUE (queue_id, severity_id)
);

CREATE TABLE ticket (
    ticket_id TEXT PRIMARY KEY,
    ticket_number TEXT NOT NULL UNIQUE,
    error_definition_id TEXT NOT NULL REFERENCES error_definition(error_definition_id),
    error_occurrence_id TEXT REFERENCES error_occurrence(error_occurrence_id),
    queue_id TEXT REFERENCES support_queue(queue_id),
    status_id TEXT NOT NULL REFERENCES ticket_status(status_id),
    severity_id TEXT REFERENCES severity(severity_id),
    sla_policy_id TEXT REFERENCES sla_policy(sla_policy_id),
    priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 5),
    source TEXT NOT NULL CHECK (source IN ('USER', 'SUPPORT', 'RULE')),
    reported_by TEXT,
    description TEXT,
    assigned_to TEXT,
    assigned_at_utc TEXT,
    opened_at_utc TEXT NOT NULL,
    response_due_at_utc TEXT,
    resolution_due_at_utc TEXT,
    resolved_at_utc TEXT,
    closed_at_utc TEXT,
    resolution_code TEXT,
    resolution_notes TEXT,
    updated_at_utc TEXT NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)
);

-- Each ticket operation and its audit insert should occur in one transaction.
CREATE TABLE ticket_status_history (
    history_id TEXT PRIMARY KEY,
    ticket_id TEXT NOT NULL REFERENCES ticket(ticket_id),
    previous_status_id TEXT REFERENCES ticket_status(status_id),
    new_status_id TEXT NOT NULL REFERENCES ticket_status(status_id),
    changed_by TEXT NOT NULL,
    changed_at_utc TEXT NOT NULL,
    reason TEXT
);

CREATE TABLE ticket_assignment_history (
    history_id TEXT PRIMARY KEY,
    ticket_id TEXT NOT NULL REFERENCES ticket(ticket_id),
    previous_assignee TEXT,
    new_assignee TEXT,
    changed_by TEXT NOT NULL,
    changed_at_utc TEXT NOT NULL,
    reason TEXT
);

CREATE TABLE ticket_comment (
    comment_id TEXT PRIMARY KEY,
    ticket_id TEXT NOT NULL REFERENCES ticket(ticket_id),
    visibility TEXT NOT NULL CHECK (visibility IN ('INTERNAL', 'REPORTER')),
    comment_text TEXT NOT NULL,
    created_by TEXT NOT NULL,
    created_at_utc TEXT NOT NULL
);

-- Scope is explicit to avoid SQLite's nullable UNIQUE behavior.
-- scope_type: GLOBAL, APPLICATION or ENVIRONMENT; scope_id: GLOBAL or the relevant ID.
CREATE TABLE configuration (
    configuration_id TEXT PRIMARY KEY,
    scope_type TEXT NOT NULL CHECK (scope_type IN ('GLOBAL', 'APPLICATION', 'ENVIRONMENT')),
    scope_id TEXT NOT NULL,
    setting_key TEXT NOT NULL,
    setting_value TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    UNIQUE (scope_type, scope_id, setting_key),
    CHECK (scope_type <> 'GLOBAL' OR scope_id = 'GLOBAL')
);

-- Rules are evaluated by the application; nullable filters mean 'any'.
CREATE TABLE auto_ticket_rule (
    rule_id TEXT PRIMARY KEY,
    application_id TEXT REFERENCES application(application_id),
    environment_id TEXT REFERENCES environment(environment_id),
    category_id TEXT REFERENCES error_category(category_id),
    severity_id TEXT REFERENCES severity(severity_id),
    queue_id TEXT REFERENCES support_queue(queue_id),
    priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 5),
    min_occurrences INTEGER NOT NULL DEFAULT 1 CHECK (min_occurrences > 0),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1))
);

CREATE TABLE archive_batch (
    archive_batch_id TEXT PRIMARY KEY,
    cutoff_at_utc TEXT NOT NULL,
    started_at_utc TEXT NOT NULL,
    completed_at_utc TEXT,
    archived_row_count INTEGER NOT NULL DEFAULT 0 CHECK (archived_row_count >= 0),
    status TEXT NOT NULL CHECK (status IN ('STARTED', 'COMPLETED', 'FAILED')),
    notes TEXT
);

CREATE INDEX ix_error_definition_last_seen ON error_definition(last_occurred_at_utc DESC);
CREATE INDEX ix_error_occurrence_definition_time ON error_occurrence(error_definition_id, occurred_at_utc DESC);
CREATE INDEX ix_error_occurrence_correlation ON error_occurrence(correlation_id, occurred_at_utc DESC);
CREATE INDEX ix_error_occurrence_time ON error_occurrence(occurred_at_utc DESC);
CREATE INDEX ix_ticket_queue_status ON ticket(queue_id, status_id, opened_at_utc DESC);
CREATE INDEX ix_ticket_definition ON ticket(error_definition_id, opened_at_utc DESC);
CREATE INDEX ix_ticket_occurrence ON ticket(error_occurrence_id);
CREATE INDEX ix_ticket_status_history ON ticket_status_history(ticket_id, changed_at_utc);
CREATE INDEX ix_ticket_assignment_history ON ticket_assignment_history(ticket_id, changed_at_utc);
CREATE INDEX ix_ticket_comment ON ticket_comment(ticket_id, created_at_utc);

CREATE TRIGGER protect_status_history_update BEFORE UPDATE ON ticket_status_history
BEGIN SELECT RAISE(ABORT, 'status history is append-only'); END;
CREATE TRIGGER protect_status_history_delete BEFORE DELETE ON ticket_status_history
BEGIN SELECT RAISE(ABORT, 'status history is append-only'); END;
CREATE TRIGGER protect_assignment_history_update BEFORE UPDATE ON ticket_assignment_history
BEGIN SELECT RAISE(ABORT, 'assignment history is append-only'); END;
CREATE TRIGGER protect_assignment_history_delete BEFORE DELETE ON ticket_assignment_history
BEGIN SELECT RAISE(ABORT, 'assignment history is append-only'); END;

INSERT INTO schema_version(version, description, applied_at_utc, applied_by)
VALUES (1, 'Initial SQLite schema', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 'schema script');

COMMIT;
