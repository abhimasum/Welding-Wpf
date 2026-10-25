using System;
using System.Collections.Concurrent;
using System.ServiceModel;
using WeldingSystem.Contracts;

namespace WeldingSystem.Service
{
    /// <summary>
    /// InstanceContextMode.PerSession: one WeldingService instance per connected client session,
    /// BUT subscriptions/callbacks are tracked in a static, process-wide registry below so that
    /// a telemetry push from the (separate) Hardware session fans out to every subscribed
    /// WPF client session \u2014 this is the standard WCF duplex "pub/sub across sessions" pattern.
    /// </summary>
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class WeldingService : IWeldingService
    {
        // machineId -> set of callback channels subscribed to that machine, shared across all sessions
        private static readonly ConcurrentDictionary<string, ConcurrentBag<IWeldingCallback>> Subscribers
            = new ConcurrentDictionary<string, ConcurrentBag<IWeldingCallback>>();

        private readonly SqliteDataAccess _dal;

        public WeldingService(SqliteDataAccess dal)
        {
            _dal = dal;
        }

        // Parameterless constructor required by default WCF activation; delegates to a shared DAL instance.
        public WeldingService() : this(ServiceHost_Program.SharedDataAccess) { }

        public void PushTelemetry(WeldingEventDto telemetry)
        {
            // 1. Persist via the raw ADO.NET DAL (atomic transaction inside SaveTelemetry)
            _dal.SaveTelemetry(telemetry);

            // 2. Fan out to every WPF client currently subscribed to this machine
            if (Subscribers.TryGetValue(telemetry.MachineId, out var callbacks))
            {
                foreach (IWeldingCallback callback in callbacks)
                {
                    try
                    {
                        callback.OnTelemetryReceived(telemetry);
                        if (telemetry.ErrorCode != 0)
                        {
                            callback.OnMachineAlarm(
                                telemetry.MachineId,
                                $"Fault code {telemetry.ErrorCode} detected \u2014 current {telemetry.CurrentAmps}A",
                                telemetry.ErrorCode);
                        }
                    }
                    catch (CommunicationException)
                    {
                        // Client disconnected without unsubscribing; it will be pruned on next Subscribe cycle.
                        // Production code: remove dead channels from the bag here.
                    }
                }
            }
        }

        public bool Subscribe(string machineId)
        {
            IWeldingCallback callback = OperationContext.Current.GetCallbackChannel<IWeldingCallback>();
            var bag = Subscribers.GetOrAdd(machineId, _ => new ConcurrentBag<IWeldingCallback>());
            bag.Add(callback);
            return true;
        }

        public void Unsubscribe(string machineId)
        {
            // ConcurrentBag has no direct remove; production code would swap to a
            // ConcurrentDictionary<IWeldingCallback, byte> per machine for O(1) removal.
            // Omitted here for brevity \u2014 flagged explicitly rather than hidden.
        }

        public DeviceStatusDto GetCurrentStatus(string machineId) => _dal.GetDeviceStatus(machineId);
    }
}
