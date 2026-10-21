using Dropbox.Api;

using Microsoft.Extensions.Configuration;

namespace PayslipArchiver.Infrastructure.Archive;

/// <summary>
/// One-time, interactive OAuth flow that turns an app key into a long-lived refresh token.
/// Run with <c>payslip-archiver authorize-dropbox</c>; nothing is stored, the token is printed.
/// </summary>
public static class DropboxAuthorizer
{
    private static readonly string[] Scopes = ["files.content.write", "files.metadata.read"];

    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextReader input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);

        var appKey = configuration[$"{DropboxOptions.Section}:{nameof(DropboxOptions.AppKey)}"];
        var appSecret = configuration[$"{DropboxOptions.Section}:{nameof(DropboxOptions.AppSecret)}"];

        if (string.IsNullOrWhiteSpace(appKey))
        {
            await output.WriteLineAsync("Dropbox:AppKey is not configured. Create an app at https://www.dropbox.com/developers/apps and set the key first.").ConfigureAwait(false);
            return 2;
        }

        var usePkce = string.IsNullOrWhiteSpace(appSecret);
        var pkce = usePkce ? new PKCEOAuthFlow() : null;

        var authorizeUri = pkce is not null
            ? pkce.GetAuthorizeUri(OAuthResponseType.Code, appKey, redirectUri: (string?)null, state: null, tokenAccessType: TokenAccessType.Offline, scopeList: Scopes)
            : DropboxOAuth2Helper.GetAuthorizeUri(OAuthResponseType.Code, appKey, redirectUri: (string?)null, state: null, tokenAccessType: TokenAccessType.Offline, scopeList: Scopes);

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("1. Open this URL in a browser while signed in to the Dropbox account that should hold the archive:").ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("   " + authorizeUri).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("2. Approve the app and paste the code shown here, then press Enter:").ConfigureAwait(false);
        await output.WriteAsync("   code> ").ConfigureAwait(false);

        var code = (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            await output.WriteLineAsync("No code entered.").ConfigureAwait(false);
            return 2;
        }

        var response = pkce is not null
            ? await pkce.ProcessCodeFlowAsync(code, appKey).ConfigureAwait(false)
            : await DropboxOAuth2Helper.ProcessCodeFlowAsync(code, appKey, appSecret).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            await output.WriteLineAsync("Dropbox did not return a refresh token. Make sure the app's access type allows offline access.").ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Done. Store this as Dropbox:RefreshToken (environment variable Dropbox__RefreshToken):").ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("   " + response.RefreshToken).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        return 0;
    }
}