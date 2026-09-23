# Proposed Implementation Architecture and Delivery Plan

## Reusable ERP Error Management Library and Ticketing Framework

This section translates the preceding requirements into a practical implementation design. The proposed solution is an embedded, reusable framework: Angular and .NET adapters are registered once in the host application, errors are normalized by a shared core library, and diagnostic, ticket, and audit data are stored in a separate SQL Server database. Existing ERP modules, controllers, business services, and stored procedures remain unchanged unless they contain errors that are handled internally and never reach a global interception point.

## 1 Implementation Approach

The initial deployment will use an embedded-library model. The Angular package captures client-side failures and sends them to an endpoint installed in the existing ERP API. The Web API 2 package captures server and SQL exceptions through global pipeline hooks and persists normalized records through a dedicated persistence provider. A remote ingestion transport can be added later without changing the error contract or the consuming applications.

- Integrate through framework extension points instead of page-by-page error handling.
- Keep error and ticket data outside the ERP business schema.
- Use one normalized error contract across Angular, Web API 2, .NET 8, business logic, and database failures.
- Treat automatic error capture and user-initiated ticket creation as separate operations.
- Prevent the error framework from interrupting the ERP if logging or persistence is unavailable.
- Version Angular, NuGet, API-contract, and database components independently using semantic versioning.

## 2 Overall Architecture

**Figure 1** Embedded Error Management architecture

| Layer | Component | Responsibility |
| --- | --- | --- |
| Angular | `@company/erp-error-angular` | Captures uncaught client errors and HTTP failures, adds correlation headers, displays safe messages, and initiates issue reporting. |
| Legacy API | `Company.ErrorManagement.WebApi2` | Registers a `DelegatingHandler` and global exception filter for .NET Framework 4.7.2/Web API 2. |
| Modern API | `Company.ErrorManagement.AspNetCore` | Registers .NET 8 middleware, dependency injection, Problem Details mapping, and correlation scopes. |
| Business layer | `Company.ErrorManagement.Core` | Normalizes errors, classifies severity, redacts sensitive values, calculates fingerprints, and generates references. |
| Database access | SQL and EF Core adapters | Extracts safe SQL exception metadata and optional command context without requiring stored-procedure changes. |
| Persistence | `Company.ErrorManagement.Persistence.SqlServer` | Creates or updates error definitions, records occurrences, manages tickets, and writes audit history. |
| Administration | Angular feature library | Provides error search, details, ticket queues, status management, audit views, configuration, and reports. |

### 2.1 Runtime Error Flow

1. Angular creates or propagates a correlation ID in the `X-Correlation-ID` request header.
2. The API adapter establishes the same correlation context and executes the existing ERP operation.
3. If an exception escapes the controller or service boundary, the global adapter captures it.
4. The core library normalizes and sanitizes the exception, calculates a fingerprint, and assigns an error reference.
5. The persistence provider stores one error definition and an occurrence record in the separate database.
6. The API returns a safe response containing the error reference and correlation ID; technical details remain server-side.
7. Angular displays the standard notification. If the response already contains an error reference, the client does not log the same backend failure again.
8. A ticket is created only when the user selects **Report Issue** or a configured automatic-ticket rule applies.

## 3 Reusable Library and Package Structure

| Package | Target | Primary contents |
| --- | --- | --- |
| `Company.ErrorManagement.Contracts` | .NET Standard 2.0 | Error envelope, receipt, ticket contracts, enums, configuration contracts, and public interfaces. |
| `Company.ErrorManagement.Core` | .NET Standard 2.0 | Normalization, redaction, fingerprinting, classification, reference generation, and transport abstractions. |
| `Company.ErrorManagement.WebApi2` | .NET Framework 4.7.2 | Exception filter, correlation handler, client-error endpoint, authentication context, and safe API response mapping. |
| `Company.ErrorManagement.AspNetCore` | .NET 8 | Exception and correlation middleware, Problem Details integration, dependency-injection extensions, and health checks. |
| `Company.ErrorManagement.EntityFrameworkCore` | .NET 8 | `SaveChanges` and command interceptors plus provider-specific exception mapping. |
| `Company.ErrorManagement.Persistence.SqlServer` | .NET Standard 2.0 or multi-targeted | Repositories, stored-procedure calls, schema version checks, retry policy, and direct SQL transport. |
| `@company/erp-error-angular` | Angular 20.x | Global `ErrorHandler`, HTTP interceptor, context providers, modal, Report Issue workflow, and optional administration routes. |
| ERP Error Management Database | SQL Server | Isolated error, occurrence, ticket, audit, configuration, SLA, retention, and schema-version objects. |

### 3.1 Core Extension Interfaces

The adapters depend on small interfaces rather than concrete database or HTTP implementations. This permits direct-database mode for the current ERP and remote-service mode for future applications.

```csharp
public interface IErrorReporter
{
    Task<ErrorReceipt> CaptureAsync(
        ErrorEnvelope error,
        CancellationToken cancellationToken);
}

public interface IErrorTransport
{
    Task<ErrorReceipt> SendAsync(
        ErrorEnvelope error,
        CancellationToken cancellationToken);
}

public interface IErrorFingerprintProvider
{
    string Generate(ErrorEnvelope error);
}
```

## 4 Error Management Database Design

The recommended initial deployment uses a dedicated database named `ERP_ErrorManagement` on the same SQL Server instance. If infrastructure policy requires a shared database, the same objects can be deployed under an isolated schema such as `err`. The framework must not create foreign keys to ERP business tables; application, module, entity, and user values are stored as logical identifiers so the framework remains reusable across schemas and applications.

### 4.1 Core Database Objects

| Object | Purpose | Important fields |
| --- | --- | --- |
| `err.Application` | Registered consuming applications. | `ApplicationId`, `Code`, `Name`, `ApiKeyHash`, `IsActive` |
| `err.Environment` | Environment reference data. | `EnvironmentId`, `Code`, `Name` |
| `err.ErrorDefinition` | One master record for each unique error fingerprint. | `ErrorDefinitionId`, `Fingerprint`, `Layer`, `CategoryId`, `ExceptionType`, `NormalizedMessage`, `FirstOccurredAtUtc`, `LastOccurredAtUtc`, `OccurrenceCount` |
| `err.ErrorOccurrence` | Request-specific occurrence history. | `ErrorOccurrenceId`, `ErrorDefinitionId`, `ErrorReference`, `CorrelationId`, `UserId`, `Module`, `Screen`, `Endpoint`, `StackTrace`, `DiagnosticPayload`, `OccurredAtUtc` |
| `err.Ticket` | Actionable incident linked to an error. | `TicketId`, `TicketNumber`, `ErrorDefinitionId`, `ErrorOccurrenceId`, `QueueId`, `StatusId`, `Priority`, `AssignedTo`, `OpenedAtUtc`, `ResolvedAtUtc`, `ClosedAtUtc` |
| `err.TicketStatus` | Configurable workflow statuses. | `TicketStatusId`, `Code`, `Name`, `Sequence`, `IsTerminal` |
| `err.TicketStatusHistory` | Immutable status-transition audit. | `HistoryId`, `TicketId`, `PreviousStatusId`, `NewStatusId`, `ChangedBy`, `ChangedAtUtc`, `Comments` |
| `err.TicketComment` | User and support correspondence. | `CommentId`, `TicketId`, `CommentType`, `CommentText`, `CreatedBy`, `CreatedAtUtc` |
| `err.SupportQueue` | Support routing configuration. | `QueueId`, `Code`, `Name`, `IsActive` |
| `err.ErrorCategory` | Configurable error classification. | `CategoryId`, `Code`, `Name`, `DefaultSeverityId` |
| `err.Severity` | Severity reference data. | `SeverityId`, `Code`, `Name`, `Rank` |
| `err.Configuration` | Application and environment settings. | `ConfigurationId`, `ApplicationId`, `EnvironmentId`, `Key`, `Value`, `IsSensitive` |
| `err.SlaPolicy` | Response and resolution targets. | `SlaPolicyId`, `QueueId`, `SeverityId`, `ResponseMinutes`, `ResolutionMinutes` |
| `err.SchemaVersion` | Database deployment history. | `Version`, `Description`, `AppliedAtUtc`, `AppliedBy` |
| `err.ArchiveBatch` | Retention and archive execution history. | `ArchiveBatchId`, `CutoffDate`, `StartedAtUtc`, `CompletedAtUtc`, `RowCount`, `Status` |

### 4.2 Key Relationships

- `Application` has many `ErrorDefinition` and `ErrorOccurrence` records.
- `ErrorDefinition` has many `ErrorOccurrence` records.
- `ErrorDefinition` can have zero, one, or multiple tickets based on configuration and user actions.
- `Ticket` has many `TicketStatusHistory` and `TicketComment` records.
- `TicketStatusHistory` is append-only and must not be updated after creation.
- `SupportQueue`, `Severity`, `ErrorCategory`, `TicketStatus`, and SLA rules are configuration-driven.

### 4.3 Deduplication Constraint

A fingerprint is calculated after variable values are removed from the message and stack trace. The unique boundary must include application and environment so the same exception in two applications or environments can be analysed independently.

```sql
CREATE UNIQUE INDEX UX_ErrorDefinition_Fingerprint
ON err.ErrorDefinition
(
    ApplicationId,
    EnvironmentId,
    Fingerprint
);
```

The persistence operation first attempts to update `LastOccurredAtUtc` and `OccurrenceCount`. If no row is found, it inserts a new `ErrorDefinition`. The unique index handles concurrent insert attempts. Each request may still create an `ErrorOccurrence` record because user, request, correlation, and timing information differs even when the master error is the same.

### 4.4 Database Deployment and Versioning

Database objects should be owned through a SQL Server Database Project and DACPAC or through ordered, idempotent SQL migration scripts. EF Core migrations should not be the sole owner because both legacy and modern applications consume the shared schema. Each deployment records its version in `err.SchemaVersion`, supports forward-only upgrades, and keeps changes backward-compatible for at least one previous client package version.

## 5 Normalized Error Contract

| Field group | Example fields | Purpose |
| --- | --- | --- |
| Identity | `EventId`, `ErrorReference`, `CorrelationId`, `Fingerprint` | Links the user response, request chain, master error, and occurrence. |
| Application | `Application`, `Environment`, `Version`, `Tenant` | Identifies the producing deployment without linking to ERP tables. |
| Location | `Layer`, `Module`, `Screen`, `Component`, `Endpoint`, `Controller`, `Action` | Shows where the failure occurred. |
| Exception | `ExceptionType`, `Message`, `StackTrace`, `InnerExceptionSummary` | Provides normalized diagnostics for developers. |
| Database | `Provider`, `ErrorNumber`, `Procedure`, `LineNumber`, `IsTimeout` | Captures safe SQL exception context where available. |
| User and client | `UserId`, `Browser`, `ClientVersion`, `Device`, `IP hash` | Supports investigation without unnecessarily storing sensitive client data. |
| Classification | `Category`, `Severity`, `IsExpected`, `IsTransient` | Supports routing, reporting, deduplication, and ticket rules. |
| Time | `OccurredAtUtc`, `FirstSeenAtUtc`, `LastSeenAtUtc` | Provides consistent UTC-based event timing. |

## 6 Application Adapter Examples

### 6.1 Angular 20 Adapter

The Angular package is registered once at application bootstrap. It obtains user and module context through injectable providers and uses router metadata when available. Existing pages do not require separate try/catch blocks.

```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([
        erpCorrelationInterceptor,
        erpErrorHttpInterceptor
      ])
    ),
    provideErpErrorManagement({
      applicationName: 'MainERP',
      environment: environment.name,
      clientErrorEndpoint: '/api/error-management/client-errors',
      enableIssueReporting: true
    })
  ]
};
```

The HTTP interceptor must not create a second record when the backend response already contains `errorReference`. Angular-only errors are submitted to the installed client-error endpoint. Form validation that does not throw an exception is captured only when a shared submit directive or form adapter is enabled; ordinary validation states cannot be discovered reliably by a global exception handler alone.

### 6.2 ASP.NET Web API 2 Adapter

The legacy package targets .NET Framework 4.7.2 and registers global components in `WebApiConfig`. It captures unhandled controller exceptions, establishes request correlation, extracts the authenticated user, and maps the response to a safe error payload.

```csharp
public static void Register(HttpConfiguration config)
{
    ErrorManagementConfig.Register(config, options =>
    {
        options.ApplicationName = "MainERP";
        options.EnvironmentName = "Production";
        options.ConnectionStringName = "ErrorManagementDatabase";
    });
}

// Registration performed by the package:
config.MessageHandlers.Add(new ErrorCorrelationHandler());
config.Filters.Add(new GlobalErrorManagementFilter());
```

### 6.3 ASP.NET Core and .NET 8 Adapter

The modern package uses middleware and dependency-injection extensions while preserving the same normalized error contract and database schema.

```csharp
builder.Services.AddErpErrorManagement(options =>
{
    options.ApplicationName = "ModernERP";
    options.EnvironmentName = builder.Environment.EnvironmentName;
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ErrorManagementDatabase"));
});

app.UseErpCorrelation();
app.UseErpExceptionHandling();
```

### 6.4 Entity Framework Core Adapter

```csharp
builder.Services.AddDbContext<ErpDbContext>((services, options) =>
{
    options.UseSqlServer(erpConnectionString);
    options.AddInterceptors(
        services.GetRequiredService<ErpDatabaseErrorInterceptor>());
});
```

The interceptor records provider error number, timeout classification, and command or procedure name where available. SQL parameter values and full request bodies are excluded by default and may be included only through an explicit, redacted allowlist.

### 6.5 SQL Error Capture Boundaries

- SQL errors returned through API database calls can be captured globally from `SqlException` or EF Core provider exceptions.
- Stored procedures do not require modification when their errors propagate back to the application.
- Errors swallowed inside stored procedures, triggers, or application catch blocks cannot be detected by the global framework.
- SQL Agent jobs and independent database processes require a separate SQL-side collector or monitoring integration.
- Incorrect business data or an empty LOV response is not an exception unless the application identifies it as a failure.

## 7 Correlation and Request Tracking

1. The Angular interceptor reuses an existing correlation ID or generates a new UUID.
2. The value is sent in `X-Correlation-ID` and echoed in the response.
3. The Web API 2 handler or .NET 8 middleware places it in request-local context and logging scopes.
4. Business and repository layers obtain the ID through `ICorrelationContext` without changing method signatures.
5. The ID is written to `ErrorOccurrence` and `Ticket` where applicable.
6. SQL Server session context may optionally receive the value through `sp_set_session_context` for database-side auditing.

## 8 Error Reference and User Experience

The framework generates the reference before persistence so the user can receive a stable identifier even when the normal persistence attempt fails. The API response contains no stack trace, SQL message, connection information, or internal server path.

```json
{
  "type": "https://erp.example/errors/unexpected",
  "title": "Unable to process the request",
  "status": 500,
  "errorReference": "ERR-20260918-004821",
  "correlationId": "8c2e8800-d7f8-4fb3-b539-f960aeb48942",
  "canReportIssue": true
}
```

The Angular notification component displays the safe message, reference number, **Close** action, and **Report Issue** action. **Report Issue** sends the error reference and optional user description to the ticket endpoint; it does not resubmit the technical error.

## 9 Ticketing and Audit Implementation

Ticket creation is separate from automatic error logging. A ticket is created through user action, support action, or a configurable automatic rule such as Critical severity in Production. The ticket references the original error definition and, when appropriate, the occurrence that prompted the report.

| Operation | Processing | Audit result |
| --- | --- | --- |
| Create ticket | Generate ticket number, select queue, apply initial status and priority. | Ticket creation and initial status history. |
| Assign | Set `AssignedTo` and `AssignmentAtUtc`. | Previous and new assignee plus acting user. |
| Change status | Validate configured transition and calculate time in previous status. | Append-only `TicketStatusHistory` record. |
| Add comment | Store user or support note with visibility classification. | Immutable author and timestamp. |
| Resolve | Capture resolution code, notes, and `ResolvedAtUtc`. | Status transition and resolver identity. |
| Close | Set `ClosedAtUtc` after validation. | Terminal transition and closure audit. |
| Reopen | Create a new transition when permitted by configuration. | Reopen reason and previous terminal status. |

## 10 Security and Failure Isolation

- Redact authorization headers, cookies, passwords, tokens, connection strings, secrets, and configured sensitive field names before transport and persistence.
- Do not capture request or response bodies by default. Use allowlisted fields when business context is required.
- Restrict technical error details to authorized support and developer roles; end users see only safe messages and their own ticket status.
- Use a dedicated database login with execute and data access limited to the Error Management database.
- Encrypt communication with TLS and use existing SQL Server encryption controls according to organizational policy.
- Apply payload-size limits and truncate oversized stack traces or diagnostic values with an explicit truncation indicator.
- Catch all internal logging failures so the original ERP request is not replaced by a framework exception.
- Use short persistence timeouts, a circuit breaker, and an optional durable local spool. Do not rely only on an in-memory queue in IIS because application-pool recycling can lose events.

## 11 Deployment Model

| Artifact | Distribution | Deployment action |
| --- | --- | --- |
| Angular library | Private npm package | Install package, add configuration, and register providers/interceptors once. |
| Web API 2 adapter | Private NuGet package | Install package and register the handler/filter in `WebApiConfig`. |
| .NET 8 adapter | Private NuGet package | Install package and register services/middleware in `Program.cs`. |
| Database | DACPAC or signed migration bundle | Deploy `ERP_ErrorManagement` and record the schema version. |
| Administration UI | Angular feature library | Add one lazy-loaded route and authorize it for support roles. |
| Configuration | Host configuration and database tables | Set application code, environment, connection or service endpoint, capture rules, retention, and ticket settings. |

## 12 Implementation Phases

Delivery should proceed as vertical, testable increments. Each phase ends with demonstrable behaviour and documented acceptance criteria.

| Phase | Scope | Main deliverables | Exit criteria |
| --- | --- | --- | --- |
| 0 Discovery and proof of concept | Confirm ERP startup, authentication, dependency injection, SQL access, and representative error paths. | Architecture baseline; one Angular-to-Web-API-to-SQL error demonstration. | One reference is generated, persisted, returned, and searchable without page-level changes. |
| 1 Database foundation | Implement isolated schema, lookup data, stored procedures, indexes, and versioning. | Database project/migrations; seed configuration; repository integration tests. | New and duplicate events persist correctly under concurrency. |
| 2 Core framework | Implement contracts, normalization, redaction, classification, fingerprinting, transports, and fallback. | Core and Contracts NuGet packages with unit tests. | Stable public API and deterministic fingerprints pass the agreed test corpus. |
| 3 Legacy API adapter | Integrate Web API 2 global handler/filter and client-error endpoint. | WebApi2 NuGet package; safe error response; user and endpoint context. | Unhandled API and SQL errors are captured through one-time registration. |
| 4 Angular adapter | Implement `ErrorHandler`, HTTP interceptor, correlation, dialog, and Report Issue action. | Angular npm package; integration guide; example application. | Angular-only and backend errors are handled without duplicate records. |
| 5 Modern .NET adapter | Implement .NET 8 middleware and EF Core interception. | AspNetCore and EntityFrameworkCore NuGet packages. | Same error contract and database support both legacy and modern applications. |
| 6 Ticket and audit module | Implement tickets, queues, transitions, assignments, comments, and audit history. | Ticket APIs; user status view; support administration screens. | Configured lifecycle is enforced and every transition is auditable. |
| 7 Reporting and operations | Implement search, dashboards, SLA metrics, retention, archive, and health monitoring. | Reports, operational metrics, retention job, and health endpoints. | Support users can identify recurring errors and measure ticket performance. |
| 8 Hardening and rollout | Performance, security, failure, compatibility, deployment, and recovery testing. | Runbook, package releases, rollback procedure, training, and phased rollout. | Pilot modules meet non-functional criteria before enterprise-wide enablement. |

### 12.1 Phase Zero Discovery and Proof of Concept

Select one Angular screen, one Web API 2 endpoint, and one SQL operation. Confirm how authentication data is exposed, how dependency injection is configured, how errors are currently returned, and whether the API can write to the separate database. The proof of concept should demonstrate correlation, safe notification, persistence, deduplication, and framework failure isolation.

### 12.2 Core Error Capture Before Ticketing

Complete automatic capture, normalization, security, correlation, and deduplication before implementing ticket workflows. This produces an independently valuable error framework and prevents ticket screens from being built on an unstable event model.

### 12.3 Controlled ERP Rollout

Enable the library first in a non-production environment and then in a small number of ERP modules. Compare application latency, database growth, duplicate rates, and missing-context reports. Broader rollout should use configuration flags so capture or user notifications can be disabled without removing packages or redeploying every module.

## 13 Testing Strategy

| Test category | Coverage |
| --- | --- |
| Unit | Normalizers, redactors, fingerprint rules, classifiers, reference generation, transition validators, and SLA calculations. |
| Integration | SQL persistence, unique constraints, concurrent duplicates, transaction handling, archive jobs, and package startup registration. |
| Angular | Global errors, rejected promises, HTTP failures, existing error references, modal behaviour, user actions, and accessibility. |
| API | Controller, business, timeout, connection, SQL, cancellation, and framework-internal failures. |
| Security | Token and password redaction, authorization boundaries, payload limits, injection attempts, and diagnostic access. |
| Performance | Capture throughput, database indexes, burst duplicates, latency overhead, queue limits, and dashboard queries. |
| Resilience | Error database unavailable, timeout, partial write, application recycle, retry recovery, and fallback replay. |
| Compatibility | Angular 20.x, .NET Framework 4.7.2/Web API 2, .NET 8/EF Core, and supported SQL Server versions. |

## 14 Minimum Changes Required in a Host Application

| Host area | Required change | Not normally required |
| --- | --- | --- |
| Angular bootstrap | Install the npm package and register providers/interceptors. | Editing individual pages, controls, LOVs, or buttons. |
| Web API startup | Install the NuGet package and register global components. | Adding try/catch to every controller action. |
| Configuration | Add application, environment, transport, and database settings. | Changing business configuration or ERP workflows. |
| Database deployment | Create the separate Error Management database/schema. | Changing existing ERP business tables or stored procedures. |
| Administration navigation | Add one authorized lazy-loaded route if the admin UI is embedded. | Rebuilding existing ERP administration screens. |
| Optional validation capture | Add a shared submit directive or base-form adapter when validation telemetry is required. | Field-by-field instrumentation for ordinary exception capture. |

## 15 Acceptance Criteria for the Initial Release

- An uncaught Angular error is captured and stored with application, user, module, screen, browser, and correlation context.
- An unhandled Web API 2 exception is captured through global registration and returned as a safe error reference.
- A SQL exception is recorded with safe provider metadata without exposing SQL details to the user.
- Repeated occurrences create one master error definition and update its frequency while retaining required occurrence history.
- A backend error returned to Angular is not recorded a second time by the HTTP interceptor.
- The user can create a ticket from an error reference and immediately receive a ticket number.
- Support users can assign, update, resolve, close, search, and audit tickets according to configurable workflow rules.
- Disabling or failing the Error Management persistence path does not prevent the ERP from completing otherwise valid work.
- The same normalized contract is demonstrated from Web API 2 and .NET 8 applications.
- Installation and removal steps are documented and do not require changes to individual ERP pages or controllers.

## 16 Scope Boundaries

The release should focus on deterministic error capture and operational ticketing. Log aggregation, distributed tracing, source-map processing, AI-assisted similarity, email or Teams notifications, external service-desk connectors, and multi-region ingestion added through the transport and notification interfaces. The framework complements existing application logs; it does not replace all observability, security monitoring, or database administration tooling.
