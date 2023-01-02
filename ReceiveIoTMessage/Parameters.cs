using CommandLine;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;

namespace ReceiveIoTMessage;

internal class Parameters
{
    [Option(
        'c',
        "ConnectionString",
        Required = true,
        HelpText = "Please input Event Hub Connection String"
    )]
    public string? ConnectionString {get; set;}

    [Option(
        'n',
        "EventHubName",
        Required = true,
        HelpText = "Please input Event Hub Name"
    )]
    public string? EventHubName {get; set;}
    
    [Option(
        'g',
        "ConsumerGroup",
        Required = false,
        HelpText = "Please input Event Hub Consumer group, the default is `$Default`"
    )]
    public string ConsumerGroup {get; set;} = EventHubConsumerClient.DefaultConsumerGroupName;


}