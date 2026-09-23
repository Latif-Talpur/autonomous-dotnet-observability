using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class ClientErrorsController : ApiController
    {
        private readonly IErrorReporter _reporter;
        private readonly ICorrelationContext _correlation;
        private readonly ErrorManagementOptions _options;

        public ClientErrorsController(IErrorReporter reporter, ICorrelationContext correlation, ErrorManagementOptions options)
        {
            _reporter = reporter;
            _correlation = correlation;
            _options = options;
        }

        [HttpPost]
        public async Task<HttpResponseMessage> Post([FromBody] ClientErrorPayload payload, CancellationToken cancellationToken)
        {
            if (payload == null) return Request.CreateResponse(HttpStatusCode.BadRequest);

            var envelope = new ErrorEnvelope
            {
                Layer = ErrorLayer.Angular,
                ApplicationCode = _options.ApplicationName,
                EnvironmentCode = _options.EnvironmentName,
                ApplicationVersion = payload.ApplicationVersion ?? _options.ApplicationVersion,
                CorrelationId = _correlation.CorrelationId ?? Guid.NewGuid().ToString("N"),
                Message = payload.Message,
                StackTrace = payload.StackTrace,
                Module = payload.Module,
                Screen = payload.Screen,
                Component = payload.Component,
                Endpoint = payload.Url,
                Browser = payload.Browser,
                ClientVersion = payload.ClientVersion,
                Device = payload.Device,
                UserId = payload.UserId,
                ExceptionType = payload.ExceptionType,
                HttpStatus = payload.HttpStatus,
                OccurredAtUtc = payload.OccurredAtUtc ?? DateTime.UtcNow
            };

            var receipt = await _reporter.CaptureAsync(envelope, cancellationToken);
            var response = new SafeErrorResponse
            {
                ErrorReference = receipt.ErrorReference,
                CorrelationId = receipt.CorrelationId,
                Status = 202,
                Title = "Client error accepted"
            };
            return Request.CreateResponse(HttpStatusCode.Accepted, response);
        }
    }

    public sealed class ClientErrorPayload
    {
        public string? Message { get; set; }
        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public string? Module { get; set; }
        public string? Screen { get; set; }
        public string? Component { get; set; }
        public string? Url { get; set; }
        public string? Browser { get; set; }
        public string? Device { get; set; }
        public string? ClientVersion { get; set; }
        public string? ApplicationVersion { get; set; }
        public string? UserId { get; set; }
        public int? HttpStatus { get; set; }
        public DateTime? OccurredAtUtc { get; set; }
    }
}
