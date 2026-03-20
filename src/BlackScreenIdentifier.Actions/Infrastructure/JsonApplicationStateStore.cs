using System.Text.Json;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Core.Utilities;

namespace BlackScreenIdentifier.Actions.Infrastructure;

public sealed class JsonApplicationStateStore : IApplicationStateStore
{
    private readonly string sessionsDirectory;
    private readonly string stateDirectory;
    private readonly string rollbacksDirectory;

    public JsonApplicationStateStore()
    {
        DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlackScreenIdentifier");

        sessionsDirectory = Path.Combine(DataRoot, "sessions");
        stateDirectory = Path.Combine(DataRoot, "state");
        rollbacksDirectory = Path.Combine(DataRoot, "rollbacks");

        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(sessionsDirectory);
        Directory.CreateDirectory(stateDirectory);
        Directory.CreateDirectory(rollbacksDirectory);
    }

    public string DataRoot { get; }

    public async Task SaveSessionAsync(DiagnosticSessionRecord session, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(sessionsDirectory, $"{session.CreatedAt:yyyyMMdd-HHmmss}-{session.SessionId}.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, session, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DiagnosticSessionRecord>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(sessionsDirectory, "*.json")
            .OrderByDescending(path => path)
            .Take(count)
            .ToList();

        var sessions = new List<DiagnosticSessionRecord>();
        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            var session = await JsonSerializer.DeserializeAsync<DiagnosticSessionRecord>(stream, JsonDefaults.Options, cancellationToken)
                .ConfigureAwait(false);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    public async Task SaveCaptureStateAsync(CaptureState state, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(stateDirectory, "capture-state.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CaptureState> GetCaptureStateAsync(CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(stateDirectory, "capture-state.json");
        if (!File.Exists(filePath))
        {
            return new CaptureState();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<CaptureState>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false)
               ?? new CaptureState();
    }

    public async Task SaveRollbackRecordAsync(RollbackRecord record, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(rollbacksDirectory, $"{record.ActionId}-{record.CreatedAt:yyyyMMdd-HHmmss}.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, record, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RollbackRecord?> GetLatestRollbackAsync(string actionId, CancellationToken cancellationToken)
    {
        var file = Directory.EnumerateFiles(rollbacksDirectory, $"{actionId}-*.json")
            .OrderByDescending(path => path)
            .FirstOrDefault();

        if (file is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<RollbackRecord>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }
}
