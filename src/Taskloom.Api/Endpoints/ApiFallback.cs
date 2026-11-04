using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// The answer for /api requests that no endpoint took. Routing can't give 405 or 415 here itself, because this
/// fallback accepts every method and content type, so it works them out from the API's own route patterns:
/// 405 (with Allow) when the path exists for other methods, 415 when the path and method exist but the body's
/// content type doesn't fit, else 404.
/// </summary>
public static class ApiFallback
{
    public static void MapApiFallback(this RouteGroupBuilder api) => api.MapFallback(Handle);

    private static IResult Handle(HttpContext http, EndpointDataSource endpoints, IInlineConstraintResolver constraints)
    {
        var segments = (http.Request.Path.Value ?? "").Trim('/').Split('/');
        var methods = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            // The fallbacks carry no methods, so they never match here.
            if (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not { HttpMethods.Count: > 0 } verbs) continue;
            if (!Matches(endpoint.RoutePattern, segments, http, constraints)) continue;
            methods.UnionWith(verbs.HttpMethods);
            if (verbs.HttpMethods.Contains(http.Request.Method, StringComparer.OrdinalIgnoreCase) &&
                endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is { } body)
                accepts.UnionWith(body.ContentTypes);
        }
        if (methods.Count == 0) return Results.NotFound();
        if (methods.Contains(http.Request.Method))
        {
            var types = accepts.Count > 0 ? string.Join(" or ", accepts) : "JSON";
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, detail: $"Send the body as {types}.");
        }
        http.Response.Headers.Allow = string.Join(", ", methods);
        return Results.Problem(statusCode: StatusCodes.Status405MethodNotAllowed, detail: $"Use {string.Join(", ", methods)} for this address.");
    }

    /// <summary>The path fits the pattern: same segments, literals equal ignoring case, parameters passing their constraints.</summary>
    private static bool Matches(RoutePattern pattern, string[] segments, HttpContext http, IInlineConstraintResolver constraints)
    {
        if (pattern.PathSegments.Count != segments.Length) return false;
        var values = new RouteValueDictionary();
        for (var i = 0; i < segments.Length; i++)
        {
            var parts = pattern.PathSegments[i].Parts;
            if (parts.Count != 1) return false;
            switch (parts[0])
            {
                case RoutePatternLiteralPart literal:
                    if (!string.Equals(literal.Content, segments[i], StringComparison.OrdinalIgnoreCase)) return false;
                    break;
                case RoutePatternParameterPart { IsCatchAll: false } parameter:
                    values[parameter.Name] = segments[i];
                    foreach (var policy in parameter.ParameterPolicies)
                    {
                        if (policy.Content is { } inline && constraints.ResolveConstraint(inline) is { } constraint &&
                            !constraint.Match(http, null, parameter.Name, values, RouteDirection.IncomingRequest))
                            return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}
