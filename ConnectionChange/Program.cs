﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Devices.Client;
using System.Text;
using System.Text.Json;


using IHost host = Host.CreateDefaultBuilder(args).Build();

var ConnectionString = host.Services.GetRequiredService<IConfiguration>().GetValue<string>("Device:ConnectString");

var deviceClient = DeviceClient.CreateFromConnectionString(ConnectionString, 
                                                        TransportType.Mqtt,
                                                        new ClientOptions {
                                                            SasTokenTimeToLive = TimeSpan.FromSeconds(5)
                                                        }
                                                        );
IRetryPolicy retryPolicy = new ExponentialBackoff(3, TimeSpan.FromMicroseconds(100),
  TimeSpan.FromSeconds(3), TimeSpan.FromMicroseconds(100));
deviceClient.SetRetryPolicy(retryPolicy);

deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangeHandlerAsync);

using var cts = new CancellationTokenSource();
await SendDeviceToCloudMessagesAsync(deviceClient, cts.Token);

await host.RunAsync();

void ConnectionStatusChangeHandlerAsync(ConnectionStatus status, ConnectionStatusChangeReason reason)
{
    Console.WriteLine($"Connection status changed: status={status}, reason={reason}");
}

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

            await Task.Delay(5000, ct);
        }
    }
    catch (TaskCanceledException) { } // ct was signaled
}