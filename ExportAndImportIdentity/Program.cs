using Microsoft.Azure.Devices;

RegistryManager registryManager = RegistryManager.CreateFromConnectionString(
    @""
);

JobProperties exportJob = await registryManager.ExportDevicesAsync(
    @"",
    false
);


while(true)
{
    exportJob = await registryManager.GetJobAsync(exportJob.JobId);

    if (exportJob.Status == JobStatus.Completed ||
    exportJob.Status == JobStatus.Failed ||
    exportJob.Status == JobStatus.Cancelled)
    {
        // Job has finished executing
        break;
    }

    await Task.Delay(TimeSpan.FromSeconds(5));
}
