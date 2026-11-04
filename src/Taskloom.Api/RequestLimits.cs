using Microsoft.AspNetCore.Http.Features;

namespace Taskloom.Api;

/// <summary>Endpoint metadata: this endpoint takes a body up to <paramref name="MaxBytes"/> (the default is <see cref="RequestLimits.DefaultBodyBytes"/>).</summary>
public sealed record LargeBody(long MaxBytes);

/// <summary>
/// How large a request body may be. Kestrel allows 30 MB everywhere by default, so any JSON endpoint would read and
/// parse 30 MB per request, and an upload over 25 MB was buffered in full before the handler could refuse it.
/// </summary>
public static class RequestLimits
{
    /// <summary>Far above the largest body the client sends (a 200,000-character description is at most about 1.2 MB of JSON).</summary>
    public const long DefaultBodyBytes = 4 * 1024 * 1024;

    /// <summary>An attachment of the largest size (spec 5.1) plus room for the multipart boundaries and part headers.</summary>
    public const long UploadBodyBytes = Limits.AttachmentBytes + 64 * 1024;

    public const string TooLargeMessage = "The request is too large.";
    public const string FileTooLargeMessage = "The request is too large. Files can be up to 25 MB.";

    /// <summary>The detail of a 413 for this request: uploads say what the file limit is.</summary>
    public static string MessageFor(HttpContext ctx) =>
        ctx.GetEndpoint()?.Metadata.GetMetadata<LargeBody>() is null ? TooLargeMessage : FileTooLargeMessage;

    /// <summary>
    /// A declared length over the endpoint's limit gets a 413 before anything is read; a body that arrives without
    /// one (chunked) is cut off by the server at the same limit. Runs after routing, which picks the endpoint.
    /// </summary>
    public static IApplicationBuilder UseRequestBodyLimits(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var max = ctx.GetEndpoint()?.Metadata.GetMetadata<LargeBody>()?.MaxBytes ?? DefaultBodyBytes;
            // Also before a refusal: the server then stops reading the unread body instead of draining all of it.
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = max;
            if (ctx.Request.ContentLength > max)
            {
                await Results.Problem(detail: MessageFor(ctx), statusCode: StatusCodes.Status413PayloadTooLarge).ExecuteAsync(ctx);
                return;
            }
            await next(ctx);
        });
}
