using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;

namespace IoTDeviceOnLinuxService;

public class DeviceService : BackgroundService
{

    private static readonly Random s_randomGenerator = new();
    private static readonly TimeSpan s_sleepDuration = TimeSpan.FromSeconds(15);

    private static readonly SemaphoreSlim s_initSemaphore = new(1, 1);
    private readonly List<string> _deviceConnectionStrings;
    private readonly TransportType _transportType;
    private static readonly ClientOptions s_clientOptions =
        new()
        {
            SdkAssignsMessageId = SdkAssignsMessageId.WhenUnset
        };


    private readonly ILogger<DeviceService> _logger;


    private static readonly HashSet<Type> s_exceptionsToBeRetried = new()
        {
            typeof(UnauthorizedException),
        };
    private readonly IRetryPolicy _customRetryPolicy;


    // Mark these fields as volatile so that their latest values are referenced.
    private static volatile DeviceClient? s_deviceClient;
    private static volatile ConnectionStatus s_connectionStatus = ConnectionStatus.Disconnected;

    private static long s_localDesiredPropertyVersion = 1;

    private static bool IsDeviceConnected => s_connectionStatus == ConnectionStatus.Connected;

    private static CancellationTokenSource? s_appCancellation;


    public DeviceService(ILogger<DeviceService> logger,
        IConfiguration config
    )
    {
        _logger = logger;

        _customRetryPolicy = new CustomRetryPolicy(s_exceptionsToBeRetried, _logger);

        _deviceConnectionStrings = new List<string>();

        _deviceConnectionStrings.Add(config.GetValue<string>("Device:ConnectionString:Primary")!);
        _deviceConnectionStrings.Add(config.GetValue<string>("Device:ConnectionString:Secondary")!);


        if (_deviceConnectionStrings == null
            || !_deviceConnectionStrings.Any())
        {
            throw new ArgumentException("At least one connection string must be provided.", nameof(_deviceConnectionStrings));
        }

        _logger.LogInformation($"Supplied with {_deviceConnectionStrings.Count} connection string(s).");

        var transportType = config.GetValue<string>("Device:Connection:TransportType");

        if (string.IsNullOrEmpty(transportType))
        {
            _transportType = TransportType.Mqtt;
        }
        else
        {
            switch (transportType.Trim().ToUpper())
            {
                case "MQTT":
                    _transportType = TransportType.Mqtt;
                    break;
                case "AMQP":
                    _transportType = TransportType.Amqp;
                    break;
                default:
                    _transportType = TransportType.Mqtt;
                    break;
            }
        }

        _logger.LogInformation($"Using {_transportType} transport.");

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        s_appCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        _logger.LogInformation($"Device App started.");

        try
        {
            await InitializeAndSetupClientAsync(s_appCancellation.Token);
            await Task.WhenAll(SendMessagesAsync(s_appCancellation.Token),
             ReceiveMessagesAsync(s_appCancellation.Token));
        }
        catch (OperationCanceledException) { } // User canceled the operation
        catch (Exception ex)
        {
            _logger.LogError($"Unrecoverable exception caught, user action is required, so exiting: \n{ex}");
            s_appCancellation.Cancel();
        }
        // Finally, close the client, but we can't use s_appCancellation because it has been signaled to quit the app.
        await s_deviceClient!.CloseAsync(CancellationToken.None);
        s_initSemaphore.Dispose();
    }

    private async Task InitializeAndSetupClientAsync(CancellationToken cancellationToken)
    {
        if (ShouldClientBeInitialized(s_connectionStatus))
        {
            // Allow a single thread to dispose and initialize the client instance.
            await s_initSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (ShouldClientBeInitialized(s_connectionStatus))
                {
                    _logger.LogDebug($"Attempting to initialize the client instance, current status={s_connectionStatus}");

                    // If the device client instance has been previously initialized, close and dispose it.
                    if (s_deviceClient != null)
                    {
                        try
                        {
                            await s_deviceClient.CloseAsync(cancellationToken);
                        }
                        catch (UnauthorizedException) { } // If the previous token is now invalid, this call may fail
                        s_deviceClient.Dispose();
                    }

                    s_deviceClient = DeviceClient.CreateFromConnectionString(_deviceConnectionStrings.First(), _transportType, s_clientOptions);
                    s_deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangeHandlerAsync);
                    s_deviceClient.SetRetryPolicy(_customRetryPolicy);
                    _logger.LogDebug("Initialized the client instance.");

                    // Force connection now.
                    // OpenAsync() is an idempotent call, it has the same effect if called once or multiple times on the same client.
                    await s_deviceClient.OpenAsync(cancellationToken);
                    _logger.LogDebug($"The client instance has been opened.");
                }
            }
            finally
            {
                s_initSemaphore.Release();
            }

            // You will need to subscribe to the client callbacks any time the client is initialized.
            await s_deviceClient!.SetDesiredPropertyUpdateCallbackAsync(HandleTwinUpdateNotificationsAsync, null, cancellationToken);
            _logger.LogDebug("The client has subscribed to desired property update notifications.");
        }
    }



    private async void ConnectionStatusChangeHandlerAsync(ConnectionStatus status, ConnectionStatusChangeReason reason)
    {
        _logger.LogDebug($"Connection status changed: status={status}, reason={reason}");
        s_connectionStatus = status;

        switch (status)
        {
            case ConnectionStatus.Connected:
                _logger.LogDebug("### The DeviceClient is CONNECTED; all operations will be carried out as normal.");

                // Call GetTwinAndDetectChangesAsync() to retrieve twin values from the server once the connection status changes into Connected.
                // This can get back "lost" twin updates in a device reconnection from status like Disconnected_Retrying or Disconnected.
                //
                // Howevever, considering how a fleet of devices connected to a hub may behave together, one must consider the implication of performing
                // work on a device (e.g., get twin) when it comes online. If all the devices go offline and then come online at the same time (for example,
                // during a servicing event) it could introduce increased latency or even throttling responses.
                // For more information, see https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-quotas-throttling#traffic-shaping.
                await GetTwinAndDetectChangesAsync(s_appCancellation!.Token);
                _logger.LogDebug("The client has retrieved twin values after the connection status changes into CONNECTED.");
                break;

            case ConnectionStatus.Disconnected_Retrying:
                _logger.LogDebug("### The DeviceClient is retrying based on the retry policy. Do NOT close or open the DeviceClient instance.");
                break;

            case ConnectionStatus.Disabled:
                _logger.LogDebug("### The DeviceClient has been closed gracefully." +
                    "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");
                break;

            case ConnectionStatus.Disconnected:
                switch (reason)
                {
                    case ConnectionStatusChangeReason.Bad_Credential:
                        // When getting this reason, the current connection string being used is not valid.
                        // If we had a backup, we can try using that.
                        _deviceConnectionStrings.RemoveAt(0);
                        if (_deviceConnectionStrings.Any())
                        {
                            _logger.LogWarning($"The current connection string is invalid. Trying another.");

                            try
                            {
                                await InitializeAndSetupClientAsync(s_appCancellation!.Token);
                            }
                            catch (OperationCanceledException) { } // User canceled

                            break;
                        }

                        _logger.LogWarning("### The supplied credentials are invalid. Update the parameters and run again.");
                        s_appCancellation!.Cancel();
                        break;

                    case ConnectionStatusChangeReason.Device_Disabled:
                        _logger.LogWarning("### The device has been deleted or marked as disabled (on your hub instance)." +
                            "\nFix the device status in Azure and then create a new device client instance.");
                        s_appCancellation!.Cancel();
                        break;

                    case ConnectionStatusChangeReason.Retry_Expired:
                        _logger.LogWarning("### The DeviceClient has been disconnected because the retry policy expired." +
                            "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

                        try
                        {
                            await InitializeAndSetupClientAsync(s_appCancellation!.Token);
                        }
                        catch (OperationCanceledException) { } // User canceled

                        break;

                    case ConnectionStatusChangeReason.Communication_Error:
                        _logger.LogWarning("### The DeviceClient has been disconnected due to a non-retry-able exception. Inspect the exception for details." +
                            "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

                        try
                        {
                            await InitializeAndSetupClientAsync(s_appCancellation!.Token);
                        }
                        catch (OperationCanceledException) { } // User canceled

                        break;

                    default:
                        _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
                        break;
                }

                break;

            default:
                _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
                break;
        }
    }


    private async Task GetTwinAndDetectChangesAsync(CancellationToken cancellationToken)
    {
        // For the following call, we execute with a retry strategy with incrementally increasing delays between retry.
        Twin twin = await s_deviceClient!.GetTwinAsync(cancellationToken);
        _logger.LogInformation($"Device retrieving twin values: {twin.ToJson()}");
        _logger.LogInformation($"Device Id: {twin.DeviceId}");
        _logger.LogInformation($"Twin Etags: {twin.ETag}");
        _logger.LogInformation($"Twin Version:{twin.Version}");

        TwinCollection twinCollection = twin.Properties.Desired;
        long serverDesiredPropertyVersion = twinCollection.Version;

        // Check if the desired property version is outdated on the local side.
        if (serverDesiredPropertyVersion > s_localDesiredPropertyVersion)
        {
            _logger.LogDebug($"The desired property version cached on local is changing from {s_localDesiredPropertyVersion} to {serverDesiredPropertyVersion}.");
            await HandleTwinUpdateNotificationsAsync(twinCollection, cancellationToken);
        }
    }

    private async Task HandleTwinUpdateNotificationsAsync(TwinCollection twinUpdateRequest, object userContext)
    {
        var reportedProperties = new TwinCollection();

        _logger.LogInformation($"Twin property update requested: \n{twinUpdateRequest.ToJson()}");

        // For the purpose of this sample, we'll blindly accept all twin property write requests.
        foreach (KeyValuePair<string, object> desiredProperty in twinUpdateRequest)
        {
            _logger.LogInformation($"Setting property {desiredProperty.Key} to {desiredProperty.Value}.");
            reportedProperties[desiredProperty.Key] = desiredProperty.Value;
        }

        s_localDesiredPropertyVersion = twinUpdateRequest.Version;
        _logger.LogDebug($"The desired property version on local is currently {s_localDesiredPropertyVersion}.");

        try
        {
            // For the purpose of this sample, we'll blindly accept all twin property write requests.
            await s_deviceClient!.UpdateReportedPropertiesAsync(reportedProperties, s_appCancellation!.Token);
        }
        catch (OperationCanceledException)
        {
            // Fail gracefully on sample exit.
        }
    }

    private async Task SendMessagesAsync(CancellationToken cancellationToken)
    {
        int messageCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsDeviceConnected)
            {
                _logger.LogInformation($"Device sending message {++messageCount} to IoT hub.");
                using Message message = PrepareMessage(messageCount);
                await s_deviceClient!.SendEventAsync(message, cancellationToken);
                _logger.LogInformation($"Device sent message {messageCount} to IoT hub.");
            }

            await Task.Delay(s_sleepDuration, cancellationToken);
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsDeviceConnected)
            {
                await Task.Delay(s_sleepDuration, cancellationToken);
                continue;
            }
            else if (_transportType == TransportType.Http1)
            {
                // The call to ReceiveAsync over HTTP completes immediately, rather than waiting up to the specified
                // time or when a cancellation token is signaled, so if we want it to poll at the same rate, we need
                // to add an explicit delay here.
                await Task.Delay(s_sleepDuration, cancellationToken);
            }

            _logger.LogInformation($"Device waiting for C2D messages from the hub for {s_sleepDuration}." +
                $"\nUse the IoT Hub Azure Portal or Azure IoT Explorer to send a message to this device.");

            await ReceiveMessageAndCompleteAsync(cancellationToken);
        }
    }

    private async Task ReceiveMessageAndCompleteAsync(CancellationToken cancellationToken)
    {
        Message? receivedMessage = null;
        try
        {
            receivedMessage = await s_deviceClient!.ReceiveAsync(cancellationToken);
        }
        catch (IotHubCommunicationException ex) when (ex.InnerException is OperationCanceledException)
        {
            _logger.LogInformation("Timed out waiting to receive a message.");
        }

        if (receivedMessage == null)
        {
            _logger.LogInformation("No message received.");
            return;
        }

        using (receivedMessage)
        {
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            var formattedMessage = new StringBuilder($"Received message '{receivedMessage.MessageId}': [{messageData}]");

            foreach (KeyValuePair<string, string> prop in receivedMessage.Properties)
            {
                formattedMessage.AppendLine($"\n\tProperty: key={prop.Key}, value={prop.Value}");
            }
            _logger.LogInformation(formattedMessage.ToString());

            try
            {
                await s_deviceClient!.CompleteAsync(receivedMessage, cancellationToken);
                _logger.LogInformation($"Completed message '{receivedMessage.MessageId}'.");
            }
            catch (DeviceMessageLockLostException)
            {
                _logger.LogWarning($"Took too long to process and complete a C2D message; it will be redelivered.");
            }
        }
    }

    private static Message PrepareMessage(int messageId)
    {
        const int temperatureThreshold = 30;

        int temperature = s_randomGenerator.Next(20, 35);
        int humidity = s_randomGenerator.Next(60, 80);
        string messagePayload = $"{{\"temperature\":{temperature},\"humidity\":{humidity}}}";

        var eventMessage = new Message(Encoding.UTF8.GetBytes(messagePayload))
        {
            MessageId = messageId.ToString(),
            ContentEncoding = Encoding.UTF8.ToString(),
            ContentType = "application/json",
        };
        eventMessage.Properties.Add("temperatureAlert", (temperature > temperatureThreshold) ? "true" : "false");

        return eventMessage;
    }




    private bool ShouldClientBeInitialized(ConnectionStatus connectionStatus)
    {
        return (connectionStatus == ConnectionStatus.Disconnected || connectionStatus == ConnectionStatus.Disabled)
            && _deviceConnectionStrings.Any();
    }


}