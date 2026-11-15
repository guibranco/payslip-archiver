using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

namespace PayslipArchiver.Infrastructure.Configuration;

/// <summary>
/// Loads, once at startup, every configuration entry of the configured Grimoire environment
/// (<c>GET consumer/configurations</c>) plus the secrets named by <see cref="GrimoireOptions.SecretKeys"/>
/// (<c>GET consumer/secrets/{name}</c>). Any failure other than a missing secret stops the host from
/// starting: a service that cannot read its configuration should not run on stale or partial settings.
/// </summary>
/// <remarks>Values are never included in exception messages.</remarks>
internal sealed class GrimoireConfigurationProvider : ConfigurationProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GrimoireOptions _options;
    private readonly HttpMessageHandler? _handler;

    public GrimoireConfigurationProvider(GrimoireOptions options, HttpMessageHandler? handler)
    {
        _options = options;
        _handler = handler;
    }

    public override void Load()
    {
        var baseUri = ValidateAndGetBaseUri(_options);
        var environment = Uri.EscapeDataString(_options.Environment);

        using var http = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        http.BaseAddress = baseUri;
        http.Timeout = _options.Timeout;
        http.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var configurations = Get<ConfigurationList>(http, $"consumer/configurations?environment={environment}", "configurations")
            ?? throw Failure($"Grimoire environment '{_options.Environment}' was not found for this application.");
        foreach (var item in configurations.Items)
        {
            data[item.Key] = item.Value;
        }

        foreach (var key in _options.SecretKeys ?? GrimoireOptions.DefaultSecretKeys)
        {
            var name = GrimoireOptions.SecretNameFor(key);
            var secret = Get<Secret>(http, $"consumer/secrets/{Uri.EscapeDataString(name)}?environment={environment}", $"secret '{name}'");
            if (secret is not null)
            {
                data[key] = secret.Value;
            }
        }

        Data = data;
    }

    /// <returns>The deserialised body, or <see langword="null"/> on 404.</returns>
    private static T? Get<T>(HttpClient http, string relativeUrl, string what)
        where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            using var response = http.Send(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw Failure($"Grimoire rejected the API key ({(int)response.StatusCode}) while reading {what}. Check Grimoire:ApiKey.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Failure($"Grimoire answered {(int)response.StatusCode} while reading {what}.");
            }

            using var body = response.Content.ReadAsStream();
            return JsonSerializer.Deserialize<T>(body, Json)
                ?? throw Failure($"Grimoire returned an empty body while reading {what}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw Failure($"Could not read {what} from Grimoire at {http.BaseAddress}: {ex.Message}", ex);
        }
    }

    internal static Uri ValidateAndGetBaseUri(GrimoireOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw Failure("Grimoire:BaseUrl is set but Grimoire:ApiKey is empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            throw Failure("Grimoire:Environment is empty.");
        }

        var raw = options.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute, out var uri))
        {
            throw Failure($"Grimoire:BaseUrl '{raw}' is not an absolute URL.");
        }

        // The API key and every secret travel over this connection: plain HTTP is only tolerated on loopback.
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw Failure($"Grimoire:BaseUrl must use https (got '{uri.Scheme}://{uri.Authority}').");
        }

        return uri;
    }

    private static InvalidOperationException Failure(string message, Exception? inner = null) => new(message, inner);

    private sealed record ConfigurationItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string? Value);

    private sealed record ConfigurationList(
        [property: JsonPropertyName("items")] IReadOnlyList<ConfigurationItem> Items);

    private sealed record Secret(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string? Value);
}