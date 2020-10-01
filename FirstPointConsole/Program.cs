using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;

namespace FirstPointConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var service = new FirstPoint.FirstPointService();
            ServiceBase[] servicesToRun = new ServiceBase[] { service };
            if (Environment.UserInteractive)
            {
                Console.WriteLine("Start FirstPoint. Press any key to stop it");
                Console.CancelKeyPress += (x, y) => service.Stop();
                service.Start();
                Console.ReadKey();
                service.Stop();
                Console.WriteLine("Service stopped");
            }
        }
    }
}
