using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// What an image's own bytes say it is. Only the four formats every browser shows are read; anything else (SVG, HTML
/// pretending to be a picture, a truncated file) is null, so a stored photo can only ever be drawn as an image.
/// </summary>
public sealed record ImageInfo(string ContentType, int Width, int Height)
{
    public static ImageInfo? Read(ReadOnlySpan<byte> b)
    {
        try
        {
            if (b.Length >= 24 && b[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && b.Slice(12, 4).SequenceEqual("IHDR"u8))
                return Sized("image/png", BinaryPrimitives.ReadInt32BigEndian(b[16..]), BinaryPrimitives.ReadInt32BigEndian(b[20..]));
            if (b.Length >= 10 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8)))
                return Sized("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
            if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return WebP(b);
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return Jpeg(b);
        }
        catch (ArgumentOutOfRangeException)
        {
            // A header that claims more than the file holds.
        }
        return null;
    }

    private static ImageInfo? Sized(string type, int width, int height) => width > 0 && height > 0 ? new(type, width, height) : null;

    private static ImageInfo? WebP(ReadOnlySpan<byte> b)
    {
        var chunk = b.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
            return Sized("image/webp", 1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        if (chunk.SequenceEqual("VP8 "u8) && b[23] == 0x9D && b[24] == 0x01 && b[25] == 0x2A)
            return Sized("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8) && b[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
            return Sized("image/webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        return null;
    }

    /// <summary>Walks the JPEG's segments to the first start-of-frame, which holds the size.</summary>
    private static ImageInfo? Jpeg(ReadOnlySpan<byte> b)
    {
        var i = 2;
        while (i + 3 < b.Length)
        {
            if (b[i] != 0xFF) return null;
            var marker = b[i + 1];
            if (marker == 0xFF) { i++; continue; }
            // Markers without a length: TEM and the restart markers.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            if (marker is 0xD9 or 0xDA) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
            if (length < 2) return null;
            // SOF0 to SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                return Sized("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
            i += 2 + length;
        }
        return null;
    }
}

/// <summary>
/// People's photos (Look 2): kept in the database (user_photos), served by GET /api/users/{id}/photo to people who share
/// a workspace with them, and named by a version in the URL so a browser fetches each photo once.
/// </summary>
public static class UserPhotos
{
    public const int MaxBytes = 512 * 1024;
    public const int MaxSide = 2048;
    public const string UploadSource = "upload";

    public const string TooLargeMessage = "Photos can be up to 512 KB.";
    public const string NotAnImageMessage = "Choose a JPEG, PNG, WebP or GIF image.";
    public const string TooBigMessage = "Photos can be up to 2048 pixels wide and tall.";

    /// <summary>Where a person's photo is, or null when they have none.</summary>
    public static string? UrlFor(Guid userId, string? version) => version is null ? null : $"/api/users/{userId:D}/photo?v={version}";

    /// <summary>The image the bytes hold, or the message that says why they can't be a photo.</summary>
    public static (ImageInfo? Image, string? Error) Check(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes) return (null, TooLargeMessage);
        if (ImageInfo.Read(data) is not { } image) return (null, NotAnImageMessage);
        if (image.Width > MaxSide || image.Height > MaxSide) return (null, TooBigMessage);
        return (image, null);
    }

    public static string VersionOf(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data))[..16];

    /// <summary>Makes <paramref name="data"/> the user's photo (saved by the caller's SaveChanges). Unchanged bytes change nothing.</summary>
    public static async Task SetAsync(TaskloomDbContext db, User user, byte[] data, ImageInfo image, string source, DateTimeOffset now, CancellationToken ct = default)
    {
        var version = VersionOf(data);
        var row = await db.UserPhotos.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (row is null)
            db.UserPhotos.Add(new UserPhoto { UserId = user.Id, ContentType = image.ContentType, Data = data, Source = source, UpdatedAt = now });
        else if (user.PhotoVersion != version || row.Source != source)
        {
            row.ContentType = image.ContentType;
            row.Data = data;
            row.Source = source;
            row.UpdatedAt = now;
        }
        user.PhotoVersion = version;
    }

    /// <summary>Takes the user's photo away (saved by the caller's SaveChanges).</summary>
    public static async Task RemoveAsync(TaskloomDbContext db, User user, CancellationToken ct = default)
    {
        var row = await db.UserPhotos.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (row is not null) db.UserPhotos.Remove(row);
        user.PhotoVersion = null;
    }
}

/// <summary>What a provider said about the person's photo at sign-in.</summary>
public abstract record ProviderPhoto
{
    /// <summary>The provider sent a photo that passed <see cref="UserPhotos.Check"/>.</summary>
    public sealed record Found(byte[] Data, ImageInfo Image) : ProviderPhoto;
    /// <summary>The provider says the person has no photo (Microsoft Graph's 404).</summary>
    public sealed record None : ProviderPhoto;
    /// <summary>Nothing to go on: no photo offered, the request failed or timed out, or the bytes weren't a usable image.</summary>
    public sealed record Unknown : ProviderPhoto;
}

/// <summary>
/// Fetches a person's photo from the provider they sign in with, so avatars show it (Look 2). Google puts a photo URL
/// in the ID token's "picture" claim; Microsoft keeps the photo in Graph, read with the sign-in's access token. A slow
/// or failing provider never holds up or breaks a sign-in: anything but a photo or a clear "none" is
/// <see cref="ProviderPhoto.Unknown"/>, and the stored photo stays as it was.
/// </summary>
public sealed partial class ProviderPhotos(IHttpClientFactory http, ILogger<ProviderPhotos> log)
{
    public const string ClientName = "provider-photos";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    public const string GraphPhotoUrl = "https://graph.microsoft.com/v1.0/me/photos/240x240/$value";

    [GeneratedRegex(@"=s\d+(-c)?$")]
    private static partial Regex GoogleSizeSuffix();

    /// <summary>
    /// The URL to fetch for a Google "picture" claim, asking for 256 pixels, or null for anything not on Google's own
    /// image host over https.
    /// </summary>
    public static Uri? GooglePhotoUri(string? picture)
    {
        if (!Uri.TryCreate(picture, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return null;
        var host = uri.IdnHost;
        if (host != "googleusercontent.com" && !host.EndsWith(".googleusercontent.com", StringComparison.Ordinal)) return null;
        var sized = GoogleSizeSuffix().Replace(uri.GetLeftPart(UriPartial.Path), "=s256-c");
        return Uri.TryCreate(sized + uri.Query, UriKind.Absolute, out var u) ? u : null;
    }

    public async Task<ProviderPhoto> FetchAsync(SignInProvider provider, ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        try
        {
            if (provider.Key == SignInProvider.MicrosoftKey)
            {
                if (string.IsNullOrEmpty(accessToken)) return new ProviderPhoto.Unknown();
                var request = new HttpRequestMessage(HttpMethod.Get, GraphPhotoUrl);
                request.Headers.Authorization = new("Bearer", accessToken);
                return await SendAsync(request, ct);
            }
            if (provider.IsGoogleAuthority && GooglePhotoUri(principal.FindFirst("picture")?.Value) is { } uri)
                return await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), ct);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Couldn't fetch the photo from {Provider}: {Message}", provider.DisplayName, e.Message);
        }
        return new ProviderPhoto.Unknown();
    }

    private async Task<ProviderPhoto> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        using var request = message;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var client = http.CreateClient(ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new ProviderPhoto.None();
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > UserPhotos.MaxBytes) return new ProviderPhoto.Unknown();

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > UserPhotos.MaxBytes) return new ProviderPhoto.Unknown();
        }
        var data = buffer.ToArray();
        var (image, error) = UserPhotos.Check(data);
        if (image is null)
        {
            log.LogInformation("The sign-in photo was not used: {Reason}", error);
            return new ProviderPhoto.Unknown();
        }
        return new ProviderPhoto.Found(data, image);
    }

    /// <summary>
    /// After a successful sign-in: takes the provider's photo unless the person chose their own (or none) by hand.
    /// Saves its own changes; never throws for a photo problem.
    /// </summary>
    public async Task RefreshAsync(TaskloomDbContext db, Guid userId, SignInProvider provider, ClaimsPrincipal principal, string? accessToken, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null || user.PhotoChosen) return;
            switch (await FetchAsync(provider, principal, accessToken, ct))
            {
                case ProviderPhoto.Found found:
                    await UserPhotos.SetAsync(db, user, found.Data, found.Image, provider.Key, now, ct);
                    break;
                case ProviderPhoto.None when user.PhotoVersion is not null:
                    // Only a photo that came from this provider goes; one from another provider stays.
                    var source = await db.UserPhotos.Where(p => p.UserId == userId).Select(p => p.Source).FirstOrDefaultAsync(ct);
                    if (source == provider.Key) await UserPhotos.RemoveAsync(db, user, ct);
                    break;
                default:
                    return;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Couldn't save the photo from {Provider}", provider.DisplayName);
        }
    }
}
