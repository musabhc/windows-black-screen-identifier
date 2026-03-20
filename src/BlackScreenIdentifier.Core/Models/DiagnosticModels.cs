using BlackScreenIdentifier.Core.Enums;

namespace BlackScreenIdentifier.Core.Models;

public sealed class DiagnosticSnapshot
{
    public string MachineName { get; set; } = Environment.MachineName;
    public string OsVersion { get; set; } = string.Empty;
    public string SystemManufacturer { get; set; } = string.Empty;
    public string SystemModel { get; set; } = string.Empty;
    public bool IsElevated { get; set; }
    public SnapshotCollectionLevel CollectionLevel { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<DeviceRecord> DisplayAdapters { get; set; } = [];
    public List<DeviceRecord> NetworkAdapters { get; set; } = [];
    public List<ServiceRecord> TrackedServices { get; set; } = [];
    public List<StructuredEventRecord> RecentEvents { get; set; } = [];
    public List<BootAttempt> BootAttempts { get; set; } = [];
    public PowerProfile PowerProfile { get; set; } = new();
    public CrashDumpSettings CrashDumpSettings { get; set; } = new();
    public CaptureState CaptureState { get; set; } = new();
    public VersionInfo VersionInfo { get; set; } = new();
    public List<string> DiagnosticsNotes { get; set; } = [];
}

public sealed class DeviceRecord
{
    public string InstanceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
}

public sealed class ServiceRecord
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartMode { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public bool IsDriver { get; set; }
}

public sealed class StructuredEventRecord
{
    public string LogName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string? Level { get; set; }
    public DateTimeOffset TimeCreated { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BootAttempt
{
    public int Sequence { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string AttemptLabel { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool HasRecoverySignal { get; set; }
    public List<string> FailedDriverNames { get; set; } = [];
    public List<string> TimedOutServices { get; set; } = [];
    public List<string> WlanSignals { get; set; } = [];
    public List<string> NotableEvents { get; set; } = [];
}

public sealed class PowerProfile
{
    public bool HibernationEnabled { get; set; }
    public bool FastStartupEnabled { get; set; }
    public List<string> AvailableSleepStates { get; set; } = [];
    public string RawPowerCfgOutput { get; set; } = string.Empty;
}

public sealed class CrashDumpSettings
{
    public int CrashDumpEnabled { get; set; }
    public string DumpFile { get; set; } = string.Empty;
    public string MinidumpDirectory { get; set; } = string.Empty;
    public bool AlwaysKeepMemoryDump { get; set; }
    public bool HasMemoryDumpFile { get; set; }
    public bool HasMinidumps { get; set; }
}

public sealed class CaptureState
{
    public bool IsArmed { get; set; }
    public bool BootLogEnabled { get; set; }
    public string ScheduledTaskName { get; set; } = string.Empty;
    public DateTimeOffset? ArmedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VersionInfo
{
    public string CurrentVersion { get; set; } = "0.0.0";
    public string LatestVersion { get; set; } = "0.0.0";
    public UpdateCheckStatus Status { get; set; } = UpdateCheckStatus.Unknown;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public sealed class Finding
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public FindingArea Area { get; set; }
    public int ConfidencePercent { get; set; }
    public bool IsSeededForCurrentMachine { get; set; }
    public List<FindingEvidence> Evidence { get; set; } = [];
    public List<string> RecommendedActionIds { get; set; } = [];
}

public sealed class FindingEvidence
{
    public string Source { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
}

public sealed class RemediationActionDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public FindingArea Area { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public bool RequiresElevation { get; set; }
    public bool IsReversible { get; set; }
    public bool IsRecommended { get; set; }
}

public sealed class ActionResult
{
    public bool Succeeded { get; set; }
    public bool RequiresRestart { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class RollbackRecord
{
    public string ActionId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DiagnosticSessionRecord
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DiagnosticSnapshot Snapshot { get; set; } = new();
    public List<Finding> Findings { get; set; } = [];
    public List<RemediationActionDescriptor> Actions { get; set; } = [];
}
