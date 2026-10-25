using System.ServiceModel;
using System.Threading.Tasks;

namespace WeldingSystem.Contracts
{
    /// <summary>
    /// Primary duplex service contract. The hardware simulator calls PushTelemetry;
    /// WPF clients call Subscribe/Unsubscribe and receive pushes via IWeldingCallback.
    /// CallbackContract wires this interface to IWeldingCallback for the duplex channel.
    /// </summary>
    [ServiceContract(CallbackContract = typeof(IWeldingCallback), SessionMode = SessionMode.Required)]
    public interface IWeldingService
    {
        /// <summary>Called by the Hardware Controller Simulator for each weld event.</summary>
        [OperationContract(IsOneWay = true)]
        void PushTelemetry(WeldingEventDto telemetry);

        /// <summary>Called by a WPF client to start receiving push callbacks for a machine.</summary>
        [OperationContract]
        bool Subscribe(string machineId);

        [OperationContract]
        void Unsubscribe(string machineId);

        /// <summary>Simple pull API used by the UI on first load, before callbacks start arriving.</summary>
        [OperationContract]
        DeviceStatusDto GetCurrentStatus(string machineId);
    }

    /// <summary>
    /// Callback contract implemented by the WPF client. The service invokes these methods
    /// on the client's duplex channel whenever new telemetry or an alarm arrives.
    /// </summary>
    [ServiceContract]
    public interface IWeldingCallback
    {
        [OperationContract(IsOneWay = true)]
        void OnTelemetryReceived(WeldingEventDto telemetry);

        [OperationContract(IsOneWay = true)]
        void OnMachineAlarm(string machineId, string alertMessage, int errorCode);
    }

    /// <summary>
    /// Plain DataContract DTO used on the wire for the WCF duplex channel.
    /// Mapped to/from the Protobuf WeldingEvent at the service boundary (see TelemetryMapper).
    /// </summary>
    [MessageContract]
    public class WeldingEventDto
    {
        [MessageBodyMember] public string MachineId { get; set; }
        [MessageBodyMember] public string JobId { get; set; }
        [MessageBodyMember] public double CurrentAmps { get; set; }
        [MessageBodyMember] public double VoltageVolts { get; set; }
        [MessageBodyMember] public double WeldDurationMs { get; set; }
        [MessageBodyMember] public int ErrorCode { get; set; }
        [MessageBodyMember] public long TimestampUnixMs { get; set; }
    }

    [MessageContract]
    public class DeviceStatusDto
    {
        [MessageBodyMember] public string MachineId { get; set; }
        [MessageBodyMember] public string State { get; set; } // Idle, Running, Fault, Maintenance
        [MessageBodyMember] public long LastUpdatedUnixMs { get; set; }
    }
}
