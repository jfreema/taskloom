using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Taskloom.Api;

/// <summary>
/// Efficiency 1: how the built web app and the API's responses travel.
/// <list type="bullet">
/// <item>Vite names every file under /assets after its content, so a release that changes a file also changes its
/// name. Those files may be kept for a year without asking again. Every other file (index.html, theme-init.js) is
/// checked on each visit and answered 304 when unchanged, so after a release the browser always gets the index.html
/// that names the new files.</item>
/// <item>The web build writes a Brotli (.br) and a gzip (.gz) copy of each text file (web/vite.config.ts); browsers
/// that accept one get it as is. Anything else of a compressible type, above all the API's JSON and the SPA
/// fallback's index.html, is compressed as it is sent.</item>
/// </list>
/// </summary>
public static class AppFiles
{
    /// <summary>Files under /assets: their names change whenever their content does.</summary>
    public const string KeepCacheControl = "public, max-age=31536000, immutable";

    /// <summary>Every other file of the app: keep it, but ask whether it changed before using it.</summary>
    public const string CheckCacheControl = "no-cache";

    private static readonly PathString HashedFolder = "/assets";

    /// <summary>The copies the web build writes, in order of preference when a browser accepts both equally.</summary>
    private static readonly (string Encoding, string Extension)[] Precompressed = [("br", ".br"), ("gzip", ".gz")];

    /// <summary>
    /// Brotli, then gzip, on HTTPS too: TLS ends at the host's proxy, and the API's responses can't be made to reflect
    /// a cross-site attacker's text (BREACH), because every /api call needs the X-Taskloom header (spec 5.1).
    /// </summary>
    public static IServiceCollection AddTaskloomCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json", "image/svg+xml"]);
        });
        // Brotli's "optimal" is its quality 4: about gzip's size at about gzip's speed. The build's copies use quality 11.
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        return services;
    }

    /// <summary>
    /// For a GET or HEAD of a file that has a compressed copy the browser accepts, serves the copy instead (by changing
    /// the request's path before UseStaticFiles). The copies are never served under their own names. A range request
    /// gets the plain file, so its byte ranges mean what the browser expects.
    /// </summary>
    public static IApplicationBuilder UsePrecompressedAppFiles(this IApplicationBuilder app)
    {
        var files = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        return app.Use((ctx, next) =>
        {
            var request = ctx.Request;
            var path = request.Path.Value;
            if (path is null || request.Path.StartsWithSegments("/api") || !Path.HasExtension(path)) return next(ctx);
            if (Precompressed.Any(p => path.EndsWith(p.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return next(ctx);
            var copies = Precompressed.Where(p => files.GetFileInfo(path + p.Extension) is { Exists: true, IsDirectory: false }).ToArray();
            if (copies.Length == 0) return next(ctx);

            // Whichever file is sent, caches must keep one per Accept-Encoding, even for a browser that sent none.
            ctx.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
            if (request.Headers.Range.Count > 0 || !TryParseAcceptEncoding(request, out var accepted)) return next(ctx);
            // The copy the browser rates highest; OrderByDescending is stable, so Brotli wins a tie.
            var (_, extension) = copies.Where(c => Quality(accepted, c.Encoding) > 0)
                .OrderByDescending(c => Quality(accepted, c.Encoding))
                .FirstOrDefault();
            if (extension is not null) request.Path = path + extension;
            return next(ctx);
        });
    }

    /// <summary>For UseStaticFiles and the SPA fallback: content types and encodings of the copies, and cache headers.</summary>
    public static StaticFileOptions StaticFileOptions() => new()
    {
        ContentTypeProvider = new CopyAwareContentTypes(),
        OnPrepareResponse = file =>
        {
            var response = file.Context.Response;
            var name = file.File.Name;
            foreach (var (encoding, extension) in Precompressed)
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    response.Headers.ContentEncoding = encoding;
            response.Headers.CacheControl = file.Context.Request.Path.StartsWithSegments(HashedFolder)
                ? KeepCacheControl
                : CheckCacheControl;
        },
    };

    private static bool TryParseAcceptEncoding(HttpRequest request, out IList<StringWithQualityHeaderValue> accepted)
    {
        accepted = [];
        var header = request.Headers.AcceptEncoding;
        return header.Count > 0 && StringWithQualityHeaderValue.TryParseList(header, out accepted!);
    }

    /// <summary>The quality the browser gives an encoding: its own entry, else "*", else 0 (not accepted).</summary>
    private static double Quality(IList<StringWithQualityHeaderValue> accepted, string encoding)
    {
        double? any = null;
        foreach (var value in accepted)
        {
            if (value.Value.Equals(encoding, StringComparison.OrdinalIgnoreCase)) return value.Quality ?? 1;
            if (value.Value == "*") any = value.Quality ?? 1;
        }
        return any ?? 0;
    }

    /// <summary>A copy such as app-1a2b.js.br has the content type of app-1a2b.js; its encoding is set in OnPrepareResponse.</summary>
    private sealed class CopyAwareContentTypes : IContentTypeProvider
    {
        private readonly FileExtensionContentTypeProvider _types = new();

        public bool TryGetContentType(string subpath, out string contentType)
        {
            foreach (var (_, extension) in Precompressed)
                if (subpath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return _types.TryGetContentType(subpath[..^extension.Length], out contentType!);
            return _types.TryGetContentType(subpath, out contentType!);
        }
    }
}
