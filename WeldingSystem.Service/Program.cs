using System;
using System.ServiceModel;
using WeldingSystem.Contracts;

namespace WeldingSystem.Service
{
    /// <summary>
    /// Self-hosted WCF service (console app, matching the original PRI 7000-style backend
    /// service engine \u2014 no IIS dependency, so it runs identically on a plant-floor PC).
    /// Exposes TWO endpoints on the same contract:
    ///   - NetTcpBinding on net.tcp://localhost:9000  (hardware simulator + remote WPF clients)
    ///   - NetNamedPipeBinding on net.pipe://localhost/WeldingServicePipe (same-machine WPF client,
    ///     lower overhead than TCP for local-only scenarios, common on a plant-floor operator PC)
    /// </summary>
    public static class ServiceHost_Program
    {
        public static readonly SqliteDataAccess SharedDataAccess =
            new SqliteDataAccess(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "welding.db"));

        public static void Main(string[] args)
        {
            using (var host = new ServiceHost(typeof(WeldingService)))
            {
                // --- NetTcpBinding endpoint: used by the Hardware simulator and remote WPF clients ---
                var tcpBinding = new NetTcpBinding(SecurityMode.Transport);
                host.AddServiceEndpoint(typeof(IWeldingService), tcpBinding, "net.tcp://localhost:9000/WeldingService");

                // --- NetNamedPipeBinding endpoint: used by a WPF client running on the SAME machine ---
                var pipeBinding = new NetNamedPipeBinding();
                host.AddServiceEndpoint(typeof(IWeldingService), pipeBinding, "net.pipe://localhost/WeldingServicePipe");

                // Enable metadata exchange for easy "Add Service Reference" during development.
                var metadataBehavior = new System.ServiceModel.Description.ServiceMetadataBehavior
                {
                    HttpGetEnabled = false
                };
                host.Description.Behaviors.Add(metadataBehavior);

                host.Opened += (s, e) => Console.WriteLine("WeldingService is running.");
                host.Faulted += (s, e) => Console.WriteLine("WeldingService FAULTED.");

                host.Open();

                Console.WriteLine("Endpoints:");
                Console.WriteLine("  net.tcp://localhost:9000/WeldingService");
                Console.WriteLine("  net.pipe://localhost/WeldingServicePipe");
                Console.WriteLine("Press ENTER to stop the service...");
                Console.ReadLine();

                host.Close();
            }
        }
    }
}
