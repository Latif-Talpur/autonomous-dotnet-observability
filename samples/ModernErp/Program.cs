using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModernErp;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080"); // Fault demonstration host; loopback only.
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=64*1024);
builder.Services.AddControllers();
builder.Services.AddErpErrorManagementHttp(o=>{
    o.ApplicationName="ERP";o.EnvironmentName="DEV";o.ApplicationVersion="phase4-modern";
},o=>{
    o.Endpoint=new Uri(builder.Configuration["Observability:Endpoint"]??"http://127.0.0.1:5070/api/error-management/events");
    o.AttemptTimeout=TimeSpan.FromSeconds(2);o.MaxAttempts=2;
});
builder.Services.AddErpDatabaseInterception();
builder.Services.AddDbContext<DemoDatabase>((services,options)=>{
    options.UseSqlite("Data Source=modern-demo.db");options.AddErpErrorInterceptors(services);
});
builder.Services.AddSingleton<DemoBusiness>();
builder.Services.AddHttpClient("legacy",client=>client.BaseAddress=new Uri("http://127.0.0.1:5081/"))
    .AddErpCorrelationPropagation();
builder.Services.AddHostedService<DemoBackgroundJob>();
var app=builder.Build();
app.UseErpCorrelation();app.UseErpExceptionHandling();
app.MapErpClientErrors(); // Anonymous only for this loopback demo. Apply host authorization in real ERP.
app.MapControllers();
using(var scope=app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<DemoDatabase>().Database.EnsureCreated();
app.Run();
