using Company.ErrorManagement.AspNetCore;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Company.ErrorManagement.AspNetCore.Tests;

public class CorrelationMiddlewareTests
{
    [Fact]
    public async Task Middleware_Echoes_Incoming_Correlation_Id()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var correlationId = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.GetAsync("/ping");
        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().Be(correlationId);
    }

    [Fact]
    public async Task Middleware_Generates_Correlation_Id_When_Missing()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/ping");
        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddErpErrorManagement(o => { o.ApplicationName = "Test"; o.EnvironmentName = "TEST"; });
                    services.AddSingleton<ICorrelationContext, AmbientCorrelationContext>();
                });
                webHost.Configure(app =>
                {
                    app.UseErpCorrelation();
                    app.Map("/ping", b => b.Run(ctx => ctx.Response.WriteAsync("pong")));
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
