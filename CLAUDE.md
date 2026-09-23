# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Build
dotnet build

# All tests
dotnet test

# Single test class
dotnet test tests/Company.ErrorManagement.Core.Tests --filter "FullyQualifiedName~FingerprintProviderTests"

# Single test method
dotnet test tests/Company.ErrorManagement.Persistence.Sqlite.Tests --filter "FullyQualifiedName~ErrorRepositoryTests.ConcurrentRetries_OnlyCountOnce"

# Run the ingestion service (Swagger at http://localhost:5080/swagger)
dotnet run --project src/Company.ErrorManagement.IngestionService

# Set up database (migrations + seed; requires sqlite3 CLI on PATH)
.\docs\installation\scripts\setup-database.ps1

# Build the Angular library
cd src/angular/erp-error-angular && npm install && npm run build
```

Migrations are applied automatically at startup when `ErrorManagement:MigrationsDirectory` points to `database/migrations/`. The ingestion service `appsettings.json` sets this for local development.

---

## Architecture

### Error pipeline

Every captured error flows through the same pipeline inside `ErrorReporter` (`Core`):

```
Exception / ErrorEnvelope
  → IErrorNormalizer.Normalize()     — stamps EventId/OccurredAtUtc, trims fields, clamps HttpStatus
  → IPayloadRedactor.Redact()        — removes connection strings, bearer tokens; truncates stack (12 KB), message (2 KB)
  → IErrorFingerprintProvider        — SHA-256 over AppCode|EnvCode|Layer|ExceptionType|NormalizedMessage|NormalizedStack
  → IErrorReferenceGenerator         — ERR-{yyyyMMdd}-{seq:D6}
  → IErrorTransport.SendAsync()      — delivers envelope; returns ErrorReceipt
```

`ErrorReporter` absorbs all transport exceptions and returns `Persisted=false` — it never throws into the host application.

### Deployment topology

The repository contains **one deployable process**: `Company.ErrorManagement.IngestionService`. Everything else is a reusable library. ERP adapters (Web API 2, ASP.NET Core) must send errors to the ingestion service over HTTP; they must not share the SQLite file.

The `DirectSqlErrorTransport` registered by `AddErrorManagementSqlite` writes directly to SQLite and is only correct for use inside the `IngestionService` itself. Host adapters should use `AddErrorManagementHttpTransport` instead, pointing at the central service.

### DI registration pattern

`AddErpErrorManagement()` (AspNetCore package) only registers `ErrorManagementOptions`. Core services (`IErrorReporter`, `IErrorNormalizer`, `ICorrelationContext`, etc.) are registered by either:

- `AddErrorManagementSqlite()` — for the central IngestionService
- `AddErrorManagementHttpTransport()` — for host adapters sending over HTTP

Calling only `AddErpErrorManagement` without one of these will cause DI exceptions at runtime.

### Angular integration quirk

`provideErpErrorManagement()` registers the `ErrorHandler` and config token but does **not** add the HTTP interceptors. Callers must add them manually:

```typescript
provideHttpClient(
  withInterceptors([erpCorrelationInterceptor, erpErrorHttpInterceptor])
)
```

### Database schema

V001 (`database/migrations/V001__initial_schema.sql`) owns the core tables. Key relationships:

- `error_definition` — one row per unique fingerprint per application/environment; `occurrence_count` tracks frequency
- `error_occurrence` — one row per event; `event_id` is the idempotency key (UNIQUE); `error_reference` is also UNIQUE
- `ticket` — linked to an `error_occurrence` via `error_reference`; immutable audit via `ticket_status_history` and `ticket_assignment_history` (append-only triggers)

V002 (`database/migrations/V002__observability_metadata.sql`) adds monitoring, diagnostic, and recovery tables. These tables exist in the schema but have no application logic yet (Phases 9–11).

Seed data (`database/seed/seed_reference_data.sql`): DEV/TEST/UAT/PROD environments, 5 severities, 6 error categories, 4 queues (L1/L2/DATABASE/INFRA), 9 ticket statuses, SLA policies, one application (`ERP`).

### EF Core interceptor and double-recording

`ErpDatabaseErrorInterceptor` stamps `exception.Data["erp:capturing"] = true` and fires a capture asynchronously (fire-and-forget). `ErpExceptionHandlingMiddleware` checks for that key and skips its own capture to prevent recording the same database exception twice. If the middleware sees the stamp it still returns a 500 response, using `Persisted=false` and `ErrorReference="ERR-UNKNOWN"`.

### Ticket workflow

`TicketRepository` handles create, search, assign, status change, comment, and resolve. Status transitions are not yet enforced by allowed-transition rules (Phase 6). SLA due-date columns (`response_due_at_utc`, `resolution_due_at_utc`) on `ticket` are never populated even though `sla_policy_id` is resolved at creation (Phase 6 gap). Optimistic concurrency via `row_version` is incremented but not validated on conflict (Phase 6 gap).

---

## Phase 2 — Spooled HTTP transport

Host adapters (Web API 2, ASP.NET Core) should use `AddErrorManagementHttpTransportWithSpool` instead of the plain transport. `SendAsync` returns immediately with a provisional receipt (`Persisted=false`); delivery happens on the background thread.

```csharp
// ASP.NET Core host adapter
builder.Services.AddErrorManagementHttpTransportWithSpool(
    transport => {
        transport.Endpoint = new Uri("https://central-service/api/error-management/events");
        transport.AttemptTimeout = TimeSpan.FromSeconds(3);
    },
    spool => {
        spool.SpoolDirectory = Path.Combine(Path.GetTempPath(), "erp-error-spool");
        spool.MaxQueuedEvents = 2000;
        spool.CircuitBreakerFailureThreshold = 5;
        spool.CircuitBreakerOpenDuration = TimeSpan.FromSeconds(60);
    });
```

Key behaviours:
- In-memory `Channel<ErrorEnvelope>` (bounded, `DropWrite` mode) — full events fall through to spool.
- Spool = one JSON file per event in `SpoolDirectory`; files are deleted on confirmed delivery.
- `BackgroundDeliveryService` makes **one attempt per event** (not the transport's `MaxAttempts`) — the circuit breaker and 30-second replay interval provide retries without blocking.
- Circuit opens after 5 consecutive failures; half-open probe allowed after 60 s.
- When both queue and spool are full, the event is dropped with a `LogWarning`.
- Spool is replayed immediately on startup, then every `ReplayInterval`; original `EventId` and `ErrorReference` are preserved so the repository's idempotency check prevents duplicates.

`DirectSqlErrorTransport` (registered by `AddErrorManagementSqlite`) remains correct only inside the central `IngestionService`. Never use it in an ERP adapter.

## Phase 3 — Security

### Authentication

Two authentication schemes run in parallel (both registered, JWT is the default):

| Scheme | Header | Used by |
|--------|--------|---------|
| `ApiKey` | `X-Api-Key` | ERP adapters posting errors (`/events`) |
| `Bearer` (JWT) | `Authorization` | Human users (Admin UI, support tools) |

Raw API keys are returned **once** at creation and **never stored** — only the SHA-256 hash is persisted in `application_credential`. Key format: `erp_<32 lowercase hex chars>`.

In non-Production, `POST /api/auth/token` issues JWT tokens for development (roles: `admin`, `support`, `user`). Do NOT expose this in production.

### Authorization policies

| Policy constant | Requires |
|-----------------|---------|
| `AuthorizationPolicies.ApplicationIngestion` | ApiKey scheme + role `application` |
| `AuthorizationPolicies.AnyUser` | JWT scheme + authenticated |
| `AuthorizationPolicies.Support` | JWT + role `support` or `admin` |
| `AuthorizationPolicies.Admin` | JWT + role `admin` |

### JWT configuration

`Jwt:SigningKey` must **not** be stored in `appsettings.json` in production. Use `dotnet user-secrets` or Secret Manager:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<your-secret>" --project src/Company.ErrorManagement.IngestionService
```

### Application credential management

`IApplicationRepository` (registered by `AddErrorManagementSqlite`) manages applications and their API keys. Admin endpoints live at `/api/applications`. Migration `V003__security.sql` must be applied before any authentication works.

### CORS

Two named policies: `ClientErrors` (POST-only from Angular origins) and `AdminUI` (full methods with credentials). Allowed origins configured via `Cors:AllowedOrigins` in appsettings.

### Rate limits

- Global: 1 000 req/60 s per IP
- `strict` policy: 15 req/60 s per IP (intended for auth endpoints)
- Body size limit: 512 KB (Kestrel)

---

## Project phases

Work is tracked in `docs/runtime-application-architecture.md`. Phases 1–3 are complete. Phases 4–12 cover: sample hosts, Angular components, ticket workflow enforcement, Admin UI, reporting, monitoring agent, diagnostics, recovery, and packaging.
