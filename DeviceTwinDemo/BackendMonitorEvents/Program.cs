using CommandLine;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs;
using BackendMonitorEvent;
using System.Text;

Parameters? parameters = null;

ParserResult<Parameters> result = Parser.Default.ParseArguments<Parameters>(args)
.WithParsed(parsed =>
{
    parameters = parsed;
})
.WithNotParsed(errors =>
{
    Environment.Exit(1);
});

Console.WriteLine("演示如何监听IoT Hub事件:");

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, EventArgs) =>
{
    EventArgs.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Exiting......");
};

await ReceiveMessageFromDeviceAsync(parameters, cts.Token);

Console.WriteLine("云消息接收完毕");

static async Task ReceiveMessageFromDeviceAsync(Parameters parameters, CancellationToken token)
{
    string connectionString = parameters!.ConnectionString!;
    string consumerGroup = parameters.ConsumerGroup;

    await using var consumer = new EventHubConsumerClient(
        consumerGroup,
        connectionString,
        parameters.EventHubName
    );

    Console.WriteLine("开始监听所有分区的消息......");

    try
    {
        await foreach (PartitionEvent partitionEvent in consumer.ReadEventsAsync(token))
        {
            //分区id
            Console.WriteLine($"\nMessage received on partition {partitionEvent.Partition.PartitionId}:");

            //消息体
            string data = Encoding.UTF8.GetString(partitionEvent.Data.Body.ToArray());
            Console.WriteLine($"\tMessage body: {data}");

            //应用属性
            Console.WriteLine("\tApplication properties (set by device):");
            foreach (KeyValuePair<string, object> prop in partitionEvent.Data.Properties)
            {
                PrintProperties(prop);
            }

            //系统属性
            Console.WriteLine("\tSystem properties (set by IoT hub):");
            foreach (KeyValuePair<string, object> prop in partitionEvent.Data.SystemProperties)
            {
                PrintProperties(prop);
            }
        }


    }
    catch (TaskCanceledException ex)
    {
        Console.WriteLine(ex.ToString());
    }
}

static void PrintProperties(KeyValuePair<string, object> prop)
{
    string? propValue = prop.Value is DateTime time
            ? time.ToString("o") : prop.Value.ToString();

    Console.WriteLine($"\t\t{prop.Key}: {propValue}");
}

