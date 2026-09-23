# ERP Error Management — Installation & Integration Guide

This guide walks through every step required to get the framework running: database creation, ingestion service startup, and integration into an Angular 20, Web API 2, or .NET 8 host.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Repository Layout](#2-repository-layout)
3. [Database Setup](#3-database-setup)
4. [Ingestion Service](#4-ingestion-service)
5. [Angular Integration](#5-angular-integration)
6. [Web API 2 Integration (.NET Framework 4.7.2)](#6-web-api-2-integration-net-framework-472)
7. [ASP.NET Core Integration (.NET 8)](#7-aspnet-core-integration-net-8)
8. [Entity Framework Core Integration](#8-entity-framework-core-integration)
9. [Running Tests](#9-running-tests)
10. [Configuration Reference](#10-configuration-reference)
11. [Upgrading the Database Schema](#11-upgrading-the-database-schema)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 8.0 or newer | Build ingestion service and modern adapters |
| .NET Framework | 4.7.2 | Build Web API 2 adapter |
| SQLite 3 | 3.35 or newer | Initial persistence (production can migrate to SQL Server) |
| Node.js | 20 or newer | Build Angular library |
| Angular CLI | 20.x | Angular integration |
| PowerShell | 5.1 or PowerShell Core 7+ | Installation scripts |

> **Windows Server note:** All scripts in `docs/installation/scripts/` are PowerShell. Run them from a Developer PowerShell or a terminal with the .NET SDK on `PATH`.

---

## 2. Repository Layout

```
AutonomousDotNetObservability.sln        ← .NET solution (all C# projects)
Directory.Build.props                    ← Shared MSBuild properties

src/
  Company.ErrorManagement.Contracts/    ← .NET Standard 2.0 — shared types + interfaces
  Company.ErrorManagement.Core/         ← .NET Standard 2.0 — fingerprint, redaction, normalizer
  Company.ErrorManagement.Persistence.Sqlite/  ← .NET 8 — SQLite repositories
  Company.ErrorManagement.AspNetCore/   ← .NET 8 — middleware + DI extensions
  Company.ErrorManagement.EntityFrameworkCore/ ← .NET 8 — EF Core interceptor
  Company.ErrorManagement.WebApi2/      ← .NET 4.7.2 — Web API 2 handler + filter
  Company.ErrorManagement.IngestionService/    ← .NET 8 Web API host (central service)
  angular/erp-error-angular/            ← Angular 20 npm library

database/
  migrations/
    V001__initial_schema.sql            ← Errors, tickets, audit, SLA, config
    V002__observability_metadata.sql    ← Monitoring, recovery, diagnostics
  seed/
    seed_reference_data.sql             ← Environments, severities, queues, statuses
  deploy.md                             ← Database deployment notes

tests/
  Company.ErrorManagement.Core.Tests/
  Company.ErrorManagement.Persistence.Sqlite.Tests/
  Company.ErrorManagement.AspNetCore.Tests/

docs/
  installation/                         ← This guide + scripts
  03_Implementation_Architecture_and_Delivery_Plan.md
```

---

## 3. Database Setup

The framework uses a **dedicated SQLite database** (`erp_error_management.db`) owned exclusively by the ingestion service. Application pods never write to it directly.

### Option A — Automated (PowerShell script)

```powershell
.\docs\installation\scripts\setup-database.ps1
```

This script:
- Creates the database file
- Applies all migrations in order
- Seeds reference data (environments, severities, queues, statuses, SLA policies)

### Option B — Manual (SQLite CLI)

```bash
sqlite3 erp_error_management.db < database/migrations/V001__initial_schema.sql
sqlite3 erp_error_management.db < database/migrations/V002__observability_metadata.sql
sqlite3 erp_error_management.db < database/seed/seed_reference_data.sql
```

### Verify

```bash
sqlite3 erp_error_management.db "SELECT version, description FROM schema_version ORDER BY version;"
```

Expected output:
```
1|Initial SQLite schema
2|Monitoring and recovery incident metadata
```

---

## 4. Ingestion Service

The ingestion service is the central .NET 8 Web API that receives errors from all adapters and hosts the ticket and reporting APIs.

### 4.1 Configuration

Edit `src/Company.ErrorManagement.IngestionService/appsettings.json` (or use environment variables / user secrets):

```json
{
  "ConnectionStrings": {
    "ErrorManagementDatabase": "Data Source=erp_error_management.db"
  },
  "ErrorManagement": {
    "ApplicationName": "IngestionService",
    "MigrationsDirectory": "../../../database/migrations",
    "SeedDirectory": "../../../database/seed",
    "RetentionDays": 90,
    "RetentionIntervalHours": 24
  }
}
```

> Set the `MigrationsDirectory` and `SeedDirectory` paths to absolute paths in production environments.

### 4.2 Start (development)

```powershell
.\docs\installation\scripts\start-ingestion-service.ps1
```

Or manually:

```bash
dotnet run --project src/Company.ErrorManagement.IngestionService --launch-profile http
```

The service starts on **http://localhost:5080**. Swagger UI is available at http://localhost:5080/swagger.

### 4.3 Available endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/error-management/events` | Ingest a server-side error |
| `POST` | `/api/error-management/client-errors` | Ingest an Angular/client error |
| `POST` | `/api/tickets` | Create a ticket from an error reference |
| `GET` | `/api/tickets/{ticketId}` | Get ticket details |
| `GET` | `/api/tickets` | Search/list tickets |
| `POST` | `/api/tickets/{ticketId}/assign` | Assign ticket |
| `POST` | `/api/tickets/{ticketId}/status` | Change ticket status |
| `POST` | `/api/tickets/{ticketId}/comments` | Add comment |
| `POST` | `/api/tickets/{ticketId}/resolve` | Resolve ticket |
| `GET` | `/api/tickets/{ticketId}/history` | Status audit trail |
| `GET` | `/api/reports/top-errors` | Top recurring errors |
| `GET` | `/api/reports/queue-stats` | Ticket counts by queue and status |
| `POST` | `/api/reports/retention/run` | Trigger manual retention pass |
| `GET` | `/health` | Health check |

---

## 5. Angular Integration

### 5.1 Install the library

The library lives in `src/angular/erp-error-angular`. For now, reference it as a local package or publish to your private npm registry.

```bash
# From your Angular app root
npm install ../path/to/src/angular/erp-error-angular
```

Or publish to a private registry first:
```bash
cd src/angular/erp-error-angular
npm publish --registry https://your-registry/
```

Then in your Angular app:
```bash
npm install @company/erp-error-angular
```

### 5.2 Register once in app.config.ts

```typescript
import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  provideErpErrorManagement,
  erpCorrelationInterceptor,
  erpErrorHttpInterceptor,
} from '@company/erp-error-angular';
import { environment } from './environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([
        erpCorrelationInterceptor,   // adds X-Correlation-ID to every request
        erpErrorHttpInterceptor,     // catches HTTP errors, avoids duplicate logging
      ])
    ),
    provideErpErrorManagement({
      applicationName: 'MainERP',
      environment: environment.name,           // 'PROD', 'UAT', etc.
      clientErrorEndpoint: '/api/error-management/client-errors',
      ticketEndpoint: '/api/tickets',
      enableIssueReporting: true,
    }),
  ],
};
```

That is the **only change** required in existing Angular code. No page-level try/catch blocks, no per-component changes.

### 5.3 Report Issue from a component

```typescript
import { Component, inject } from '@angular/core';
import { ErpErrorReportingService, ErpTicketService } from '@company/erp-error-angular';

@Component({ ... })
export class ErrorDialogComponent {
  private reporter = inject(ErpErrorReportingService);
  private tickets = inject(ErpTicketService);

  reportIssue(description: string, reportedBy: string): void {
    const ref = this.reporter.getLastReference();
    if (!ref?.errorReference) return;

    this.tickets.create({
      errorReference: ref.errorReference,
      description,
      reportedBy,
    }).subscribe(ticket => {
      console.log('Ticket created:', ticket.ticketNumber);
    });
  }
}
```

---

## 6. Web API 2 Integration (.NET Framework 4.7.2)

### 6.1 Add NuGet reference

Add a project reference (or NuGet package once published) to `Company.ErrorManagement.WebApi2`.

### 6.2 Register in WebApiConfig.cs

```csharp
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.WebApi2;

public static class WebApiConfig
{
    public static void Register(HttpConfiguration config)
    {
        // --- Existing ERP registrations remain unchanged ---

        // Register error management (add after existing setup)
        ErrorManagementConfig.Register(
            config,
            reporter,           // IErrorReporter from your DI container
            correlationContext, // ICorrelationContext from your DI container
            options =>
            {
                options.ApplicationName = "MainERP";
                options.EnvironmentName = "Production";
                options.EnableClientErrorEndpoint = true;
            });
    }
}
```

#### Resolving IErrorReporter and ICorrelationContext

If the ERP uses Unity or another IoC container, register the core services:

```csharp
// In your Unity / DI bootstrap
container.RegisterType<ICorrelationContext, AmbientCorrelationContext>(
    new ContainerControlledLifetimeManager());

container.RegisterType<IErrorFingerprintProvider, FingerprintProvider>(
    new ContainerControlledLifetimeManager());

container.RegisterType<IPayloadRedactor>(
    new InjectionFactory(_ => new PayloadRedactor()),
    new ContainerControlledLifetimeManager());

container.RegisterType<IErrorNormalizer, ErrorNormalizer>(
    new ContainerControlledLifetimeManager());

container.RegisterType<IErrorReferenceGenerator, ErrorReferenceGenerator>(
    new ContainerControlledLifetimeManager());

// IErrorTransport — use the HTTP transport pointing to the ingestion service
container.RegisterType<IErrorTransport>(
    new InjectionFactory(_ => new HttpErrorTransport("http://localhost:5080")),
    new ContainerControlledLifetimeManager());

container.RegisterType<IErrorReporter, ErrorReporter>(
    new ContainerControlledLifetimeManager());
```

> **No changes are needed** to existing controllers, business services, or stored procedures. The global filter catches everything that escapes the controller boundary automatically.

---

## 7. ASP.NET Core Integration (.NET 8)

### 7.1 Add project references

```xml
<ProjectReference Include="path/to/Company.ErrorManagement.AspNetCore.csproj" />
<ProjectReference Include="path/to/Company.ErrorManagement.Persistence.Sqlite.csproj" />
```

### 7.2 Register in Program.cs

```csharp
using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.Persistence.Sqlite;

// Services
builder.Services.AddErpErrorManagement(options =>
{
    options.ApplicationName = "ModernERP";
    options.EnvironmentName = builder.Environment.EnvironmentName;
    options.ApplicationVersion = "1.0.0";
});

builder.Services.AddErrorManagementSqlite(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("ErrorManagementDatabase")!;
});

// Middleware (order matters — add before routing)
app.UseErpCorrelation();       // establishes X-Correlation-ID context
app.UseErpExceptionHandling(); // catches unhandled exceptions, returns SafeErrorResponse
```

> **No changes needed** to existing controllers or business logic.

---

## 8. Entity Framework Core Integration

### 8.1 Add project reference

```xml
<ProjectReference Include="path/to/Company.ErrorManagement.EntityFrameworkCore.csproj" />
```

### 8.2 Register the interceptor

```csharp
builder.Services.AddSingleton(sp =>
    new ErpDatabaseErrorInterceptor(
        sp.GetRequiredService<IErrorReporter>(),
        sp.GetRequiredService<ICorrelationContext>(),
        sp.GetRequiredService<ILogger<ErpDatabaseErrorInterceptor>>(),
        applicationCode: "ModernERP",
        environmentCode: builder.Environment.EnvironmentName));

builder.Services.AddDbContext<ErpDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetRequiredService<ErpDatabaseErrorInterceptor>());
});
```

SQL exceptions are captured automatically. Stored procedures that propagate errors back to EF Core are captured without any stored procedure modification.

---

## 9. Running Tests

### All tests (PowerShell)

```powershell
.\docs\installation\scripts\run-tests.ps1
```

### Manually

```bash
# Unit tests — no database required
dotnet test tests/Company.ErrorManagement.Core.Tests

# Integration tests — creates a temp SQLite DB, reads migrations from repo
dotnet test tests/Company.ErrorManagement.Persistence.Sqlite.Tests

# ASP.NET Core middleware tests — uses TestServer, no external dependencies
dotnet test tests/Company.ErrorManagement.AspNetCore.Tests

# All at once
dotnet test AutonomousDotNetObservability.sln
```

> The Persistence.Sqlite integration tests locate the repo root automatically using `AutonomousDotNetObservability.sln` as a landmark. Run them from within the repo directory.

---

## 10. Configuration Reference

### Ingestion Service (`appsettings.json`)

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:ErrorManagementDatabase` | `Data Source=erp_error_management.db` | SQLite connection string |
| `ErrorManagement:ApplicationName` | `IngestionService` | Logical name registered in `application` table |
| `ErrorManagement:MigrationsDirectory` | — | Path to `database/migrations/` folder |
| `ErrorManagement:SeedDirectory` | — | Path to `database/seed/` folder |
| `ErrorManagement:RetentionDays` | `90` | Occurrences older than this are archived |
| `ErrorManagement:RetentionIntervalHours` | `24` | How often the retention background job runs |

### SQLite Options (code)

| Property | Default | Description |
|---|---|---|
| `ConnectionString` | `Data Source=erp_error_management.db` | SQLite file path |
| `EnableForeignKeys` | `true` | Always leave `true` |
| `JournalMode` | `WAL` | Write-Ahead Logging for concurrent reads |
| `BusyTimeoutMs` | `5000` | Retry timeout when DB is locked |

### Angular Config

| Property | Required | Description |
|---|---|---|
| `applicationName` | Yes | Must match the `code` column in the `application` table |
| `environment` | Yes | Must match the `code` in the `environment` table (`DEV`, `UAT`, `PROD`) |
| `clientErrorEndpoint` | Yes | URL to POST client errors to |
| `ticketEndpoint` | No | URL to POST ticket creation requests to |
| `enableIssueReporting` | No (default `true`) | Show "Report Issue" option to users |
| `correlationHeaderName` | No (default `X-Correlation-ID`) | Header used for correlation |
| `captureUserAgent` | No (default `true`) | Include browser user-agent |

---

## 11. Upgrading the Database Schema

1. Create a new file named `V003__description.sql` in `database/migrations/`.
2. Wrap it in `BEGIN TRANSACTION; ... COMMIT;`.
3. Add an `INSERT INTO schema_version ...` row at the end.
4. Run the migration runner (restart the ingestion service — it applies pending migrations at startup, or run `setup-database.ps1` again).

The `SqliteMigrationRunner` skips any version already present in `schema_version`, so running it multiple times is safe.

---

## 12. Troubleshooting

### "Application 'ERP' is not registered"

The `ErrorRepository` requires a matching row in the `application` table. Run the seed script or insert manually:

```sql
INSERT INTO application(application_id, code, name, is_active)
VALUES ('app-erp', 'ERP', 'Main ERP', 1);
```

### "Environment 'PROD' is not registered"

Same cause. Seed data inserts `DEV`, `TEST`, `UAT`, `PROD`. Add others as needed.

### Ingestion service cannot find migration files

Set absolute paths in `appsettings.json`:

```json
"ErrorManagement": {
  "MigrationsDirectory": "C:\\path\\to\\repo\\database\\migrations",
  "SeedDirectory":       "C:\\path\\to\\repo\\database\\seed"
}
```

### Angular errors not appearing in the database

1. Confirm `clientErrorEndpoint` is reachable from the browser (check Network tab for 4xx/5xx on the POST).
2. Check that `erpCorrelationInterceptor` and `erpErrorHttpInterceptor` are both in the `withInterceptors([...])` array.
3. Verify the ingestion service is running (`GET /health` returns `Healthy`).

### Duplicate occurrences not being merged

Check that `ApplicationCode` and `EnvironmentCode` are identical across calls. A mismatch in casing produces different fingerprints. Both values are case-sensitive in the deduplication index.

### SQLite "database is locked"

Ensure only the ingestion service writes to the file. Application pods must send events over HTTP to the service, not open the `.db` file directly. For multi-process or multi-server scenarios, migrate to SQL Server and update `IErrorTransport`.
