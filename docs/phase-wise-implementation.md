# Phase-wise implementation

The target is a central ingestion service used by monitored ERP applications, with a separate admin UI and monitoring agent. See runtime-application-architecture.md for the component diagram. The ERP client/API are integrations, not replacements for the business application.

## Phase 1: Foundation (in progress)

This increment fixes replay handling in SQLite: the same event ID returns the stored receipt without incrementing counts. Event IDs are globally unique; a replay with a different application, environment, or tenant is rejected. Other constraint failures roll back the whole write. Regression tests cover replay, concurrent replay, distinct events, scope conflicts, and rollback. A manual Windows CI workflow is available for the later testing phase. Testing is deferred at the user's request.

Still required before this phase is complete:
- Finalize API/client JSON contracts and validate ingestion payloads.
- Preserve stable event/reference identities across adapter capture and transport retries.
- Prevent duplicate capture of one exception by EF and middleware.
- Confirm central-only persistence and document supported hosting boundaries.

## Remaining phases

| Phase | Work | Exit criteria |
|---|---|---|
| 2 (started) | Direct HTTP transport and bounded retry implemented; bounded queue, durable spool, circuit breaker and replay remain | Ingestion outage does not break business requests; recovery replays without duplicates |
| 3 | Application registration, credentials, admin authentication and roles | Ingestion and admin routes enforce separate permissions and scope |
| 4 | ASP.NET Core, Web API 2 and EF adapter integration | Sample applications correlate and capture failures end to end |
| 5 | Angular error reporting, notifications and issue submission | Client and HTTP failures report once; reporting failure cannot recurse |
| 6 | Ticket transitions, assignment, comments and audit | Workflow and concurrency rules tested with immutable audit |
| 7 | Admin UI | Authorized users triage errors and manage tickets |
| 8 | Reporting, configuration, retention and operations | Queries and retention preserve audit and obey scope |
| 9 | Monitoring agent, heartbeat and resource metrics | Registered hosts report scoped health and metrics |
| 10 | Trace/dump capture and protected artifact storage | Authorized bounded collection, expiry and audited access |
| 11 | Guarded recovery with approvals and execution audit | Allowlisted actions enforce approval, limits and cooldowns |
| 12 | Packaging, deployment and acceptance testing | Documented install, upgrade, restore and end-to-end checks pass |

Admin UI, monitoring agent, diagnostic capture and recovery remain planned. This increment does not declare Phase 1 or the full application complete. No local .NET runtime is attached to this editing session. Validation is deferred until implementation is ready.
