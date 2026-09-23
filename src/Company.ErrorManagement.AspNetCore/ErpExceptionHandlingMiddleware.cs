using System;
using System.Text.Json;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public sealed class ErpExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErpExceptionHandlingMiddleware> _logger;
        private readonly ErrorManagementOptions _options;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public ErpExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ErpExceptionHandlingMiddleware> logger,
            IOptions<ErrorManagementOptions> options)
        {
            _next = next;
            _logger = logger;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context, IErrorReporter reporter, ICorrelationContext correlation)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

                var correlationId = correlation.CorrelationId ?? Guid.NewGuid().ToString("N");
                ErrorReceipt receipt;

                if (ex.Data.Contains("erp:capturing"))
                {
                    // The EF Core interceptor already stamped this exception and is capturing it
                    // asynchronously.  Skip a second capture to prevent duplicate occurrence records.
                    receipt = new ErrorReceipt
                    {
                        ErrorReference = "ERR-UNKNOWN",
                        CorrelationId = correlationId,
                        Persisted = false,
                        CanReportIssue = false
                    };
                }
                else
                {
                    var envelope = ErrorNormalizer.FromException(ex, ErrorLayer.AspNetCore);
                    envelope.ApplicationCode = _options.ApplicationName;
                    envelope.EnvironmentCode = _options.EnvironmentName;
                    envelope.ApplicationVersion = _options.ApplicationVersion;
                    envelope.CorrelationId = correlationId;
                    envelope.UserId = correlation.UserId;
                    envelope.Endpoint = context.Request.Path;
                    envelope.HttpStatus = 500;

                    var routeValues = context.GetRouteData()?.Values;
                    if (routeValues != null)
                    {
                        envelope.Controller = routeValues.TryGetValue("controller", out var c) ? c?.ToString() : null;
                        envelope.Action = routeValues.TryGetValue("action", out var a) ? a?.ToString() : null;
                    }

                    try
                    {
                        receipt = await reporter.CaptureAsync(envelope, context.RequestAborted);
                    }
                    catch (Exception reportingException)
                    {
                        _logger.LogError(reportingException, "Error reporting itself failed");
                        receipt = new ErrorReceipt
                        {
                            ErrorReference = "ERR-UNKNOWN",
                            CorrelationId = correlationId,
                            Persisted = false
                        };
                    }
                }

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/problem+json";
                    var payload = new SafeErrorResponse
                    {
                        ErrorReference = receipt.ErrorReference,
                        CorrelationId = receipt.CorrelationId,
                        CanReportIssue = receipt.CanReportIssue,
                        Status = 500,
                        Title = "Unable to process the request"
                    };
                    await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
                }
            }
        }
    }
}
