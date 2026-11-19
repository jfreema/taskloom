using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Made-up images: just enough of each format's header for <see cref="ImageInfo"/>, then filler that tells them apart.</summary>
public static class TestImages
{
    /// <summary>A real 1×1 PNG.</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public static byte[] Png(int width, int height, byte salt = 0)
    {
        var b = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        b.AddRange("IHDR"u8.ToArray());
        b.AddRange(BigEndian(width));
        b.AddRange(BigEndian(height));
        b.AddRange([8, 6, 0, 0, 0, salt]);
        return [.. b];
    }

    public static byte[] Jpeg(int width, int height, byte salt = 0)
    {
        // SOI, an APP0 (JFIF) segment to skip, then SOF0 with the size.
        byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8.ToArray(), 1, 1, 0, 0, 1, 0, 1, 0, salt];
        byte[] sof0 = [0xFF, 0xC0, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1];
        return [0xFF, 0xD8, .. app0, .. sof0, 0xFF, 0xD9];
    }

    public static byte[] Gif(int width, int height) =>
        [.. "GIF89a"u8.ToArray(), (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0, 0, 0];

    public static byte[] WebPExtended(int width, int height)
    {
        byte[] b = [.. "RIFF"u8.ToArray(), 22, 0, 0, 0, .. "WEBP"u8.ToArray(), .. "VP8X"u8.ToArray(), 10, 0, 0, 0, 0, 0, 0, 0,
            (byte)(width - 1), (byte)((width - 1) >> 8), (byte)((width - 1) >> 16),
            (byte)(height - 1), (byte)((height - 1) >> 8), (byte)((height - 1) >> 16)];
        return b;
    }

    public static byte[] WebPLossy(int width, int height) =>
        [.. "RIFF"u8.ToArray(), 22, 0, 0, 0, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray(), 10, 0, 0, 0, 0, 0, 0, 0x9D, 0x01, 0x2A,
            (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)];

    public static byte[] WebPLossless(int width, int height)
    {
        var bits = (uint)(width - 1) | (uint)(height - 1) << 14;
        return [.. "RIFF"u8.ToArray(), 22, 0, 0, 0, .. "WEBP"u8.ToArray(), .. "VP8L"u8.ToArray(), 10, 0, 0, 0, 0x2F,
            (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24), 0, 0, 0, 0, 0];
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
}

/// <summary>Look 2: what <see cref="ImageInfo"/> accepts as a photo.</summary>
public class ImageInfoTests
{
    [Fact]
    public void Reads_the_type_and_size_of_the_four_photo_formats()
    {
        Assert.Equal(new ImageInfo("image/png", 1, 1), ImageInfo.Read(TestImages.TinyPng));
        Assert.Equal(new ImageInfo("image/png", 300, 200), ImageInfo.Read(TestImages.Png(300, 200)));
        Assert.Equal(new ImageInfo("image/jpeg", 256, 240), ImageInfo.Read(TestImages.Jpeg(256, 240)));
        Assert.Equal(new ImageInfo("image/gif", 48, 64), ImageInfo.Read(TestImages.Gif(48, 64)));
        Assert.Equal(new ImageInfo("image/webp", 1024, 768), ImageInfo.Read(TestImages.WebPExtended(1024, 768)));
        Assert.Equal(new ImageInfo("image/webp", 240, 120), ImageInfo.Read(TestImages.WebPLossy(240, 120)));
        Assert.Equal(new ImageInfo("image/webp", 333, 222), ImageInfo.Read(TestImages.WebPLossless(333, 222)));
    }

    [Fact]
    public void Anything_else_is_not_a_photo()
    {
        foreach (var bytes in new[]
        {
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(),
            "<html><script>alert(1)</script></html>"u8.ToArray(),
            "GIF8"u8.ToArray(),
            TestImages.Png(10, 10)[..20],
            TestImages.Jpeg(10, 10)[..12],
            TestImages.Png(0, 10),
            [0xFF, 0xD8, 0xFF, 0xD9],
            [],
        })
            Assert.Null(ImageInfo.Read(bytes));
    }

    [Fact]
    public void Photos_are_limited_in_bytes_and_pixels()
    {
        Assert.Equal((new ImageInfo("image/png", 2048, 2048), (string?)null), UserPhotos.Check(TestImages.Png(2048, 2048)));
        Assert.Equal("Photos can be up to 2048 pixels wide and tall.", UserPhotos.Check(TestImages.Png(2049, 10)).Error);
        Assert.Equal("Photos can be up to 2048 pixels wide and tall.", UserPhotos.Check(TestImages.Jpeg(10, 4000)).Error);
        Assert.Equal("Photos can be up to 512 KB.", UserPhotos.Check([.. TestImages.Png(10, 10), .. new byte[512 * 1024]]).Error);
        Assert.Equal("Choose a JPEG, PNG, WebP or GIF image.", UserPhotos.Check("hello"u8).Error);
    }

    [Theory]
    [InlineData("https://lh3.googleusercontent.com/a/ACg8ocK=s96-c", "https://lh3.googleusercontent.com/a/ACg8ocK=s256-c")]
    [InlineData("https://lh3.googleusercontent.com/a/ACg8ocK", "https://lh3.googleusercontent.com/a/ACg8ocK")]
    [InlineData("https://lh5.googleusercontent.com/-abc/photo.jpg?sz=50", "https://lh5.googleusercontent.com/-abc/photo.jpg?sz=50")]
    [InlineData("http://lh3.googleusercontent.com/a/x=s96-c", null)]
    [InlineData("https://lh3.googleusercontent.com:8443/a/x", null)]
    [InlineData("https://evil.example/a/x=s96-c", null)]
    [InlineData("https://googleusercontent.com.evil.example/a", null)]
    [InlineData("https://notgoogleusercontent.com/a", null)]
    [InlineData("/relative", null)]
    [InlineData("", null)]
    public void Google_photos_come_only_from_googles_image_host_at_256_pixels(string picture, string? expected) =>
        Assert.Equal(expected, ProviderPhotos.GooglePhotoUri(picture)?.AbsoluteUri);
}

/// <summary>Look 2: uploading, removing and serving photos, and who can see them.</summary>
public class PhotoTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    public static async Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, byte[] bytes, string contentType = "image/jpeg", string fileName = "photo.jpg")
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        return await client.PostAsync("/api/me/photo", form);
    }

    [Fact]
    public async Task Uploading_a_photo_shows_it_to_people_who_share_a_workspace_and_nobody_else()
    {
        var owner = await factory.NewUserAsync();
        var (member, memberInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var outsider = await factory.NewUserAsync();

        var before = await (await owner.Client.GetAsync("/api/me")).ReadAsync<MeDto>();
        Assert.Equal(((string?)null, false), (before.PhotoUrl, before.PhotoChosen));

        var jpeg = TestImages.Jpeg(256, 256, salt: 1);
        // The type comes from the bytes, not from what the browser said.
        var me = await (await UploadPhotoAsync(owner.Client, jpeg, contentType: "text/html")).ReadAsync<MeDto>();
        var version = UserPhotos.VersionOf(jpeg);
        Assert.Equal($"/api/users/{owner.Me.Id}/photo?v={version}", me.PhotoUrl);
        Assert.True(me.PhotoChosen);
        Assert.Equal(me, await (await owner.Client.GetAsync("/api/me")).ReadAsync<MeDto>());

        // Everyone in the workspace sees where it is, and can fetch it; browsers keep that version for good.
        var members = await member.MembersAsync(owner.Workspace.Id);
        Assert.Equal(me.PhotoUrl, members.Single(m => m.UserId == owner.Me.Id).PhotoUrl);
        Assert.Null(members.Single(m => m.UserId == memberInfo.UserId).PhotoUrl);
        foreach (var client in new[] { owner.Client, member })
        {
            var res = await client.GetAsync(me.PhotoUrl);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("image/jpeg", res.Content.Headers.ContentType?.MediaType);
            Assert.Equal(jpeg, await res.Content.ReadAsByteArrayAsync());
            Assert.Equal((true, true, TimeSpan.FromDays(365)), (res.Headers.CacheControl!.Private, res.Headers.CacheControl.Extensions.Any(e => e.Name == "immutable"), res.Headers.CacheControl.MaxAge!.Value));
            Assert.Equal($"\"{version}\"", res.Headers.ETag?.Tag);
            Assert.Equal("sandbox", res.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        }

        // An old link answers the current photo, but it must be checked every time; an unchanged one is a 304.
        var stale = await member.GetAsync($"/api/users/{owner.Me.Id}/photo?v=0000000000000000");
        Assert.Equal((HttpStatusCode.OK, true, true, (TimeSpan?)null), (stale.StatusCode, stale.Headers.CacheControl!.Private, stale.Headers.CacheControl.NoCache, stale.Headers.CacheControl.MaxAge));
        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/users/{owner.Me.Id}/photo");
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        Assert.Equal(HttpStatusCode.NotModified, (await member.SendAsync(conditional)).StatusCode);

        // Someone who shares no workspace can't tell it exists; nor can anyone fetch a photo that isn't there.
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync(me.PhotoUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/users/{memberInfo.UserId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/users/{Guid.NewGuid()}/photo")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(me.PhotoUrl)).StatusCode);
    }

    [Fact]
    public async Task Photos_come_with_people_everywhere_the_api_names_them()
    {
        var owner = await factory.NewUserAsync();
        var (member, memberInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Mo Member");
        var ownerUrl = (await (await UploadPhotoAsync(owner.Client, TestImages.Png(64, 64, 2), "image/png")).ReadAsync<MeDto>()).PhotoUrl;
        var memberUrl = (await (await UploadPhotoAsync(member, TestImages.Gif(64, 64), "image/gif")).ReadAsync<MeDto>()).PhotoUrl;
        Assert.NotNull(ownerUrl);
        Assert.NotNull(memberUrl);

        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "With faces" });
        await member.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "<p>Hi</p>" });
        await member.PatchTaskAsync(task.Id, new { priority = "High" });

        var detail = await owner.Client.TaskDetailAsync(task.Id);
        Assert.Equal(ownerUrl, detail.CreatedBy.PhotoUrl);
        Assert.Equal(memberUrl, detail.Comments.Single().Author.PhotoUrl);
        Assert.Equal(memberUrl, detail.Activity.Single().Actor.PhotoUrl);
        var list = await (await owner.Client.GetAsync($"/api/lists/{owner.List.Id}")).ReadAsync<ListDetailDto>();
        Assert.Equal(memberUrl, list.Members.Single(m => m.Id == memberInfo.UserId).PhotoUrl);
        var comment = await (await member.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "<p>Again</p>" })).ReadAsync<CommentDto>();
        Assert.Equal(memberUrl, comment.Author.PhotoUrl);
    }

    [Fact]
    public async Task Refused_uploads_change_nothing()
    {
        var user = await factory.NewUserAsync();
        var kept = await (await UploadPhotoAsync(user.Client, TestImages.Jpeg(200, 200, 3))).ReadAsync<MeDto>();

        Assert.Equal("Choose a JPEG, PNG, WebP or GIF image.", await (await UploadPhotoAsync(user.Client, "<svg/>"u8.ToArray(), "image/svg+xml")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose a JPEG, PNG, WebP or GIF image.", await (await UploadPhotoAsync(user.Client, [], "image/png")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Photos can be up to 2048 pixels wide and tall.", await (await UploadPhotoAsync(user.Client, TestImages.Png(4000, 4000), "image/png")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Photos can be up to 512 KB.", await (await UploadPhotoAsync(user.Client, [.. TestImages.Png(10, 10), .. new byte[UserPhotos.MaxBytes]], "image/png")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Photos can be up to 512 KB.", await (await UploadPhotoAsync(user.Client, new byte[3 * 1024 * 1024], "image/png")).ProblemAsync(HttpStatusCode.RequestEntityTooLarge));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await user.Client.PostAsync("/api/me/photo", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

        Assert.Equal(kept, await (await user.Client.GetAsync("/api/me")).ReadAsync<MeDto>());
    }

    [Fact]
    public async Task Removing_the_photo_goes_back_to_initials()
    {
        var user = await factory.NewUserAsync();
        var url = (await (await UploadPhotoAsync(user.Client, TestImages.Jpeg(100, 100, 4))).ReadAsync<MeDto>()).PhotoUrl!;

        var me = await (await user.Client.DeleteAsync("/api/me/photo")).ReadAsync<MeDto>();
        Assert.Equal(((string?)null, true), (me.PhotoUrl, me.PhotoChosen));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync(url)).StatusCode);
        Assert.False(await factory.DbAsync(db => db.UserPhotos.AnyAsync(p => p.UserId == user.Me.Id)));
        // Removing again is harmless.
        Assert.Equal(me, await (await user.Client.DeleteAsync("/api/me/photo")).ReadAsync<MeDto>());
        // The CSRF header is still required.
        using var bare = factory.CreateClient(new() { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.DeleteAsync("/api/me/photo")).StatusCode);
    }
}

/// <summary>A fake for the providers' photo hosts: records each request and answers with <see cref="Respond"/>.</summary>
public sealed class FakePhotoHost : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Image(byte[] bytes, string type) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue(type) } } };
}

public class PhotoSignInFactory : SignInFactory
{
    public FakePhotoHost Host { get; } = new();

    public PhotoSignInFactory()
    {
        var oidc = TestServices;
        TestServices = services =>
        {
            oidc?.Invoke(services);
            services.AddHttpClient(ProviderPhotos.ClientName).ConfigurePrimaryHttpMessageHandler(() => Host);
        };
    }
}

/// <summary>Look 2: the photo a provider gives at sign-in, and when a person's own choice wins over it.</summary>
public class ProviderPhotoTests(PhotoSignInFactory factory) : IClassFixture<PhotoSignInFactory>
{
    private const string Picture = "https://lh3.googleusercontent.com/a/ACg8ocJ=s96-c";

    private Task<TokenOutcome> GoogleAsync(string email, string subject, string? picture = Picture)
    {
        var claims = new List<(string, string)> { ("sub", subject), ("email", email), ("email_verified", "true") };
        if (picture is not null) claims.Add(("picture", picture));
        return factory.ValidateTokenAsync("google", [.. claims]);
    }

    private Task<TokenOutcome> MicrosoftAsync(string email, string subject, string? accessToken = "graph-token") =>
        factory.ValidateTokenAsync("microsoft", accessToken, ("oid", subject), ("tid", SignInFactory.Tenant.ToString()), ("email", email));

    private Task<(string? Version, bool Chosen, string? Source)> PhotoOfAsync(Guid userId) =>
        factory.DbAsync(async db => await db.Users.Where(u => u.Id == userId)
            .Select(u => new ValueTuple<string?, bool, string?>(u.PhotoVersion, u.PhotoChosen, db.UserPhotos.Where(p => p.UserId == u.Id).Select(p => p.Source).FirstOrDefault()))
            .SingleAsync());

    private static string Email() => $"p{Guid.NewGuid():N}@{SignInFactory.AllowedDomain}";

    [Fact]
    public async Task Microsoft_asks_for_graph_access_only_for_the_photo()
    {
        Assert.Contains("User.Read", factory.OidcOptions("microsoft").Scope);
        Assert.DoesNotContain("User.Read", factory.OidcOptions("google").Scope);
    }

    [Fact]
    public async Task Google_sign_in_brings_the_google_photo_at_256_pixels_and_keeps_it_current()
    {
        var first = TestImages.Jpeg(256, 256, 10);
        factory.Host.Requests.Clear();
        factory.Host.Respond = _ => FakePhotoHost.Image(first, "image/jpeg");
        var subject = $"g-{Guid.NewGuid():N}";
        var email = Email();
        var outcome = await GoogleAsync(email, subject);

        var request = Assert.Single(factory.Host.Requests);
        Assert.Equal("https://lh3.googleusercontent.com/a/ACg8ocJ=s256-c", request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        var userId = outcome.UserId!.Value;
        Assert.Equal((UserPhotos.VersionOf(first), false, "google"), await PhotoOfAsync(userId));

        // A new photo at Google replaces it at the next sign-in; a failed fetch or a non-image leaves it.
        var second = TestImages.Png(256, 256, 11);
        factory.Host.Respond = _ => FakePhotoHost.Image(second, "image/png");
        await GoogleAsync(email, subject);
        Assert.Equal((UserPhotos.VersionOf(second), false, "google"), await PhotoOfAsync(userId));
        factory.Host.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        await GoogleAsync(email, subject);
        factory.Host.Respond = _ => FakePhotoHost.Image("<svg/>"u8.ToArray(), "image/png");
        await GoogleAsync(email, subject);
        factory.Host.Respond = _ => throw new HttpRequestException("offline");
        Assert.NotNull((await GoogleAsync(email, subject)).Principal);
        Assert.Equal((UserPhotos.VersionOf(second), false, "google"), await PhotoOfAsync(userId));

        // A picture anywhere but Google's image host is never fetched.
        factory.Host.Requests.Clear();
        await GoogleAsync(email, subject, picture: "https://evil.example/me.png");
        await GoogleAsync(email, subject, picture: null);
        Assert.Empty(factory.Host.Requests);
        Assert.Equal((UserPhotos.VersionOf(second), false, "google"), await PhotoOfAsync(userId));
    }

    [Fact]
    public async Task A_photo_chosen_by_hand_wins_over_the_sign_in_photo()
    {
        factory.Host.Respond = _ => FakePhotoHost.Image(TestImages.Jpeg(256, 256, 20), "image/jpeg");
        var subject = $"g-{Guid.NewGuid():N}";
        var email = Email();
        var userId = (await GoogleAsync(email, subject)).UserId!.Value;
        using var client = factory.CookieClient(factory.Protect((await GoogleAsync(email, subject)).Principal!));

        var own = TestImages.Png(256, 256, 21);
        await (await PhotoTests.UploadPhotoAsync(client, own, "image/png")).ReadAsync<MeDto>();
        await GoogleAsync(email, subject);
        Assert.Equal((UserPhotos.VersionOf(own), true, "upload"), await PhotoOfAsync(userId));

        // Initials chosen by hand stay too.
        await (await client.DeleteAsync("/api/me/photo")).ReadAsync<MeDto>();
        await GoogleAsync(email, subject);
        Assert.Equal(((string?)null, true, (string?)null), await PhotoOfAsync(userId));
    }

    [Fact]
    public async Task Microsoft_sign_in_reads_the_photo_from_graph_with_the_sign_in_access_token()
    {
        var photo = TestImages.Jpeg(240, 240, 30);
        factory.Host.Requests.Clear();
        factory.Host.Respond = _ => FakePhotoHost.Image(photo, "image/jpeg");
        var subject = Guid.NewGuid().ToString();
        var email = Email();
        var userId = (await MicrosoftAsync(email, subject)).UserId!.Value;

        var request = Assert.Single(factory.Host.Requests);
        Assert.Equal(ProviderPhotos.GraphPhotoUrl, request.RequestUri!.OriginalString);
        Assert.Equal(("Bearer", "graph-token"), (request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));
        Assert.Equal((UserPhotos.VersionOf(photo), false, "microsoft"), await PhotoOfAsync(userId));

        // Without an access token nothing is asked; a failure keeps the photo; Graph's "no photo" takes it away.
        factory.Host.Requests.Clear();
        await MicrosoftAsync(email, subject, accessToken: null);
        Assert.Empty(factory.Host.Requests);
        factory.Host.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await MicrosoftAsync(email, subject);
        Assert.Equal((UserPhotos.VersionOf(photo), false, "microsoft"), await PhotoOfAsync(userId));
        factory.Host.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        await MicrosoftAsync(email, subject);
        Assert.Equal(((string?)null, false, (string?)null), await PhotoOfAsync(userId));
    }

    [Fact]
    public async Task Graphs_no_photo_leaves_a_photo_from_another_provider()
    {
        var email = Email();
        factory.Host.Respond = _ => FakePhotoHost.Image(TestImages.Jpeg(256, 256, 40), "image/jpeg");
        var userId = (await GoogleAsync(email, $"g-{Guid.NewGuid():N}")).UserId!.Value;
        var google = await PhotoOfAsync(userId);

        factory.Host.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Equal(userId, (await MicrosoftAsync(email, Guid.NewGuid().ToString())).UserId);
        Assert.Equal(google, await PhotoOfAsync(userId));
    }
}
