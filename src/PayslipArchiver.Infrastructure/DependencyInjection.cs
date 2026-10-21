using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Infrastructure.Archive;
using PayslipArchiver.Infrastructure.Ledger;
using PayslipArchiver.Infrastructure.Mail;
using PayslipArchiver.Infrastructure.Monitoring;
using PayslipArchiver.Infrastructure.Pdf;

namespace PayslipArchiver.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires every port to an adapter chosen by configuration:
    /// <c>Mailbox:Protocol</c> (Imap | Pop3), <c>Pdf:Engine</c> (PdfSharp | Qpdf),
    /// <c>Archive:Provider</c> (Dropbox | FileSystem) and <c>Heartbeat:PingUrl</c> (set | empty).
    /// </summary>
    public static IServiceCollection AddPayslipArchiverInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient();

        // Mailbox
        services.AddOptions<MailboxOptions>()
            .Bind(configuration.GetSection(MailboxOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var protocol = configuration.GetValue(
            $"{MailboxOptions.Section}:{nameof(MailboxOptions.Protocol)}",
            MailboxProtocol.Imap);
        switch (protocol)
        {
            case MailboxProtocol.Pop3:
                services.AddSingleton<IMailboxSource, Pop3MailboxSource>();
                break;
            default:
                services.AddSingleton<IMailboxSource, ImapMailboxSource>();
                break;
        }

        // PDF
        services.AddOptions<PdfEngineOptions>()
            .Bind(configuration.GetSection(PdfEngineOptions.Section));

        var engine = configuration.GetValue(
            $"{PdfEngineOptions.Section}:{nameof(PdfEngineOptions.Engine)}",
            PdfEngine.PdfSharp);
        switch (engine)
        {
            case PdfEngine.Qpdf:
                services.AddSingleton<IPdfDecryptor, QpdfDecryptor>();
                break;
            default:
                services.AddSingleton<IPdfDecryptor, PdfSharpDecryptor>();
                break;
        }

        // Archive
        services.AddOptions<ArchiveOptions>()
            .Bind(configuration.GetSection(ArchiveOptions.Section));

        var provider = configuration.GetValue(
            $"{ArchiveOptions.Section}:{nameof(ArchiveOptions.Provider)}",
            ArchiveProvider.Dropbox);
        switch (provider)
        {
            case ArchiveProvider.FileSystem:
                services.AddSingleton<IArchiveStore, FileSystemArchiveStore>();
                break;
            default:
                services.AddOptions<DropboxOptions>()
                    .Bind(configuration.GetSection(DropboxOptions.Section))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddHttpClient(nameof(DropboxArchiveStore), client => client.Timeout = TimeSpan.FromMinutes(5));
                services.AddSingleton<IArchiveStore, DropboxArchiveStore>();
                break;
        }

        // Ledger
        services.AddOptions<LedgerOptions>()
            .Bind(configuration.GetSection(LedgerOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IProcessedLedger, SqliteProcessedLedger>();

        // Heartbeat
        services.AddOptions<HeartbeatOptions>()
            .Bind(configuration.GetSection(HeartbeatOptions.Section));

        var pingUrl = configuration[$"{HeartbeatOptions.Section}:{nameof(HeartbeatOptions.PingUrl)}"];
        if (string.IsNullOrWhiteSpace(pingUrl))
        {
            services.AddSingleton<IHeartbeat, NoOpHeartbeat>();
        }
        else
        {
            services.AddHttpClient(HealthchecksHeartbeat.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
            services.AddSingleton<IHeartbeat, HealthchecksHeartbeat>();
        }

        return services;
    }
}