using System;
using System.Data.SqlClient;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Company.ErrorManagement.Contracts;

namespace LegacyErp
{
    [RoutePrefix("api/demo")]
    public sealed class DemoController : ApiController
    {
        private readonly IExceptionReporter reporter;private readonly HttpClient client;
        public DemoController(IExceptionReporter reporter,HttpClient client){this.reporter=reporter;this.client=client;}
        [HttpGet,Route("controller")]
        public IHttpActionResult ControllerFailure()=>throw new InvalidOperationException("Legacy controller demonstration failure");
        [HttpGet,Route("business")]
        public IHttpActionResult BusinessFailure(){BusinessOperation();return Ok();}
        [HttpGet,Route("timeout")]
        public IHttpActionResult TimeoutFailure()=>throw new TimeoutException("Legacy timeout demonstration");
        [HttpGet,Route("database")]
        public Task<IHttpActionResult> DatabaseFailure(CancellationToken ct)=>DatabaseCommand("THROW 51000, 'Legacy SQL demonstration failure', 1;",30,ct);
        [HttpGet,Route("database-timeout")]
        public Task<IHttpActionResult> DatabaseTimeout(CancellationToken ct)=>DatabaseCommand("WAITFOR DELAY '00:00:05'; SELECT 1;",1,ct);
        private async Task<IHttpActionResult> DatabaseCommand(string command,int timeout,CancellationToken ct)
        {
            var connectionString=Environment.GetEnvironmentVariable("DEMO_SQLSERVER_CONNECTION");
            if(string.IsNullOrWhiteSpace(connectionString))return Content(HttpStatusCode.Conflict,new{message="Set DEMO_SQLSERVER_CONNECTION to a disposable SQL Server database."});
            using(var connection=new SqlConnection(connectionString))
            using(var sql=new SqlCommand(command,connection){CommandTimeout=timeout})
            {await connection.OpenAsync(ct);await sql.ExecuteNonQueryAsync(ct);return Ok();}
        }
        [HttpGet,Route("handled")]
        public async Task<IHttpActionResult> HandledFailure()
        {
            try{BusinessOperation();return Ok();}
            catch(Exception ex){var receipt=await reporter.ReportAsync(ex,new ErrorCaptureContext{Layer=ErrorLayer.Business,Module="LegacyDemo"});
                return Ok(new{message="Fallback completed",receipt.ErrorReference,receipt.CorrelationId,receipt.CanReportIssue});}
        }
        [HttpGet,Route("modern")]
        public async Task<HttpResponseMessage> CallModern(CancellationToken ct)
        {
            using(var downstream=await client.GetAsync("api/demo/controller",ct))
                return new HttpResponseMessage(downstream.StatusCode){Content=new StringContent(await downstream.Content.ReadAsStringAsync(),System.Text.Encoding.UTF8,"application/json")};
        }
        private static void BusinessOperation()=>throw new InvalidOperationException("Legacy business demonstration failure");
    }
}
