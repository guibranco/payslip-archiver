using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PayslipArchiver.Application.Archiving;
using PayslipArchiver.Application.Options;

namespace PayslipArchiver.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use case, the message filter and the application-level options (validated at startup).</summary>
    public static IServiceCollection AddPayslipArchiverApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PipelineOptions>()
            .Bind(configuration.GetSection(PipelineOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MessageFilterOptions>()
            .Bind(configuration.GetSection(MessageFilterOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ArchiveNamingOptions>()
            .Bind(configuration.GetSection(ArchiveNamingOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PdfOptions>()
            .Bind(configuration.GetSection(PdfOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<MessageFilter>();
        services.AddSingleton<ArchivePayslips>();

        return services;
    }
}