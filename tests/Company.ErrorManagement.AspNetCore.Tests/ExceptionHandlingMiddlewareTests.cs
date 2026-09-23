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
using System.Net;
using System.Text.Json;
using Xunit;

namespace Company.ErrorManagement.AspNetCore.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Middleware_Returns_500_With_SafeErrorResponse_On_Exception()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/explode");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");

        var json = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<SafeErrorResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBeNull();
        payload!.ErrorReference.Should().NotBeNullOrWhiteSpace();
        payload.CorrelationId.Should().NotBeNullOrWhiteSpace();
        payload.Status.Should().Be(500);
    }

    [Fact]
    public async Task Middleware_Does_Not_Expose_Stack_Trace_To_Client()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/explode");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().NotContain("at ");
        json.Should().NotContain("Exception");
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
                    services.AddSingleton<IErrorReferenceGenerator, ErrorReferenceGenerator>();
                    services.AddSingleton<IErrorFingerprintProvider, FingerprintProvider>();
                    services.AddSingleton<IPayloadRedactor>(_ => new PayloadRedactor());
                    services.AddSingleton<IErrorNormalizer, ErrorNormalizer>();
                    services.AddSingleton<IErrorTransport, NullTransport>();
                    services.AddSingleton<IErrorReporter, ErrorReporter>();
                    services.AddLogging();
                });
                webHost.Configure(app =>
                {
                    app.UseErpCorrelation();
                    app.UseErpExceptionHandling();
                    app.Map("/explode", b => b.Run(_ => throw new InvalidOperationException("This should be hidden")));
                    app.Map("/ok", b => b.Run(ctx => ctx.Response.WriteAsync("ok")));
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class NullTransport : IErrorTransport
    {
        public Task<ErrorReceipt> SendAsync(ErrorEnvelope error, CancellationToken cancellationToken)
            => Task.FromResult(new ErrorReceipt
            {
                ErrorReference = error.ErrorReference ?? "ERR-TEST",
                CorrelationId = error.CorrelationId,
                Persisted = true,
                CanReportIssue = true
            });
    }
}
