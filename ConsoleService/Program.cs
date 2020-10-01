using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;

namespace ConsoleService
{
    class Program
    {
        static void Main(string[] args)
        {
            var service = new CardRoute.CardRouteService();
            ServiceBase[] servicesToRun = new ServiceBase[] { service };
            if (Environment.UserInteractive)
            {
                Console.WriteLine("Start CardRouteService. Press any key to stop it");
                Console.CancelKeyPress += (x, y) => service.Stop();
                service.Start();
                Console.ReadKey();
                service.Stop();
                Console.WriteLine("Service stopped");
            }
            // CW Hub
            //var service = new CwHubService.sMain();
            //ServiceBase[] servicesToRun = new ServiceBase[] { service };
            //if (Environment.UserInteractive)
            //{
            //    Console.WriteLine("Start CWHubService. Press any key to stop it");
            //    Console.CancelKeyPress += (x, y) => service.Stop();
            //    service.Start();
            //    Console.ReadKey();
            //    service.Stop();
            //    Console.WriteLine("Service stopped");
            //}
        }
    }
}
