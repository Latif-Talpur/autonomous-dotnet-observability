using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public sealed class ErpExceptionHandlingMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger<ErpExceptionHandlingMiddleware> logger;
        private readonly ErrorManagementOptions options;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public ErpExceptionHandlingMiddleware(RequestDelegate next, ILogger<ErpExceptionHandlingMiddleware> logger, IOptions<ErrorManagementOptions> options)
        { this.next = next; this.logger = logger; this.options = options.Value; }
        public async Task InvokeAsync(HttpContext http, IErrorReporter reporter, ICorrelationContext correlation)
        {
            try { await next(http); }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var capture = http.RequestServices?.GetService<IExceptionReporter>() ?? new ExceptionReporter(reporter, correlation, options.ApplicationName, options.EnvironmentName, options.ApplicationVersion);
                var receipt = await capture.ReportAsync(ex, new ErrorCaptureContext {
                    Layer = ErrorLayer.AspNetCore, Endpoint = http.Request.Path, HttpStatus = 500,
                    UserId = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null,
                    Controller = http.Request.RouteValues.TryGetValue("controller", out var c) ? c?.ToString() : null,
                    Action = http.Request.RouteValues.TryGetValue("action", out var a) ? a?.ToString() : null
                }, CancellationToken.None);
                logger.LogError("Unhandled request failure {Reference} ({Correlation})", receipt.ErrorReference, receipt.CorrelationId);
                if (http.Response.HasStarted) throw;
                http.Response.Clear(); http.Response.StatusCode = 500; http.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(http.Response.Body, new SafeErrorResponse {
                    ErrorReference = receipt.ErrorReference, CorrelationId = receipt.CorrelationId, CanReportIssue = receipt.CanReportIssue,
                    Status = 500, Title = "Unable to process the request"
                }, JsonOptions, http.RequestAborted);
            }
        }
    }
}
