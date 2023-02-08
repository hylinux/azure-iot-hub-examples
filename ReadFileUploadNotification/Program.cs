using Microsoft.Azure.Devices;

ServiceClient serviceClient;
string connectionString = "{iot hub connection string}";

Console.WriteLine("Receive file upload notifications\n");
serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
ReceiveFileUploadNotificationAsync(serviceClient);
Console.WriteLine("Press Enter to exit\n");
Console.ReadLine();


async void ReceiveFileUploadNotificationAsync(ServiceClient serviceClient)
{
    var notificationReceiver = serviceClient.GetFileNotificationReceiver();
    Console.WriteLine("\nReceiving file upload notification from service");
    while (true)
    {
        var fileUploadNotification = await notificationReceiver!.ReceiveAsync();
        if (fileUploadNotification == null) continue;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Received file upload notification: {0}",
          string.Join(", ", fileUploadNotification.BlobName));
        Console.ResetColor();
        await notificationReceiver!.CompleteAsync(fileUploadNotification);
    }
}
