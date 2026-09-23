using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;

namespace Company.ErrorManagement.WebApi2
{
    // Optional compatibility filter. RegisterHttp already installs the broader global handler/logger.
    public sealed class GlobalErrorManagementFilter : ExceptionFilterAttribute
    {
        private readonly IExceptionReporter reporter;
        public GlobalErrorManagementFilter(IExceptionReporter reporter){this.reporter=reporter;}
        public GlobalErrorManagementFilter(IErrorReporter reporter,ICorrelationContext correlation,ErrorManagementOptions options)
            :this(new ExceptionReporter(reporter,correlation,options.ApplicationName,options.EnvironmentName,options.ApplicationVersion)) { }
        public override async Task OnExceptionAsync(HttpActionExecutedContext context,CancellationToken cancellationToken)
        {
            if(context?.Exception==null)return;
            if(context.Exception is System.OperationCanceledException && cancellationToken.IsCancellationRequested)return;
            var receipt=await ExceptionCapture.Report(reporter,context.Exception,context.Request).ConfigureAwait(false);
            context.Response=ExceptionCapture.Response(context.Request,receipt);
        }
    }
}
