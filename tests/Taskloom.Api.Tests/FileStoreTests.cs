using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>The local file store keeps every key inside its root, compared by whole path segments.</summary>
public class FileStoreTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"taskloom_store_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
    }

    [Theory]
    [InlineData("files")]
    [InlineData("files/")]
    [InlineData("files/./")]
    public async Task Local_store_keys_stay_inside_the_root(string rootName)
    {
        var root = Path.Combine(_parent, rootName);
        var store = new LocalDiskFileStore(root);

        await store.SaveAsync("tasks/a/b", new MemoryStream("inside"u8.ToArray()), "text/plain");
        Assert.True(File.Exists(Path.Combine(_parent, "files", "tasks", "a", "b")));
        await using (var read = await store.OpenReadAsync("tasks/a/b"))
            Assert.Equal("inside", await new StreamReader(read!).ReadToEndAsync());
        Assert.Null(await store.OpenReadAsync("tasks/a/missing"));

        // A sibling folder that merely starts with the root's name, the root itself, and anything above it are refused.
        foreach (var key in new[] { "../files-old/x", "../files2/x", "../x", "tasks/../../x", "", ".", "tasks/..", "/etc/passwd", Path.Combine(_parent, "files-old", "x") })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(key, new MemoryStream([1]), "text/plain"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key));
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key));
        }
        Assert.False(Directory.Exists(Path.Combine(_parent, "files-old")));
        Assert.False(Directory.Exists(Path.Combine(_parent, "files2")));
        Assert.False(File.Exists(Path.Combine(_parent, "x")));

        await store.DeleteAsync("tasks/a/b");
        Assert.False(File.Exists(Path.Combine(_parent, "files", "tasks", "a", "b")));
        // Deleting what isn't there is fine.
        await store.DeleteAsync("tasks/a/b");
    }
}
