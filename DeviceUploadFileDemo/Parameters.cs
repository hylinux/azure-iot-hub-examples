using CommandLine;
using Microsoft.Azure.Devices.Client;

namespace DeviceUploadFileDemo;

internal class Parameters
{
    [Option(
        'c',
        "PrimaryConnectionString",
        Required = true,
        HelpText = "The primary connection string for the device to simulate.")]
    public string? PrimaryConnectionString { get; set; }

    [Option(
        't',
        "TransportType",
        Default = TransportType.Mqtt,
        Required = false,
        HelpText = "The transport to use to communicate with the IoT Hub. Possible values include Mqtt, Mqtt_WebSocket_Only, Mqtt_Tcp_Only, Amqp, Amqp_WebSocket_Only, Amqp_Tcp_only, and Http1.")]
    public TransportType TransportType { get; set; }
}
