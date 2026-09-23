using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ModernErp;
[ApiController,Route("api/demo")]
public sealed class DemoController(DemoDatabase database,IExceptionReporter reporter,DemoBusiness business,IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("controller")]
    public IActionResult ControllerFailure()=>throw new InvalidOperationException("Modern controller demonstration failure");
    [HttpGet("business")]
    public IActionResult BusinessFailure(){business.Execute();return Ok();}
    [HttpGet("timeout")]
    public IActionResult TimeoutFailure()=>throw new TimeoutException("Modern timeout demonstration");
    [HttpGet("database")]
    public async Task<IActionResult> DatabaseFailure(){await database.Database.ExecuteSqlRawAsync("SELECT * FROM deliberately_missing_demo_table");return Ok();}
    [HttpPost("savechanges")]
    public async Task<IActionResult> SaveFailure()
    {
        var code=Guid.NewGuid().ToString("N");database.Rows.AddRange(new DemoRow{Code=code},new DemoRow{Code=code});
        await database.SaveChangesAsync();return Ok();
    }
    [HttpGet("connection")]
    public async Task<IActionResult> ConnectionFailure()
    {
        database.Database.SetConnectionString("Data Source="+Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"missing.db"));
        await database.Database.OpenConnectionAsync();return Ok();
    }
    [HttpGet("handled")]
    public async Task<IActionResult> HandledFailure()
    {
        try{business.Execute();return Ok();}
        catch(Exception ex){var receipt=await reporter.ReportAsync(ex,new ErrorCaptureContext{Layer=ErrorLayer.Business,Module="Demo"});
            return Ok(new{message="Fallback completed",receipt.ErrorReference,receipt.CorrelationId,receipt.CanReportIssue});}
    }
    [HttpGet("legacy")]
    public async Task<IActionResult> CallLegacy(CancellationToken ct)
    {
        using var response=await clients.CreateClient("legacy").GetAsync("api/demo/controller",ct);
        return new ContentResult{StatusCode=(int)response.StatusCode,ContentType="application/json",Content=await response.Content.ReadAsStringAsync(ct)};
    }
}
public sealed class DemoBusiness { public void Execute()=>throw new InvalidOperationException("Business demonstration failure"); }
public sealed class DemoRow { public int Id{get;set;} public string Code{get;set;}=""; }
public sealed class DemoDatabase(DbContextOptions<DemoDatabase> options) : DbContext(options)
{
    public DbSet<DemoRow> Rows=>Set<DemoRow>();
    protected override void OnModelCreating(ModelBuilder model)=>model.Entity<DemoRow>().HasIndex(x=>x.Code).IsUnique();
}
public sealed class DemoBackgroundJob(IExceptionReporter reporter,ICorrelationContext correlation,IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue<bool>("Demo:RunBackgroundFailure"))return;
        using(CorrelationIds.BeginScope(correlation))
        {
            try{throw new InvalidOperationException("Background demonstration failure");}
            catch(Exception ex){await reporter.ReportAsync(ex,new ErrorCaptureContext{Layer=ErrorLayer.BackgroundJob,Module="DemoJob"},stoppingToken);}
        }
    }
}
