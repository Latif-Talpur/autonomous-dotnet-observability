using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.IngestionService.Endpoints;
using Company.ErrorManagement.Persistence.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var appOptions = builder.Configuration.GetSection("ErrorManagement");
builder.Services.AddErpErrorManagement(o =>
{
    o.ApplicationName = appOptions["ApplicationName"] ?? "IngestionService";
    o.EnvironmentName = builder.Environment.EnvironmentName;
    o.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString();
});

builder.Services.AddErrorManagementSqlite(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("ErrorManagementDatabase")
                         ?? "Data Source=erp_error_management.db";
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Company.ErrorManagement.IngestionService.Hosting.RetentionHostedService>();

var app = builder.Build();

// Apply migrations from configured folder if present.
using (var scope = app.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<SqliteMigrationRunner>();
    var migrationDir = Path.GetFullPath(builder.Configuration["ErrorManagement:MigrationsDirectory"] ?? "database/migrations", AppContext.BaseDirectory);
    var seedDir = Path.GetFullPath(builder.Configuration["ErrorManagement:SeedDirectory"] ?? "database/seed", AppContext.BaseDirectory);
    if (!Directory.Exists(migrationDir)) throw new DirectoryNotFoundException("Ingestion migrations directory is missing: " + migrationDir);
    if (Directory.Exists(migrationDir))
    {
        var applied = runner.Apply(migrationDir, seedDir);
        app.Logger.LogInformation("Applied {Count} migrations", applied.Count);
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseErpCorrelation();
app.UseErpExceptionHandling();

app.MapHealthChecks("/health");
app.MapErrorIngestionEndpoints();
app.MapTicketEndpoints();
app.MapReportingEndpoints();

app.Run();

public partial class Program { }
