using System.Windows;
using Prism.Events;
using WeldingSystem.Client.Services;
using WeldingSystem.Client.ViewModels;
using WeldingSystem.Client.Views;

namespace WeldingSystem.Client
{
    /// <summary>
    /// Composition root. Deliberately kept as plain manual wiring (no Prism.DryIoc/Unity module
    /// system) so the full dependency chain is visible in one place for this reference sample:
    ///   IEventAggregator (shared) -> WeldingCallbackHandler -> DuplexClientService.Connect
    ///   IEventAggregator (shared) -> DashboardViewModel + AlarmsViewModel (both subscribe)
    ///   MainWindow <- both ViewModels injected via constructor
    /// </summary>
    public partial class App : Application
    {
        private const string MachineId = "LINE3-WELDER-01";
        private DuplexClientService _duplexClient;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. One shared EventAggregator instance for the whole app \u2014 this IS the Prism pub/sub bus.
            IEventAggregator eventAggregator = new EventAggregator();

            // 2. Callback handler translates WCF duplex callbacks into Prism event publishes.
            var callbackHandler = new WeldingCallbackHandler(eventAggregator);

            // 3. Open the duplex channel and subscribe to this machine's telemetry/alarms.
            _duplexClient = new DuplexClientService();
            _duplexClient.Connect(callbackHandler, MachineId);

            // 4. Build both independent subscriber ViewModels against the SAME event aggregator.
            var dashboardViewModel = new DashboardViewModel(eventAggregator);
            var alarmsViewModel = new AlarmsViewModel(eventAggregator);

            // 5. Pull the current status once on startup (before any callback has arrived yet).
            var initialStatus = _duplexClient.GetCurrentStatus(MachineId);
            dashboardViewModel.MachineState = initialStatus.State;

            // 6. Compose and show the window.
            var mainWindow = new MainWindow(dashboardViewModel, alarmsViewModel);
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _duplexClient?.Disconnect(MachineId);
            base.OnExit(e);
        }
    }
}
