using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;

using PayslipArchiver.Infrastructure.Configuration;

namespace PayslipArchiver.Infrastructure.Tests.Configuration;

public sealed class GrimoireConfigurationProviderTests
{
    private const string ApiKey = "grm_0123456789abcdef0123456789abcdef01234567";

    private static GrimoireOptions Options(string baseUrl = "https://grimoire.example.com/api") => new()
    {
        BaseUrl = baseUrl,
        ApiKey = ApiKey,
        Environment = "production",
    };

    [Fact]
    public void LoadsConfigurationEntries_AndMapsSecretsOntoConfigurationKeys()
    {
        using var handler = new FakeGrimoire()
            .Respond("/api/consumer/configurations?environment=production",
                """{"items":[{"key":"Mailbox:Host","value":"imap.example.com","label":"production"},{"key":"Pdf:Engine","value":"Qpdf","label":"production"}]}""")
            .Respond("/api/consumer/secrets/Mailbox--Password?environment=production",
                """{"name":"Mailbox--Password","value":"p@ss $1","properties":{"enabled":true,"version":2}}""")
            .Respond("/api/consumer/secrets/Pdf--Password?environment=production",
                """{"name":"Pdf--Password","value":"12345","properties":{"enabled":true,"version":1}}""");

        var configuration = Build(Options(), handler);

        Assert.Equal("imap.example.com", configuration["Mailbox:Host"]);
        Assert.Equal("Qpdf", configuration["Pdf:Engine"]);
        Assert.Equal("p@ss $1", configuration["Mailbox:Password"]);
        Assert.Equal("12345", configuration["Pdf:Password"]);
        Assert.Null(configuration["Dropbox:RefreshToken"]); // 404 → skipped, left to options validation
        Assert.All(handler.ApiKeys, key => Assert.Equal(ApiKey, key));
        Assert.Equal(1 + GrimoireOptions.DefaultSecretKeys.Count, handler.ApiKeys.Count);
    }

    [Fact]
    public void FetchesOnlyTheConfiguredSecretKeys_WhenOverridden()
    {
        using var handler = new FakeGrimoire()
            .Respond("/api/consumer/configurations?environment=production", """{"items":[]}""")
            .Respond("/api/consumer/secrets/Custom--Key?environment=production", """{"name":"Custom--Key","value":"v"}""");
        var options = Options();
        options.SecretKeys = ["Custom:Key"];

        var configuration = Build(options, handler);

        Assert.Equal("v", configuration["Custom:Key"]);
        Assert.Equal(2, handler.ApiKeys.Count);
    }

    [Fact]
    public void Throws_WhenTheEnvironmentDoesNotExist()
    {
        using var handler = new FakeGrimoire();

        var ex = Assert.Throws<InvalidOperationException>(() => Build(Options(), handler));

        Assert.Contains("'production' was not found", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.InternalServerError, "answered 500")]
    public void Throws_OnAuthenticationOrServerErrors_WithoutLeakingTheKey(HttpStatusCode status, string expected)
    {
        using var handler = new FakeGrimoire { Fallback = status };

        var ex = Assert.Throws<InvalidOperationException>(() => Build(Options(), handler));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://grimoire.example.com/api")]
    [InlineData("ftp://grimoire.example.com")]
    [InlineData("not a url")]
    public void RefusesNonHttpsBaseUrls(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => GrimoireConfigurationProvider.ValidateAndGetBaseUri(Options(baseUrl)));
    }

    [Fact]
    public void AllowsPlainHttpOnLoopback_AndNormalisesTheTrailingSlash()
    {
        var uri = GrimoireConfigurationProvider.ValidateAndGetBaseUri(Options("http://localhost:8080"));

        Assert.Equal("http://localhost:8080/", uri.ToString());
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var options = Options();
        options.ApiKey = " ";

        Assert.Throws<InvalidOperationException>(() => GrimoireConfigurationProvider.ValidateAndGetBaseUri(options));
    }

    [Theory]
    [InlineData("Mailbox:Password", "Mailbox--Password")]
    [InlineData("Dropbox:RefreshToken", "Dropbox--RefreshToken")]
    [InlineData("Plain", "Plain")]
    public void SecretNames_FollowTheKeyVaultConvention(string key, string expected) =>
        Assert.Equal(expected, GrimoireOptions.SecretNameFor(key));

    private static IConfigurationRoot Build(GrimoireOptions options, HttpMessageHandler handler) =>
        new ConfigurationBuilder().Add(new GrimoireConfigurationSource(options, handler)).Build();

    /// <summary>Answers canned JSON by path and query; everything else gets <see cref="Fallback"/>.</summary>
    private sealed class FakeGrimoire : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);

        public HttpStatusCode Fallback { get; init; } = HttpStatusCode.NotFound;

        public List<string?> ApiKeys { get; } = [];

        public FakeGrimoire Respond(string pathAndQuery, string json)
        {
            _responses[pathAndQuery] = json;
            return this;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiKeys.Add(request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null);

            return _responses.TryGetValue(request.RequestUri!.PathAndQuery, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(Fallback);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}