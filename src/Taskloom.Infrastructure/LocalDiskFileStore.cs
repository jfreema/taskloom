using Taskloom.Application;

namespace Taskloom.Infrastructure;

/// <summary>Stores files under a folder on disk. For local development only.</summary>
public class LocalDiskFileStore(string root) : IFileStore
{
    /// <summary>
    /// The root as a full path ending with a separator, so the check below compares whole path segments: a root of
    /// "/data/files" must not admit "/data/files-old/x".
    /// </summary>
    private readonly string _root = WithTrailingSeparator(Path.GetFullPath(root));

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (full.Length <= _root.Length || !full.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key", nameof(key));
        return full;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
