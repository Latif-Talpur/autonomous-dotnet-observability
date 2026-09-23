using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;

namespace Company.ErrorManagement.WebApi2
{
    // Inherits the host's authorization filters. Hosts should protect and rate-limit this route.
    public sealed class ClientErrorsController : ApiController
    {
        private readonly IErrorReporter reporter;private readonly ICorrelationContext correlation;private readonly ErrorManagementOptions options;
        public ClientErrorsController(IErrorReporter reporter,ICorrelationContext correlation,ErrorManagementOptions options)
        {this.reporter=reporter;this.correlation=correlation;this.options=options;}
        [HttpPost]
        public async Task<HttpResponseMessage> Post([FromBody] ClientErrorPayload payload,CancellationToken cancellationToken)
        {
            if(!ModelState.IsValid||!ClientErrorMapping.IsValid(payload))return Request.CreateResponse(HttpStatusCode.BadRequest);
            var principal=Request.GetRequestContext()?.Principal;
            var envelope=ClientErrorMapping.Map(payload,options.ApplicationName,options.EnvironmentName,options.ApplicationVersion,correlation.CorrelationId,
                principal?.Identity?.IsAuthenticated==true?principal.Identity.Name:null);
            var receipt=await reporter.CaptureAsync(envelope,cancellationToken).ConfigureAwait(false);
            var status=receipt.Persisted?HttpStatusCode.Accepted:HttpStatusCode.ServiceUnavailable;
            return Request.CreateResponse(status,new SafeErrorResponse{ErrorReference=receipt.ErrorReference,CorrelationId=receipt.CorrelationId,
                CanReportIssue=receipt.CanReportIssue,Status=(int)status,Title=receipt.Persisted?"Client error accepted":"Error reporting is temporarily unavailable"});
        }
    }
    public sealed class ClientErrorPayload : ClientErrorReport { }
}
