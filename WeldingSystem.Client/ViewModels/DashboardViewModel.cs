using Prism.Events;
using WeldingSystem.Client.Events;
using WeldingSystem.Contracts;

namespace WeldingSystem.Client.ViewModels
{
    /// <summary>
    /// SUBSCRIBER #1 of TelemetryReceivedEvent. Shows the latest live reading as a dashboard tile.
    /// Knows nothing about WCF, the callback handler, or the other subscriber ViewModels below \u2014
    /// it only knows about the Prism event, which is exactly the decoupling the architecture asks for.
    /// </summary>
    public class DashboardViewModel : ViewModelBase
    {
        private double _currentAmps;
        private double _voltageVolts;
        private string _machineState = "Unknown";

        public double CurrentAmps { get => _currentAmps; set => SetProperty(ref _currentAmps, value); }
        public double VoltageVolts { get => _voltageVolts; set => SetProperty(ref _voltageVolts, value); }
        public string MachineState { get => _machineState; set => SetProperty(ref _machineState, value); }

        public DashboardViewModel(IEventAggregator eventAggregator)
        {
            // ThreadOption.UIThread: Prism marshals the callback onto the UI dispatcher automatically,
            // so setting bound properties here is safe even though the original WCF callback fired
            // on a background thread \u2014 this replaces a manual Application.Current.Dispatcher.Invoke call.
            eventAggregator.GetEvent<TelemetryReceivedEvent>()
                .Subscribe(OnTelemetryReceived, ThreadOption.UIThread);

            eventAggregator.GetEvent<MachineAlarmEvent>()
                .Subscribe(_ => MachineState = "Fault", ThreadOption.UIThread);
        }

        private void OnTelemetryReceived(WeldingEventDto telemetry)
        {
            CurrentAmps = telemetry.CurrentAmps;
            VoltageVolts = telemetry.VoltageVolts;
            MachineState = telemetry.ErrorCode != 0 ? "Fault" : "Running";
        }
    }
}
