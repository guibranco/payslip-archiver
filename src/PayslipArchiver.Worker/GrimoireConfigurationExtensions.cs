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

        var index = 0;
        while (index < builder.Sources.Count && builder.Sources[index] is not EnvironmentVariablesConfigurationSource)
        {
            index++;
        }

        builder.Sources.Insert(index, new GrimoireConfigurationSource(options));
        return builder;
    }
}