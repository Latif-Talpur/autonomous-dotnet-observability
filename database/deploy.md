# Deploying the Error Management SQLite database

## Prerequisites
- SQLite 3.35 or newer for `INSERT OR IGNORE` and modern PRAGMA behavior.
- The CLI: `sqlite3`.

## Fresh install
```bash
sqlite3 ./erp_error_management.db < database/migrations/V001__initial_schema.sql
sqlite3 ./erp_error_management.db < database/migrations/V002__observability_metadata.sql
sqlite3 ./erp_error_management.db < database/seed/seed_reference_data.sql
```

Every connection must enable foreign keys:
```sql
PRAGMA foreign_keys = ON;
```

## Verifying the schema version
```sql
SELECT version, description, applied_at_utc FROM schema_version ORDER BY version;
```

## Applying future migrations
- Name new files `V###__short_name.sql` where `###` is strictly increasing.
- Wrap each in a `BEGIN TRANSACTION; ... COMMIT;` block.
- Every migration must insert its own row into `schema_version`.
- Migrations are forward-only; do not modify a file after it has been applied to a shared environment.

## Production considerations
- SQLite is intended for single-writer central ingestion. For multi-replica ingestion, migrate to SQL Server or PostgreSQL and reuse the repository interfaces in `Company.ErrorManagement.Persistence.Sqlite` as a template.
- Enable WAL: `PRAGMA journal_mode=WAL;`
- Backup with `.backup` while WAL is active.
