using System.ServiceModel;
using WeldingSystem.Contracts;

namespace WeldingSystem.Client.Services
{
    /// <summary>
    /// Owns the duplex WCF channel's lifetime: opens it with the InstanceContext wired to
    /// WeldingCallbackHandler, subscribes to a machine, and exposes GetCurrentStatus for the
    /// initial pull-based load before any callbacks have arrived yet.
    /// </summary>
    public class DuplexClientService
    {
        private DuplexChannelFactory<IWeldingService> _factory;
        private IWeldingService _channel;

        public void Connect(WeldingCallbackHandler callbackHandler, string machineId)
        {
            var context = new InstanceContext(callbackHandler);

            // NetTcpBinding here to match the service's TCP endpoint; swap to NetNamedPipeBinding
            // + "net.pipe://localhost/WeldingServicePipe" if client and service run on the same box.
            var binding = new NetTcpBinding(SecurityMode.Transport);
            var endpoint = new EndpointAddress("net.tcp://localhost:9000/WeldingService");

            _factory = new DuplexChannelFactory<IWeldingService>(context, binding, endpoint);
            _channel = _factory.CreateChannel();

            _channel.Subscribe(machineId);
        }

        public DeviceStatusDto GetCurrentStatus(string machineId) => _channel.GetCurrentStatus(machineId);

        public void Disconnect(string machineId)
        {
            _channel?.Unsubscribe(machineId);
            ((ICommunicationObject)_factory)?.Close();
        }
    }
}
