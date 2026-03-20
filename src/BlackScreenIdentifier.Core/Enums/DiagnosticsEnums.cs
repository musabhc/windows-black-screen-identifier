namespace BlackScreenIdentifier.Core.Enums;

public enum SnapshotCollectionLevel
{
    Quick,
    Deep,
    PostBootIngest
}

public enum FindingSeverity
{
    Informational,
    Low,
    Medium,
    High,
    Critical
}

public enum FindingArea
{
    Boot,
    Graphics,
    Network,
    Drivers,
    Services,
    Recovery,
    Dumps,
    Capture,
    Versioning
}

public enum RiskLevel
{
    Safe,
    Medium,
    High
}

public enum UpdateCheckStatus
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    NoPublishedRelease,
    Failed
}
