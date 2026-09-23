using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class GlobalErrorManagementFilter : ExceptionFilterAttribute
    {
        private readonly IErrorReporter _reporter;
        private readonly ICorrelationContext _correlation;
        private readonly ErrorManagementOptions _options;

        public GlobalErrorManagementFilter(
            IErrorReporter reporter,
            ICorrelationContext correlation,
            ErrorManagementOptions options)
        {
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override async Task OnExceptionAsync(HttpActionExecutedContext actionContext, CancellationToken cancellationToken)
        {
            if (actionContext?.Exception == null) return;

            var envelope = ErrorNormalizer.FromException(actionContext.Exception, ErrorLayer.WebApi2);
            envelope.ApplicationCode = _options.ApplicationName;
            envelope.EnvironmentCode = _options.EnvironmentName;
            envelope.ApplicationVersion = _options.ApplicationVersion;
            envelope.CorrelationId = _correlation.CorrelationId ?? Guid.NewGuid().ToString("N");
            envelope.UserId = actionContext.Request?.GetOwinUserName();
            envelope.Endpoint = actionContext.Request?.RequestUri?.AbsolutePath;
            envelope.Controller = actionContext.ActionContext?.ControllerContext?.ControllerDescriptor?.ControllerName;
            envelope.Action = actionContext.ActionContext?.ActionDescriptor?.ActionName;
            envelope.HttpStatus = 500;

            ErrorReceipt receipt;
            try
            {
                receipt = await _reporter.CaptureAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                receipt = new ErrorReceipt
                {
                    ErrorReference = "ERR-UNKNOWN",
                    CorrelationId = envelope.CorrelationId,
                    Persisted = false
                };
            }

            var payload = new SafeErrorResponse
            {
                ErrorReference = receipt.ErrorReference,
                CorrelationId = receipt.CorrelationId,
                CanReportIssue = receipt.CanReportIssue,
                Status = 500,
                Title = "Unable to process the request"
            };

            actionContext.Response = actionContext.Request!.CreateResponse(HttpStatusCode.InternalServerError, payload);
            actionContext.Response.Headers.Add(ErrorCorrelationHandler.HeaderName, receipt.CorrelationId);
        }
    }

    internal static class HttpRequestExtensions
    {
        public static string? GetOwinUserName(this HttpRequestMessage request)
        {
            if (request == null) return null;
            try
            {
                var principal = System.Threading.Thread.CurrentPrincipal;
                return principal?.Identity?.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
