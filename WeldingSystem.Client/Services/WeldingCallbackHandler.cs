using Prism.Events;
using WeldingSystem.Client.Events;
using WeldingSystem.Contracts;

namespace WeldingSystem.Client.Services
{
    /// <summary>
    /// Implements the WCF callback contract. This class's ONLY job is to translate a raw
    /// duplex callback into a Prism event publish \u2014 it deliberately knows nothing about
    /// any ViewModel. This is the seam between "WCF plumbing" and "application events."
    ///
    /// IMPORTANT: these methods fire on a WCF-owned background thread, NOT the UI thread.
    /// IEventAggregator.Publish() itself is thread-safe to call from any thread, but each
    /// SUBSCRIBER below must request ThreadOption.UIThread (see DashboardViewModel) so that
    /// bound UI properties are only ever mutated on the UI thread.
    /// </summary>
    public class WeldingCallbackHandler : IWeldingCallback
    {
        private readonly IEventAggregator _eventAggregator;

        public WeldingCallbackHandler(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void OnTelemetryReceived(WeldingEventDto telemetry)
        {
            _eventAggregator.GetEvent<TelemetryReceivedEvent>().Publish(telemetry);
        }

        public void OnMachineAlarm(string machineId, string alertMessage, int errorCode)
        {
            _eventAggregator.GetEvent<MachineAlarmEvent>().Publish(new MachineAlarmPayload
            {
                MachineId = machineId,
                Message = alertMessage,
                ErrorCode = errorCode
            });
        }
    }
}
