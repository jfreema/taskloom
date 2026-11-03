using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskloom.Application;

namespace Taskloom.Cloud.Azure;

public static class AzureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Blob Storage as the file store.
    /// Uses FileStore:Azure:ConnectionString when set (Azurite locally), otherwise
    /// FileStore:Azure:AccountUrl with the container app's managed identity.
    /// </summary>
    public static IServiceCollection AddAzureBlobFileStore(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("FileStore:Azure");
        var containerName = section["Container"] ?? "attachments";
        var connectionString = section["ConnectionString"];
        var accountUrl = section["AccountUrl"];

        BlobServiceClient serviceClient = !string.IsNullOrWhiteSpace(connectionString)
            ? new BlobServiceClient(connectionString)
            : new BlobServiceClient(new Uri(accountUrl ?? throw new InvalidOperationException(
                "Set FileStore:Azure:ConnectionString or FileStore:Azure:AccountUrl")), new DefaultAzureCredential());

        services.AddSingleton<IFileStore>(new AzureBlobFileStore(serviceClient.GetBlobContainerClient(containerName)));
        return services;
    }

    /// <summary>Sends OpenTelemetry traces, metrics and logs to Application Insights when a connection string is set.</summary>
    public static IServiceCollection AddAzureMonitorIfConfigured(this IServiceCollection services, IConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            services.AddOpenTelemetry().UseAzureMonitor();
        return services;
    }
}
