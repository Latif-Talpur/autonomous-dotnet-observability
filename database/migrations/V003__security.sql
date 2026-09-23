-- Application API credentials for server-to-server ingestion authentication.
-- Raw keys are never stored; only the SHA-256 hash and a display prefix are kept.

CREATE TABLE IF NOT EXISTS application_credential (
    credential_id       TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
    application_id      TEXT NOT NULL,
    key_hash            TEXT NOT NULL UNIQUE,
    key_prefix          TEXT NOT NULL,    -- first 12 chars of the raw key for display
    description         TEXT,
    created_at_utc      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    expires_at_utc      TEXT,             -- NULL = does not expire
    is_active           INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (application_id) REFERENCES application(application_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_application_credential_hash
    ON application_credential(key_hash);

CREATE INDEX IF NOT EXISTS ix_application_credential_app
    ON application_credential(application_id, is_active);
