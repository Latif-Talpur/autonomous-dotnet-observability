# Sending errors to the central service

Monitored applications reference Company.ErrorManagement.Transport.Http and their framework adapter. They do not need a connection to the ingestion SQLite database.

ASP.NET Core registration:

```csharp
using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.Transport.Http;

builder.Services.AddErpErrorManagement(options =>
{
    options.ApplicationName = "ERP";
    options.EnvironmentName = "DEV";
});
builder.Services.AddErrorManagementHttpTransport(options =>
{
    options.Endpoint = new Uri("https://observability.example/api/error-management/events");
});
```

Keep UseErpCorrelation and UseErpExceptionHandling in the host pipeline. Use application/environment codes registered in the central database. Do not register the SQLite provider in the monitored host; it belongs to the ingestion service.

For Web API 2, construct HttpErrorTransport with a long-lived HttpClient and options, pass it to ErrorReporter with the core services, then use ErrorManagementConfig.Register. Configure HttpClientHandler.AllowAutoRedirect=false and UseCookies=false, as the DI registration does.

The transport serializes once and preserves event identity across bounded retries for network failure, timeout, 408, 429, and server errors. Permanent HTTP failures return an unpersisted receipt. Response receipt size and request payload size are bounded. HTTPS is required except for loopback development. Credentials/authentication are a subsequent phase; do not expose an unauthenticated ingestion service publicly.

This is a direct asynchronous HTTP transport: the caller waits for delivery attempts. Durable queuing, offline replay, circuit breaking and background delivery remain to be implemented. Callers must not label an unpersisted receipt as accepted by central storage.

Testing is deferred at the user's request. The .NET workflow is manual-only; existing test code is retained for the later validation phase.
