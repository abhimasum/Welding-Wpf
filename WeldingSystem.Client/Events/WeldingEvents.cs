using Prism.Events;
using WeldingSystem.Contracts;

namespace WeldingSystem.Client.Events
{
    /// <summary>Published every time a telemetry callback arrives from the WCF duplex channel.
    /// Any number of ViewModels can subscribe independently (dashboard tile, chart, raw log view)
    /// without any of them knowing about the WCF client or each other \u2014 this decoupling via
    /// IEventAggregator is the whole point of using Prism events instead of a shared callback field.</summary>
    public class TelemetryReceivedEvent : PubSubEvent<WeldingEventDto> { }

    /// <summary>Published when the backend raises an alarm (non-zero error code).</summary>
    public class MachineAlarmEvent : PubSubEvent<MachineAlarmPayload> { }

    public class MachineAlarmPayload
    {
        public string MachineId { get; set; }
        public string Message { get; set; }
        public int ErrorCode { get; set; }
    }
}
