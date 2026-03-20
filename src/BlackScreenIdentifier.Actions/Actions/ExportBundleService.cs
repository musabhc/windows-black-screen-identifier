using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Core.Utilities;

namespace BlackScreenIdentifier.Actions.Actions;

public sealed class ExportBundleService(IApplicationStateStore stateStore) : IExportBundleService
{
    public async Task<string> ExportAsync(DiagnosticSessionRecord session, CancellationToken cancellationToken)
    {
        var exportDirectory = Path.Combine(stateStore.DataRoot, "exports");
        Directory.CreateDirectory(exportDirectory);

        var exportRoot = Path.Combine(exportDirectory, $"bundle-{session.CreatedAt:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(exportRoot);

        var sessionPath = Path.Combine(exportRoot, "session.json");
        await File.WriteAllTextAsync(
            sessionPath,
            JsonSerializer.Serialize(session, JsonDefaults.Options),
            Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);

        var summaryPath = Path.Combine(exportRoot, "summary.txt");
        var summaryBuilder = new StringBuilder();
        summaryBuilder.AppendLine($"Session: {session.SessionId}");
        summaryBuilder.AppendLine($"Captured: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        summaryBuilder.AppendLine($"Machine: {session.Snapshot.SystemManufacturer} {session.Snapshot.SystemModel}");
        summaryBuilder.AppendLine($"OS: {session.Snapshot.OsVersion}");
        summaryBuilder.AppendLine();
        foreach (var finding in session.Findings)
        {
            summaryBuilder.AppendLine($"[{finding.Severity}] {finding.Title} ({finding.ConfidencePercent}%)");
            summaryBuilder.AppendLine(finding.Summary);
            summaryBuilder.AppendLine();
        }

        await File.WriteAllTextAsync(summaryPath, summaryBuilder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        var zipPath = $"{exportRoot}.zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(exportRoot, zipPath, CompressionLevel.Optimal, false);
        return zipPath;
    }
}
