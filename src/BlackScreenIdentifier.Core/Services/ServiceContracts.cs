using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;

namespace BlackScreenIdentifier.Core.Services;

public interface IDiagnosticCollector
{
    Task<DiagnosticSnapshot> CollectAsync(SnapshotCollectionLevel level, CancellationToken cancellationToken);
}

public interface IDiagnosticAnalyzer
{
    IReadOnlyList<Finding> Analyze(DiagnosticSnapshot snapshot);
}

public interface IRemediationService
{
    IReadOnlyList<RemediationActionDescriptor> BuildCatalog(DiagnosticSnapshot snapshot, IReadOnlyList<Finding> findings);
    Task<ActionResult> ApplyAsync(string actionId, CancellationToken cancellationToken);
    Task<ActionResult> RollbackAsync(string actionId, CancellationToken cancellationToken);
}

public interface IApplicationStateStore
{
    string DataRoot { get; }
    Task SaveSessionAsync(DiagnosticSessionRecord session, CancellationToken cancellationToken);
    Task<IReadOnlyList<DiagnosticSessionRecord>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken);
    Task SaveCaptureStateAsync(CaptureState state, CancellationToken cancellationToken);
    Task<CaptureState> GetCaptureStateAsync(CancellationToken cancellationToken);
    Task SaveRollbackRecordAsync(RollbackRecord record, CancellationToken cancellationToken);
    Task<RollbackRecord?> GetLatestRollbackAsync(string actionId, CancellationToken cancellationToken);
}

public interface IUpdateService
{
    Task<VersionInfo> CheckAsync(CancellationToken cancellationToken);
}

public interface IExportBundleService
{
    Task<string> ExportAsync(DiagnosticSessionRecord session, CancellationToken cancellationToken);
}
