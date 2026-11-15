using Microsoft.Extensions.Configuration;

namespace PayslipArchiver.Infrastructure.Configuration;

/// <summary>Adds a Grimoire server's configuration entries and secrets to <see cref="IConfiguration"/>.</summary>
public sealed class GrimoireConfigurationSource : IConfigurationSource
{
    public GrimoireConfigurationSource(GrimoireOptions options)
        : this(options, handler: null)
    {
    }

    internal GrimoireConfigurationSource(GrimoireOptions options, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Handler = handler;
    }

    public GrimoireOptions Options { get; }

    /// <summary>Test seam; <see langword="null"/> uses a plain <see cref="HttpClientHandler"/>.</summary>
    internal HttpMessageHandler? Handler { get; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new GrimoireConfigurationProvider(Options, Handler);
}