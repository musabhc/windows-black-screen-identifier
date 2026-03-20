using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Core.Utilities;

namespace BlackScreenIdentifier.Actions.Infrastructure;

public sealed class GitHubReleaseService(HttpClient? httpClient = null) : IUpdateService
{
    private readonly HttpClient client = httpClient ?? BuildClient();

    public async Task<VersionInfo> CheckAsync(CancellationToken cancellationToken)
    {
        var currentVersion = GetCurrentVersion();
        var result = new VersionInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = currentVersion,
            Status = UpdateCheckStatus.Unknown,
            CheckedAt = DateTimeOffset.Now
        };

        try
        {
            var url = $"https://api.github.com/repos/{ApplicationMetadata.RepositoryOwner}/{ApplicationMetadata.RepositoryName}/releases/latest";
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var rawTag = root.GetProperty("tag_name").GetString() ?? currentVersion;
            var latest = rawTag.Trim().TrimStart('v', 'V');
            result.LatestVersion = latest;
            result.ReleaseUrl = root.GetProperty("html_url").GetString() ?? string.Empty;

            if (TryParseVersion(latest, out var latestVersion) && TryParseVersion(currentVersion, out var installedVersion))
            {
                result.Status = latestVersion > installedVersion ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
                result.StatusMessage = result.Status == UpdateCheckStatus.UpdateAvailable
                    ? $"Yeni sürüm hazır: {latest}"
                    : "Yüklü sürüm güncel.";
            }
            else
            {
                result.Status = UpdateCheckStatus.Unknown;
                result.StatusMessage = $"Sürüm bilgisi okundu: {latest}";
            }
        }
        catch (Exception ex)
        {
            result.Status = UpdateCheckStatus.Failed;
            result.StatusMessage = $"Sürüm kontrolü başarısız: {ex.Message}";
        }

        return result;
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BlackScreenIdentifier/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var cleaned = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(cleaned, out version!);
    }
}
