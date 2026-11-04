using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskloom.Api.Tests.Support;

/// <summary>JSON as the API writes it (camelCase, enums as strings), and response helpers.</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Asserts success and reads the body.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            Assert.Fail($"{res.RequestMessage?.Method} {res.RequestMessage?.RequestUri} returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Options))!;
    }

    /// <summary>Asserts the status code and returns the ProblemDetails <c>detail</c> (null when there is no body).</summary>
    public static async Task<string?> ProblemAsync(this HttpResponseMessage res, HttpStatusCode expected)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"expected {(int)expected}, got {(int)res.StatusCode}: {body}");
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
    }

    /// <summary>The property names of a JSON object, sorted.</summary>
    public static string[] Keys(this JsonElement e) => e.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    public static string[] Sorted(params string[] keys) => keys.Order(StringComparer.Ordinal).ToArray();
}
