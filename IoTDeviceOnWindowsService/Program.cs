using IoTDeviceOnWindowsService;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;


IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => 
    {
        options.ServiceName = "IoT Device App";
    })
    .ConfigureServices(services =>
    {
        LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(services);

        services.AddHostedService<DeviceService>();
    })
    .ConfigureLogging( (context, logging) => {
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .Build();

await host.RunAsync();

