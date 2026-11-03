using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Taskloom.Application;

namespace Taskloom.Cloud.Azure;

public class AzureBlobFileStore(BlobContainerClient container) : IFileStore
{
    private bool _ensured;

    private async Task EnsureAsync(CancellationToken ct)
    {
        if (_ensured) return;
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        _ensured = true;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureAsync(ct);
        await container.GetBlobClient(key).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        await EnsureAsync(ct);
        var blob = container.GetBlobClient(key);
        if (!await blob.ExistsAsync(ct)) return null;
        return await blob.OpenReadAsync(cancellationToken: ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await EnsureAsync(ct);
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
    }
}
