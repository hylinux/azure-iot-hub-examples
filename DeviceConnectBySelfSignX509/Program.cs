using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Devices.Client;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;


using IHost host = Host.CreateDefaultBuilder(args).Build();

var iotHubHostURL = host.Services.GetRequiredService<IConfiguration>().GetValue<string>("Device:IoTHubHostURL");
var certPassword = host.Services.GetRequiredService<IConfiguration>().GetValue<string>("X509:Password");
var deviceId = host.Services.GetRequiredService<IConfiguration>().GetValue<string>("Device:Id");

var cert = new X509Certificate2(@"D:\MyProjects\azure-iot-hub-examples\DeviceConnectBySelfSignX509\X509\device.pfx", certPassword);

var auth = new DeviceAuthenticationWithX509Certificate(deviceId, cert);

var deviceClient = DeviceClient.Create(iotHubHostURL, auth, TransportType.Mqtt);

Console.WriteLine("设备连接正常。");

using var cts = new CancellationTokenSource();
await SendDeviceToCloudMessagesAsync(deviceClient, cts.Token);

await host.RunAsync();


static async Task SendDeviceToCloudMessagesAsync(DeviceClient deviceClient, CancellationToken ct)
{
    double minTemperature = 20;
    double minHumidity = 60;
    var rand = new Random();

    try
    {
        while (!ct.IsCancellationRequested)
        {
            double currentTemperature = minTemperature + rand.NextDouble() * 15;
            double currentHumidity = minHumidity + rand.NextDouble() * 20;

            // Create JSON message
            string messageBody = JsonSerializer.Serialize(
                new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                });
            using var message = new Message(Encoding.ASCII.GetBytes(messageBody))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8",
            };

            // Add a custom application property to the message.
            // An IoT hub can filter on these properties without access to the message body.
            message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

            // Send the telemetry message
            await deviceClient.SendEventAsync(message, ct);
            Console.WriteLine($"{DateTime.Now} > Sending message: {messageBody}");

            await Task.Delay(1000, ct);
        }
    }
    catch (TaskCanceledException) { } // ct was signaled
}