using System.Reflection;
using System.Net;
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
        var repositoryUrl = GetRepositoryUrl();
        var result = new VersionInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = currentVersion,
            Status = UpdateCheckStatus.Unknown,
            CheckedAt = DateTimeOffset.Now,
            RepositoryUrl = repositoryUrl,
            ReleaseUrl = $"{repositoryUrl}/releases",
            IsConfigured = !string.IsNullOrWhiteSpace(repositoryUrl)
        };

        if (!result.IsConfigured)
        {
            result.StatusMessage = "Sürüm deposu yapılandırılmadı.";
            return result;
        }

        try
        {
            var url = $"https://api.github.com/repos/{ApplicationMetadata.RepositoryOwner}/{ApplicationMetadata.RepositoryName}/releases/latest";
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            result.LastHttpStatusCode = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.Status = UpdateCheckStatus.NoPublishedRelease;
                result.StatusMessage = "Henüz yayınlanmış stable sürüm yok.";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.Status = UpdateCheckStatus.Failed;
                result.StatusMessage = $"Sürüm kontrolü şu anda kullanılamıyor ({(int)response.StatusCode}).";
                return result;
            }

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
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.Status = UpdateCheckStatus.Failed;
            result.StatusMessage = "Sürüm kontrolü zaman aşımına uğradı.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            result.Status = UpdateCheckStatus.NoPublishedRelease;
            result.LastHttpStatusCode = (int)HttpStatusCode.NotFound;
            result.StatusMessage = "Henüz yayınlanmış stable sürüm yok.";
        }
        catch (HttpRequestException ex)
        {
            result.Status = UpdateCheckStatus.Failed;
            result.LastHttpStatusCode = ex.StatusCode is null ? null : (int)ex.StatusCode.Value;
            result.StatusMessage = ex.StatusCode is null
                ? "Sürüm kontrolü şu anda kullanılamıyor."
                : $"Sürüm kontrolü şu anda kullanılamıyor ({(int)ex.StatusCode.Value}).";
        }
        catch (Exception ex)
        {
            result.Status = UpdateCheckStatus.Failed;
            result.StatusMessage = $"Sürüm kontrolü başarısız oldu: {ex.Message}";
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

    private static string GetRepositoryUrl()
    {
        if (string.IsNullOrWhiteSpace(ApplicationMetadata.RepositoryOwner) ||
            string.IsNullOrWhiteSpace(ApplicationMetadata.RepositoryName))
        {
            return string.Empty;
        }

        return $"https://github.com/{ApplicationMetadata.RepositoryOwner}/{ApplicationMetadata.RepositoryName}";
    }
}
