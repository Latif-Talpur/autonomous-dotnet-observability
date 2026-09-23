using Company.ErrorManagement.Persistence.Sqlite;

namespace Company.ErrorManagement.IngestionService.Hosting;

public sealed class RetentionHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RetentionHostedService> _logger;
    private readonly IConfiguration _configuration;

    public RetentionHostedService(
        IServiceProvider services,
        ILogger<RetentionHostedService> logger,
        IConfiguration configuration)
    {
        _services = services;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _configuration.GetValue<int?>("ErrorManagement:RetentionIntervalHours") ?? 24;
        var retentionDays = _configuration.GetValue<int?>("ErrorManagement:RetentionDays") ?? 90;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var reporting = scope.ServiceProvider.GetRequiredService<IReportingRepository>();
                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                var archived = await reporting.ArchiveOccurrencesOlderThanAsync(cutoff, stoppingToken);
                if (archived > 0) _logger.LogInformation("Retention pass archived {Rows} occurrences", archived);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Retention pass failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
