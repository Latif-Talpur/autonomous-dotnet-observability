using System;
using Microsoft.Owin.Hosting;

namespace LegacyErp
{
    internal static class Program
    {
        private static void Main()
        {
            using(WebApp.Start<Startup>("http://127.0.0.1:5081/"))
            {
                Console.WriteLine("Legacy ERP sample: http://127.0.0.1:5081/api/demo/controller");
                Console.WriteLine("Press Enter to stop.");Console.ReadLine();
            }
        }
    }
}
