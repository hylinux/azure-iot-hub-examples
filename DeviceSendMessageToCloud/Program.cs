using DeviceSendMessageToCloud;
using CommandLine;
using System;
using Microsoft.Azure.Devices.Client;
using System.Text.Json;
using System.Text;

Parameters? parameters = null;

ParserResult<Parameters> result = Parser.Default.ParseArguments<Parameters>(args)
    .WithParsed( parsedParams => {
        parameters = parsedParams;
    })
    .WithNotParsed(errors => {
        Environment.Exit(1);
    });

TransportType protocol = TransportType.Mqtt;

switch(parameters!.Protocol!.ToLower())
{
    case "mqtt":
        protocol = TransportType.Mqtt;
        break;
    case "mqtt_tcp_only":
        protocol = TransportType.Mqtt_Tcp_Only;
        break;
    case "mqtt_websocket_only":
        protocol = TransportType.Mqtt_WebSocket_Only;
        break;
    case "amqp":
        protocol = TransportType.Amqp;
        break;
    case "amqp_tcp_only":
        protocol = TransportType.Amqp_Tcp_Only;
        break;
    case "amqp_websocket_only":
        protocol = TransportType.Amqp_WebSocket_Only;
        break;
    case "http":
        protocol = TransportType.Http1;
        break;
    default:
        protocol = TransportType.Mqtt;
        break;
}

DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(parameters.ConnectionString, protocol);

Console.WriteLine("按 Ctrl - C 退出应用。");
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, EventArgs) => 
{
    EventArgs.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Exit......");
};

await SendDeviceToCloudMessageAsync(deviceClient, cts.Token);

await deviceClient.CloseAsync();

Console.WriteLine("Device App Exiting......");


static async Task SendDeviceToCloudMessageAsync(DeviceClient deviceClient, CancellationToken token)
{
    //假设我们设备中向云发送两项监控数据
    //温度和湿度
    double minTemperature = 20;
    double minHumidity = 60;

    var rand = new Random();

    try 
    {
        while ( !token.IsCancellationRequested )
        {
            double currentTemperature = minTemperature + rand.NextDouble() * 5; //模拟生成当前的温度.
            double currentHumidity = minHumidity + rand.NextDouble() * 20; //模拟生成当前的湿度

            //开始创建一个基于`Json`的消息体
            string messageBody = JsonSerializer.Serialize(
                new 
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                }
            );

            //在基于Json的消息体中，一定要给消息设置contenttype和encoding
            using var message = new Message( 
                Encoding.ASCII.GetBytes(messageBody))
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                };
            
            //在生成了消息体之后，消息还可以设置自定义的`Application Properties`, 即应用属性。
            //在消息的应用属性里设置一个温度告警.
            message.Properties.Add("temperatureAlert", (currentTemperature> 30 )? "true":"false");

            //发送消息
            await deviceClient.SendEventAsync(message, token);
            Console.WriteLine($"{DateTime.Now} > Sending message: {messageBody}");

            await Task.Delay(1000, token);
        }
    }
    catch ( TaskCanceledException e )
    {
        Console.WriteLine(e.ToString());
    }


}