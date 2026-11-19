using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Taskloom.Api.Tests.Support;

namespace Taskloom.Api.Tests;

/// <summary>
/// Efficiency 1: the app's files are kept for a year when their names carry a content hash (/assets) and checked on
/// every visit otherwise; the web build's Brotli and gzip copies go to browsers that accept them, and everything else of
/// a compressible type, the API's JSON above all, is compressed as it is sent.
/// </summary>
public class DeliveryTests(DeliveryTests.Site factory) : IClassFixture<DeliveryTests.Site>
{
    private const string Keep = "public, max-age=31536000, immutable";
    private const string Check = "no-cache";
    private const string Script = "/assets/app-1a2b3c4d.js";
    private const string Styles = "/assets/app-5e6f7a8b.css";
    private const string Font = "/assets/inter-9c0d1e2f.woff2";

    /// <summary>A stub build like the one web/vite.config.ts writes: copies beside the text files, the styles with a gzip copy only.</summary>
    public sealed class Site : TaskloomFactory
    {
        public Site()
        {
            WebRoot = Directory.CreateTempSubdirectory("taskloom-delivery-").FullName;
            Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
            Write("index.html", $"<!doctype html><html><head><script src=\"/theme-init.js\"></script><script type=\"module\" src=\"{Script}\"></script></head><body><div id=\"app\"></div></body></html>", "br", "gzip");
            Write("theme-init.js", "document.documentElement.dataset.theme = 'light';");
            Write(Script, string.Concat(Enumerable.Repeat("export const greeting = 'Taskloom';\n", 200)), "br", "gzip");
            Write(Styles, string.Concat(Enumerable.Repeat(".row { display: flex; gap: 8px; }\n", 200)), "gzip");
            File.WriteAllBytes(Path.Combine(WebRoot, Font.TrimStart('/')), [0x77, 0x4F, 0x46, 0x32, 1, 2, 3, 4]);
        }

        public string PathOf(string urlPath) => Path.Combine(WebRoot!, urlPath.TrimStart('/'));

        private void Write(string urlPath, string text, params string[] copies)
        {
            var file = PathOf(urlPath);
            var bytes = Encoding.UTF8.GetBytes(text);
            File.WriteAllBytes(file, bytes);
            foreach (var encoding in copies) File.WriteAllBytes(file + (encoding == "br" ? ".br" : ".gz"), Compress(bytes, encoding));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Directory.Delete(WebRoot!, recursive: true);
        }
    }

    private static byte[] Compress(byte[] bytes, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream stream = encoding == "br"
                   ? new BrotliStream(output, CompressionLevel.SmallestSize)
                   : new GZipStream(output, CompressionLevel.SmallestSize))
            stream.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes, string? encoding)
    {
        if (encoding is null) return bytes;
        using var input = new MemoryStream(bytes);
        using Stream stream = encoding switch
        {
            "br" => new BrotliStream(input, CompressionMode.Decompress),
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            _ => throw new InvalidOperationException($"Unexpected encoding {encoding}"),
        };
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? acceptEncoding,
        HttpMethod? method = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (acceptEncoding is not null) request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        configure?.Invoke(request);
        return await client.SendAsync(request);
    }

    private static string? Encoding_(HttpResponseMessage res) => res.Content.Headers.ContentEncoding.SingleOrDefault();

    private static string Header(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var values) || res.Content.Headers.TryGetValues(name, out values) ? string.Join(", ", values) : "";

    [Fact]
    public async Task Hashed_files_are_kept_for_a_year_and_the_rest_are_checked_on_every_visit()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var path in new[] { Script, Styles, Font })
        {
            var res = await SendAsync(client, path, "gzip, deflate, br");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(Keep, Header(res, "Cache-Control"));
        }

        // index.html (as itself, as "/" and as the SPA fallback) and theme-init.js, whose names never change.
        foreach (var path in new[] { "/", "/index.html", "/l/abc/list", "/theme-init.js" })
        foreach (var accept in new[] { "gzip, deflate, br", null })
        {
            var res = await SendAsync(client, path, accept);
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{path}: {(int)res.StatusCode}");
            Assert.Equal(Check, Header(res, "Cache-Control"));

            // The check costs no download when nothing changed, and the answer says the same about keeping it.
            var etag = res.Headers.ETag;
            if (path == "/l/abc/list") continue;
            Assert.NotNull(etag);
            var again = await SendAsync(client, path, accept, configure: r => r.Headers.IfNoneMatch.Add(etag));
            Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
            Assert.Equal(Check, Header(again, "Cache-Control"));
        }

        // A file a release no longer has is 404, never index.html, and nothing tells the browser to keep the answer.
        foreach (var path in new[] { "/assets/app-00000000.js", "/assets/app-00000000.css" })
        {
            var res = await SendAsync(client, path, "gzip, deflate, br");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Equal("", Header(res, "Cache-Control"));
        }
    }

    [Fact]
    public async Task The_builds_compressed_copies_go_to_browsers_that_accept_them()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var script = await File.ReadAllBytesAsync(factory.PathOf(Script));

        // What each browser offers, and the file it gets: Brotli unless gzip is rated higher, the plain file if neither.
        foreach (var (accept, expected) in new (string?, string?)[]
                 {
                     ("gzip, deflate, br, zstd", "br"), ("br", "br"), ("*", "br"), ("gzip", "gzip"), ("br;q=0.5, gzip", "gzip"),
                     ("br;q=0, *", "gzip"), ("BR", "br"), ("identity", null), ("deflate", null), ("br;q=0, gzip;q=0", null), (null, null),
                 })
        {
            var res = await SendAsync(client, Script, accept);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.True(expected == Encoding_(res), $"Accept-Encoding: {accept} gave {Encoding_(res) ?? "the plain file"}");
            Assert.Equal("text/javascript", res.Content.Headers.ContentType?.MediaType);
            Assert.Equal("Accept-Encoding", Header(res, "Vary"));
            Assert.Equal(Keep, Header(res, "Cache-Control"));
            var body = await res.Content.ReadAsByteArrayAsync();
            Assert.Equal(body.Length, res.Content.Headers.ContentLength);
            Assert.Equal(script, Decompress(body, expected));
            if (expected is not null) Assert.Equal(await File.ReadAllBytesAsync(factory.PathOf(Script) + (expected == "br" ? ".br" : ".gz")), body);
        }

        // Each copy has an ETag of its own, so a browser that checks never gets a 304 meant for another copy.
        var tags = new List<string>();
        foreach (var accept in new[] { "br", "gzip", "identity" })
            tags.Add((await SendAsync(client, Script, accept)).Headers.ETag!.Tag);
        Assert.Equal(3, tags.Distinct().Count());

        // The styles have a gzip copy only: a browser that prefers Brotli gets that rather than the plain file.
        var styles = await SendAsync(client, Styles, "gzip, deflate, br");
        Assert.Equal("gzip", Encoding_(styles));
        Assert.Equal("text/css", styles.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(factory.PathOf(Styles)), Decompress(await styles.Content.ReadAsByteArrayAsync(), "gzip"));

        // HEAD describes the copy; a range request gets bytes of the plain file; the copies have no address of their own.
        var head = await SendAsync(client, Script, "br", HttpMethod.Head);
        Assert.Equal("br", Encoding_(head));
        Assert.Equal(new FileInfo(factory.PathOf(Script) + ".br").Length, head.Content.Headers.ContentLength);
        var range = await SendAsync(client, Script, "br", configure: r => r.Headers.Range = new RangeHeaderValue(0, 9));
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Null(Encoding_(range));
        Assert.Equal(script[..10], await range.Content.ReadAsByteArrayAsync());
        foreach (var path in new[] { Script + ".br", Script + ".gz", "/index.html.br", "/assets/app-00000000.js.br" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, path, "gzip, deflate, br")).StatusCode);

        // Fonts are compressed already: sent as they are, with no Vary.
        var font = await SendAsync(client, Font, "gzip, deflate, br");
        Assert.Null(Encoding_(font));
        Assert.Equal("", Header(font, "Vary"));
    }

    [Fact]
    public async Task Pages_keep_their_headers_when_compressed()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var path in new[] { "/", "/index.html", "/l/abc/list" })
        {
            var res = await SendAsync(client, path, "gzip, deflate, br");
            Assert.Equal("br", Encoding_(res));
            Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Api.Auth.ContentSecurityPolicy.Value, Header(res, "Content-Security-Policy"));
            Assert.Equal("nosniff", Header(res, "X-Content-Type-Options"));
            Assert.Contains("<div id=\"app\">", Encoding.UTF8.GetString(Decompress(await res.Content.ReadAsByteArrayAsync(), "br")));
        }
    }

    [Fact]
    public async Task Api_responses_are_compressed_over_https_too()
    {
        // As on Cloud Run or Azure Container Apps, where the app sees HTTPS once UseForwardedHeaders reads the proxy's header.
        foreach (var scheme in new[] { "http", "https" })
        {
            var client = factory.CreateClient(new() { BaseAddress = new Uri($"{scheme}://localhost"), HandleCookies = true, AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Taskloom", "1");
            (await client.PostAsJsonAsync("/auth/dev-login", new { email = Builders.NewEmail(), displayName = "Dana" })).EnsureSuccessStatusCode();

            var plain = await SendAsync(client, "/api/workspaces", null);
            Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
            Assert.Null(Encoding_(plain));
            var expected = JsonDocument.Parse(await plain.Content.ReadAsStringAsync()).RootElement.GetRawText();
            foreach (var (accept, encoding) in new[] { ("gzip, deflate, br", "br"), ("gzip", "gzip") })
            {
                var res = await SendAsync(client, "/api/workspaces", accept);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.True(encoding == Encoding_(res), $"{scheme}, Accept-Encoding: {accept} gave {Encoding_(res) ?? "no compression"}");
                Assert.Contains("Accept-Encoding", Header(res, "Vary"));
                var json = Encoding.UTF8.GetString(Decompress(await res.Content.ReadAsByteArrayAsync(), encoding));
                Assert.Equal(expected, JsonDocument.Parse(json).RootElement.GetRawText());
            }

            // Errors too.
            var missing = await SendAsync(client, "/api/no-such-thing", "gzip");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("gzip", Encoding_(missing));
            Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
            Assert.Contains("\"status\":404", Encoding.UTF8.GetString(Decompress(await missing.Content.ReadAsByteArrayAsync(), "gzip")));
        }
    }
}
