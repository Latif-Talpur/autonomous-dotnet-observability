using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;

namespace Company.ErrorManagement.WebApi2
{
    internal static class ExceptionCapture
    {
        public static Task<ErrorReceipt> Report(IExceptionReporter reporter, Exception exception, HttpRequestMessage request)
        {
            var principal=request.GetRequestContext()?.Principal;
            request.Properties.TryGetValue(ErrorCorrelationHandler.PropertyKey,out var correlation);
            return reporter.ReportAsync(exception,new ErrorCaptureContext {
                Layer=ErrorLayer.WebApi2,CorrelationId=correlation as string,Endpoint=request.RequestUri?.AbsolutePath,HttpStatus=500,
                UserId=principal?.Identity?.IsAuthenticated==true?principal.Identity.Name:null
            },CancellationToken.None);
        }
        public static HttpResponseMessage Response(HttpRequestMessage request,ErrorReceipt receipt)
        {
            var response=request.CreateResponse(HttpStatusCode.InternalServerError,new SafeErrorResponse {
                ErrorReference=receipt.ErrorReference,CorrelationId=receipt.CorrelationId,CanReportIssue=receipt.CanReportIssue,Status=500,Title="Unable to process the request"
            });
            response.Headers.TryAddWithoutValidation(ErrorCorrelationHandler.HeaderName,receipt.CorrelationId);return response;
        }
    }
    public sealed class ErpExceptionLogger : ExceptionLogger
    {
        private readonly IExceptionReporter reporter;
        public ErpExceptionLogger(IExceptionReporter reporter){this.reporter=reporter;}
        public override async Task LogAsync(ExceptionLoggerContext context,CancellationToken cancellationToken)
        {
            if(context.Exception is OperationCanceledException && cancellationToken.IsCancellationRequested)return;
            try { await ExceptionCapture.Report(reporter,context.Exception,context.Request).ConfigureAwait(false); }
            catch { }
        }
    }
    public sealed class ErpExceptionHandler : ExceptionHandler
    {
        private readonly IExceptionReporter reporter;
        public ErpExceptionHandler(IExceptionReporter reporter){this.reporter=reporter;}
        public override async Task HandleAsync(ExceptionHandlerContext context,CancellationToken cancellationToken)
        {
            if(context.Exception is OperationCanceledException && cancellationToken.IsCancellationRequested)return;
            try {
                var receipt=await ExceptionCapture.Report(reporter,context.Exception,context.Request).ConfigureAwait(false);
                context.Result=new ResponseMessageResult(ExceptionCapture.Response(context.Request,receipt));
            }
            catch {
                context.Result=new ResponseMessageResult(context.Request.CreateResponse(HttpStatusCode.InternalServerError,new SafeErrorResponse{Status=500,Title="Unable to process the request",CanReportIssue=false}));
            }
        }
    }
}
