using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WeldingSystem.Contracts;
using WeldingSystem.Contracts.Proto;

namespace WeldingSystem.Hardware
{
    /// <summary>
    /// Simulates the physical welding controller (as in Bosch Rexroth PRI 7000).
    /// Encodes each weld cycle as a Protobuf WeldingEvent, then forwards it to the
    /// WCF backend over the duplex channel's one-way PushTelemetry operation.
    ///
    /// Protobuf is used here for the ENCODING of the event payload (compact, versioned,
    /// cross-platform schema) while WCF/NetTcpBinding is the TRANSPORT + duplex channel.
    /// This mirrors real industrial setups where the wire schema (Protobuf) is decoupled
    /// from the specific RPC transport (WCF today, could be gRPC tomorrow).
    /// </summary>
    public class HardwareSimulatorWorker : BackgroundService
    {
        private readonly ILogger<HardwareSimulatorWorker> _logger;
        private readonly Random _rng = new Random();
        private const string MachineId = "LINE3-WELDER-01";

        public HardwareSimulatorWorker(ILogger<HardwareSimulatorWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // DuplexChannelFactory requires an InstanceContext even on the "sender" side
            // here because PushTelemetry's contract declares a CallbackContract; the hardware
            // simulator doesn't act on callbacks itself, so we pass a no-op handler.
            var callbackHandler = new NoOpCallbackHandler();
            var binding = new NetTcpBinding(SecurityMode.Transport);
            var endpoint = new EndpointAddress("net.tcp://localhost:9000/WeldingService");
            var context = new InstanceContext(callbackHandler);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var factory = new DuplexChannelFactory<IWeldingService>(context, binding, endpoint))
                    {
                        IWeldingService channel = factory.CreateChannel();

                        for (int i = 0; i < 5 && !stoppingToken.IsCancellationRequested; i++)
                        {
                            WeldingEvent protoEvent = GenerateWeldEvent();

                            // --- Protobuf encode step (what would go over gRPC/MQTT in a pure-Protobuf transport) ---
                            byte[] encoded = protoEvent.ToByteArray();
                            _logger.LogInformation("Encoded WeldingEvent: {Bytes} bytes for machine {MachineId}",
                                encoded.Length, protoEvent.MachineId);

                            // --- Decode immediately to build the WCF DTO sent over the duplex channel ---
                            WeldingEvent decoded = WeldingEvent.Parser.ParseFrom(encoded);
                            WeldingEventDto dto = TelemetryMapper.ToDto(decoded);

                            channel.PushTelemetry(dto);
                            _logger.LogInformation(
                                "Pushed telemetry: Current={Current}A Voltage={Voltage}V Error={Error}",
                                dto.CurrentAmps, dto.VoltageVolts, dto.ErrorCode);

                            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        }

                        ((ICommunicationObject)channel).Close();
                    }
                }
                catch (CommunicationException ex)
                {
                    _logger.LogWarning(ex, "WCF channel fault \u2014 service may not be running yet. Retrying in 5s.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private WeldingEvent GenerateWeldEvent()
        {
            bool isFault = _rng.NextDouble() < 0.1; // ~10% of cycles simulate a fault
            return new WeldingEvent
            {
                MachineId = MachineId,
                JobId = $"JOB-{DateTime.UtcNow:yyyyMMddHHmm}",
                CurrentAmps = Math.Round(180 + _rng.NextDouble() * 40, 2),
                VoltageVolts = Math.Round(22 + _rng.NextDouble() * 4, 2),
                WeldDurationMs = Math.Round(250 + _rng.NextDouble() * 50, 2),
                ErrorCode = isFault ? 4071 : 0, // 4071 = example "current spike" fault code
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }

    /// <summary>Maps the Protobuf wire type to the WCF MessageContract DTO.</summary>
    internal static class TelemetryMapper
    {
        public static WeldingEventDto ToDto(WeldingEvent proto) => new WeldingEventDto
        {
            MachineId = proto.MachineId,
            JobId = proto.JobId,
            CurrentAmps = proto.CurrentAmps,
            VoltageVolts = proto.VoltageVolts,
            WeldDurationMs = proto.WeldDurationMs,
            ErrorCode = proto.ErrorCode,
            TimestampUnixMs = proto.TimestampUnixMs
        };
    }

    /// <summary>
    /// The hardware simulator never receives callbacks (it's a sender), but the duplex
    /// contract still requires a callback instance to open the channel. Minimal no-op impl.
    /// </summary>
    internal class NoOpCallbackHandler : IWeldingCallback
    {
        public void OnTelemetryReceived(WeldingEventDto telemetry) { /* not used by hardware simulator */ }
        public void OnMachineAlarm(string machineId, string alertMessage, int errorCode) { /* not used */ }
    }
}
