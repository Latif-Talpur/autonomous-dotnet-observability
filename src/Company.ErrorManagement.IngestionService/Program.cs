using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.IngestionService.Auth;
using Company.ErrorManagement.IngestionService.Endpoints;
using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── JSON ──────────────────────────────────────────────────────────────────────
// Accept string enum values from Angular and .NET adapters ("Angular", "AspNetCore", etc.).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── Core services ─────────────────────────────────────────────────────────────
var appOptions = builder.Configuration.GetSection("ErrorManagement");
builder.Services.AddErpErrorManagement(o =>
{
    o.ApplicationName    = appOptions["ApplicationName"] ?? "IngestionService";
    o.EnvironmentName    = builder.Environment.EnvironmentName;
    o.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString();
});
builder.Services.AddErrorManagementSqlite(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("ErrorManagementDatabase")
                         ?? "Data Source=erp_error_management.db";
});

// ── Authentication ────────────────────────────────────────────────────────────
var jwtSection  = builder.Configuration.GetSection("Jwt");
var signingKey  = jwtSection["SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is required.");
var jwtIssuer   = jwtSection["Issuer"]   ?? "erp-error-management";
var jwtAudience = jwtSection["Audience"] ?? "erp-error-management";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.SchemeName, _ => { });

// ── Authorization ─────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.ApplicationIngestion, p =>
        p.AddAuthenticationSchemes(ApiKeyAuthenticationOptions.SchemeName)
         .RequireRole("application"));

    options.AddPolicy(AuthorizationPolicies.AnyUser, p =>
        p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
         .RequireAuthenticatedUser());

    options.AddPolicy(AuthorizationPolicies.Support, p =>
        p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
         .RequireRole("support", "admin"));

    options.AddPolicy(AuthorizationPolicies.Admin, p =>
        p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
         .RequireRole("admin"));
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    // Permissive policy for browser client-error reporting.
    options.AddPolicy("ClientErrors", policy =>
        policy.WithOrigins(allowedOrigins)
              .WithMethods("POST", "OPTIONS")
              .WithHeaders("Content-Type", "X-Correlation-ID")
              .SetPreflightMaxAge(TimeSpan.FromHours(1)));

    // Strict policy for the Angular Admin UI.
    options.AddPolicy("AdminUI", policy =>
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .AllowAnyHeader()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromHours(1)));
});

// ── Rate limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Default: 1 000 requests per 60 seconds per IP address.
    options.AddFixedWindowLimiter("default", o =>
    {
        o.Window           = TimeSpan.FromSeconds(60);
        o.PermitLimit      = 1000;
        o.QueueLimit       = 0;
        o.AutoReplenishment = true;
    });

    // Auth endpoints: 15 requests per 60 seconds per IP (brute-force protection).
    options.AddFixedWindowLimiter("strict", o =>
    {
        o.Window           = TimeSpan.FromSeconds(60);
        o.PermitLimit      = 15;
        o.QueueLimit       = 0;
        o.AutoReplenishment = true;
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window           = TimeSpan.FromSeconds(60),
                PermitLimit      = 1000,
                AutoReplenishment = true
            }));
});

// ── Payload size limit ────────────────────────────────────────────────────────
// 512 KB is sufficient for a fully-populated error envelope with stack trace.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 524_288);

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ERP Error Management", Version = "v1" });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type   = SecuritySchemeType.ApiKey,
        In     = ParameterLocation.Header,
        Name   = ApiKeyAuthenticationOptions.HeaderName,
        Scheme = ApiKeyAuthenticationOptions.SchemeName
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Company.ErrorManagement.IngestionService.Hosting.RetentionHostedService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply migrations.
using (var scope = app.Services.CreateScope())
{
    var runner      = scope.ServiceProvider.GetRequiredService<SqliteMigrationRunner>();
    var migrationDir = builder.Configuration["ErrorManagement:MigrationsDirectory"];
    var seedDir      = builder.Configuration["ErrorManagement:SeedDirectory"];
    if (!string.IsNullOrWhiteSpace(migrationDir) && Directory.Exists(migrationDir))
    {
        var applied = runner.Apply(migrationDir, seedDir);
        app.Logger.LogInformation("Applied {Count} migration(s)", applied.Count);
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseErpCorrelation();
app.UseAuthentication();
app.UseAuthorization();
app.UseErpExceptionHandling();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health").AllowAnonymous();
app.MapErrorIngestionEndpoints();
app.MapTicketEndpoints();
app.MapReportingEndpoints();
app.MapApplicationEndpoints();

if (!app.Environment.IsProduction())
    app.MapDevTokenEndpoints();

app.Run();

public partial class Program { }
