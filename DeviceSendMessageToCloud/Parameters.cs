using CommandLine;
using System;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Logging;


namespace DeviceSendMessageToCloud;

internal class Parameters 
{
    [
        Option(
            'p',
            "protocol",
            HelpText = "Please setup the connection Protocol",
            Required = true,
            Default = "mqtt"
        )
    ]
    public string? Protocol {get; set;}  = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVICE_CONNECTION_PROTOCOL"))?
        Environment.GetEnvironmentVariable("DEVICE_CONNECTION_PROTOCOL"): "mqtt";


    [
        Option(
            'c',
            "ConnectionString",
            HelpText = "Device need a Connection String which help them connect to Cloud",
            Required = true
        )
    ]
    public string? ConnectionString {get; set;} = Environment.GetEnvironmentVariable("DEVICE_CONNECTION_STRING");

    public bool Validate(ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            logger.LogError("please setup ConnectionString");
            return false;
        }
        else
        {
            return true;
        }
    }
}