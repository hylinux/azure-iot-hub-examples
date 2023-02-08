using CommandLine;
using DeviceUploadFileDemo;
using Microsoft.Azure.Devices.Client;


Parameters? parameters = null;
ParserResult<Parameters> result = Parser.Default.ParseArguments<Parameters>(args)
    .WithParsed(parsedParams =>
    {
        parameters = parsedParams;
    })
    .WithNotParsed(errors =>
    {
        Environment.Exit(1);
    });

using var deviceClient = DeviceClient.CreateFromConnectionString(
    parameters!.PrimaryConnectionString,
    parameters.TransportType);
var sample = new FileUploadDemo(deviceClient);

await sample.RunSampleAsync();

await deviceClient.CloseAsync();

Console.WriteLine("Done.");