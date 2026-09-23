using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Company.ErrorManagement.Transport.Http;
using Company.ErrorManagement.WebApi2;
using Newtonsoft.Json.Serialization;
using Owin;

namespace LegacyErp
{
    public sealed class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config=new HttpConfiguration();
            config.IncludeErrorDetailPolicy=IncludeErrorDetailPolicy.Never;
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver=new CamelCasePropertyNamesContractResolver();
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            var resolver=new DemoResolver();config.DependencyResolver=resolver;
            var reporter=ErrorManagementConfig.RegisterHttp(config,o=>{
                o.ApplicationName="ERP";o.EnvironmentName="DEV";o.ApplicationVersion="phase4-legacy";
            },o=>{
                o.Endpoint=new Uri(Environment.GetEnvironmentVariable("OBSERVABILITY_ENDPOINT")??"http://127.0.0.1:5070/api/error-management/events");
                o.ApiKey=Environment.GetEnvironmentVariable("OBSERVABILITY_API_KEY") ?? throw new InvalidOperationException("Set OBSERVABILITY_API_KEY to an ERP application credential.");
                o.AttemptTimeout=TimeSpan.FromSeconds(2);o.MaxAttempts=2;
            });
            var correlation=(ICorrelationContext)config.DependencyResolver.GetService(typeof(ICorrelationContext))!;
            resolver.Reporter=reporter;
            resolver.Client=new HttpClient(new CorrelationPropagationHandler(correlation){InnerHandler=new HttpClientHandler{AllowAutoRedirect=false}}){BaseAddress=new Uri("http://127.0.0.1:5080/")};
            config.MapHttpAttributeRoutes();
            app.UseWebApi(config);
            // Demonstrates an awaited explicit background operation. Opt-in; no unattended failure loop.
            if(Environment.GetEnvironmentVariable("DEMO_BACKGROUND_FAILURE")=="1")
                RunBackground(reporter,correlation).GetAwaiter().GetResult();
        }
        private static async Task RunBackground(IExceptionReporter reporter,ICorrelationContext correlation)
        {
            using(CorrelationIds.BeginScope(correlation))
            {
                try{throw new InvalidOperationException("Legacy background demonstration failure");}
                catch(Exception ex){await reporter.ReportAsync(ex,new ErrorCaptureContext{Layer=ErrorLayer.BackgroundJob,Module="LegacyJob"}).ConfigureAwait(false);}
            }
        }
    }
    // Minimal host-owned resolver to show coexistence with the adapter's dependency wrapper.
    internal sealed class DemoResolver : IDependencyResolver
    {
        public IExceptionReporter Reporter{get;set;}=null!;
        public HttpClient Client{get;set;}=null!;
        public object? GetService(Type type)=>type==typeof(DemoController)?new DemoController(Reporter,Client):null;
        public IEnumerable<object> GetServices(Type type)=>Array.Empty<object>();
        public IDependencyScope BeginScope()=>new DemoScope(this);
        public void Dispose()=>Client?.Dispose();
        private sealed class DemoScope : IDependencyScope
        {
            private readonly DemoResolver parent;public DemoScope(DemoResolver parent){this.parent=parent;}
            public object? GetService(Type type)=>parent.GetService(type);
            public IEnumerable<object> GetServices(Type type)=>parent.GetServices(type);
            public void Dispose(){ }
        }
    }
}
