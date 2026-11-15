using Microsoft.Extensions.Configuration.EnvironmentVariables;

using PayslipArchiver.Infrastructure.Configuration;

namespace PayslipArchiver.Worker;

internal static class GrimoireConfigurationExtensions
{
    /// <summary>
    /// When <c>Grimoire:BaseUrl</c> is set in <paramref name="bootstrap"/>, adds Grimoire as a configuration
    /// source just <em>before</em> the environment-variable sources: Grimoire overrides <c>appsettings*.json</c>
    /// and user secrets, while a variable in the env file can still override a single Grimoire value.
    /// </summary>
    public static IConfigurationBuilder AddGrimoireIfConfigured(this IConfigurationBuilder builder, IConfiguration bootstrap)
    {
        var options = bootstrap.GetSection(GrimoireOptions.Section).Get<GrimoireOptions>();
        if (options is null || !options.IsEnabled)
        {
            return builder;
        }

        builder.Sources.Insert(InsertionIndex(builder.Sources), new GrimoireConfigurationSource(options));
        return builder;
    }

    /// <summary>
    /// Before the last un-prefixed environment-variable source, or at the end when there is none. The host
    /// builder also registers a <c>DOTNET_</c>-prefixed one <em>first</em>, ahead of <c>appsettings.json</c>;
    /// inserting there would let the empty defaults in <c>appsettings.json</c> override every Grimoire value.
    /// </summary>
    internal static int InsertionIndex(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                return i;
            }
        }

        return sources.Count;
    }
}