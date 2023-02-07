using BackendApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;

namespace BackendApp.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly ServiceClient _serviceClient;
    private readonly RegistryManager _registryManager;

    public List<Twin> twins { get; set; } = new List<Twin>();


    public IndexModel(ILogger<IndexModel> logger, IoTServiceClientService ioTServiceClientService)
    {
        _logger = logger;
        _serviceClient = ioTServiceClientService.GetServiceClient();
        _registryManager = ioTServiceClientService.GetRegistryManager();

    }

    public async Task OnGet()
    {
        var query = _registryManager.CreateQuery("select * from devices", 100);

        while ( query.HasMoreResults )
        {
            var page = await query.GetNextAsTwinAsync();

            foreach ( var twin in page )
            {
                twins.Add( twin );
            }
        }
    }
}
