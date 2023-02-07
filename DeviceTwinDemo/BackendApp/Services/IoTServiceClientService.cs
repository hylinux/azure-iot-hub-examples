using Microsoft.Azure.Devices;
using Microsoft.Extensions.Configuration;

namespace BackendApp.Services
{
    public class IoTServiceClientService
    {
        private readonly ILogger<IoTServiceClientService> _logger;
        private ServiceClient? _serviceClient;
        private RegistryManager? _registryManager;
        private readonly IConfiguration _config;

        public IoTServiceClientService(IConfiguration config, ILogger<IoTServiceClientService> logger)
        {
            _logger= logger;
            _config= config;

            if (_serviceClient != null)
            {
                _serviceClient.CloseAsync();
                _serviceClient.Dispose();
                _serviceClient = null;
                _logger.LogInformation("Closed and disposed the current service client instance.");
            }

            var options = new ServiceClientOptions
            {
                SdkAssignsMessageId = Microsoft.Azure.Devices.Shared.SdkAssignsMessageId.WhenUnset,
            };
            _serviceClient = ServiceClient.CreateFromConnectionString(config["IoTHubConnectString"], options);
            _logger.LogInformation("Initialized a new service client instance.");



        

            if ( _registryManager != null )
            {
                _registryManager.CloseAsync();
                _registryManager.Dispose();
                _registryManager = null;
                _logger.LogInformation("Closed and disposed the current register manager instance.");

            }
            _registryManager = RegistryManager.CreateFromConnectionString(config["IoTHubConnectString"]);
            _logger.LogInformation("Initialized a new register manager instance.");
        }


        public ServiceClient GetServiceClient()
        {
            if (_serviceClient == null)
            {
                var options = new ServiceClientOptions
                {
                    SdkAssignsMessageId = Microsoft.Azure.Devices.Shared.SdkAssignsMessageId.WhenUnset,
                };
                _serviceClient = ServiceClient.CreateFromConnectionString(_config["IoTHubConnectString"], options);
            }

            return _serviceClient;
        }


        public RegistryManager GetRegistryManager()
        {
            if ( _registryManager == null )
            {
                _registryManager = RegistryManager.CreateFromConnectionString(_config["IoTHubConnectString"]);
            }

            return _registryManager;
        }
    }
}
