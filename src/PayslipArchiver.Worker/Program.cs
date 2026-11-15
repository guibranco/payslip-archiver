using PayslipArchiver.Application;
using PayslipArchiver.Infrastructure;
using PayslipArchiver.Infrastructure.Archive;
using PayslipArchiver.Worker;

// One-off interactive mode: `payslip-archiver authorize-dropbox` prints a refresh token and exits.
if (args is [var command, ..] && command.Equals("authorize-dropbox", StringComparison.OrdinalIgnoreCase))
{
    var configurationBuilder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args[1..]);
    var configuration = configurationBuilder
        .AddGrimoireIfConfigured(configurationBuilder.Build())
        .Build();

    return await DropboxAuthorizer.RunAsync(configuration, Console.Out, Console.In).ConfigureAwait(false);
}

var builder = Host.CreateApplicationBuilder(args);

// Everything but the Grimoire:* bootstrap keys can live in Grimoire (see README → Grimoire).
builder.Configuration.AddGrimoireIfConfigured(builder.Configuration);

builder.Services.AddSystemd();
builder.Services.AddOptions<ScheduleOptions>().Bind(builder.Configuration.GetSection(ScheduleOptions.Section));
builder.Services.AddPayslipArchiverApplication(builder.Configuration);
builder.Services.AddPayslipArchiverInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ArchiverWorker>();

await builder.Build().RunAsync().ConfigureAwait(false);
return Environment.ExitCode;

/// <summary>Anchor type for <c>AddUserSecrets&lt;Program&gt;</c>.</summary>
#pragma warning disable CA1050 // Top-level statements compile into this class; a partial declaration lets it be referenced by name.
public partial class Program;
#pragma warning restore CA1050