using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using BlackScreenIdentifier.Actions.Actions;
using BlackScreenIdentifier.Actions.Infrastructure;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Tests.Fixtures;
using BlackScreenIdentifier.Rules;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Analyzer surfaces AEHD finding", AnalyzerSurfacesAehdFindingAsync),
    ("Analyzer recommends dump preparation", AnalyzerRecommendsDumpPreparationAsync),
    ("Remediation catalog includes capture and aehd actions", RemediationCatalogIncludesExpectedActionsAsync),
    ("Analyzer surfaces recovery and MediaTek findings", AnalyzerSurfacesRecoveryAndMediaTekFindingsAsync),
    ("Capture cleanup replaces arm action when capture is active", RemediationCatalogSwitchesToCleanupWhenCaptureArmedAsync),
    ("Update service parses GitHub release payload", UpdateServiceParsesReleasePayloadAsync),
    ("Export bundle creates zip with session payload", ExportBundleCreatesZipAsync)
};

var failures = new List<string>();

foreach (var (name, run) in tests)
{
    try
    {
        await run().ConfigureAwait(false);
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

return 0;

static Task AnalyzerSurfacesAehdFindingAsync()
{
    var analyzer = new DiagnosticAnalyzer();
    var findings = analyzer.Analyze(SnapshotFixtures.CreateAehdHeavySnapshot());

    Assert(findings.Any(finding => finding.Id == "aehd-boot-driver"), "aehd finding should exist.");
    return Task.CompletedTask;
}

static Task AnalyzerRecommendsDumpPreparationAsync()
{
    var analyzer = new DiagnosticAnalyzer();
    var findings = analyzer.Analyze(SnapshotFixtures.CreateAehdHeavySnapshot());

    Assert(findings.Any(finding => finding.RecommendedActionIds.Contains("prepare-dumps")), "prepare-dumps recommendation should exist.");
    return Task.CompletedTask;
}

static Task RemediationCatalogIncludesExpectedActionsAsync()
{
    var store = new FakeStateStore();
    var remediationService = new RemediationService(store, new ProcessRunner());
    var analyzer = new DiagnosticAnalyzer();
    var snapshot = SnapshotFixtures.CreateAehdHeavySnapshot();
    var findings = analyzer.Analyze(snapshot);
    var actions = remediationService.BuildCatalog(snapshot, findings);

    Assert(actions.Any(action => action.Id == "arm-boot-capture"), "arm-boot-capture action missing.");
    Assert(actions.Any(action => action.Id == "set-aehd-demand-start"), "set-aehd-demand-start action missing.");
    return Task.CompletedTask;
}

static Task AnalyzerSurfacesRecoveryAndMediaTekFindingsAsync()
{
    var analyzer = new DiagnosticAnalyzer();
    var findings = analyzer.Analyze(SnapshotFixtures.CreateRecoverySnapshot());

    Assert(findings.Any(finding => finding.Id == "recovery-trace"), "recovery-trace finding should exist.");
    Assert(findings.Any(finding => finding.Id == "mediatek-wlan-startup"), "mediatek finding should exist.");
    Assert(!findings.Any(finding => finding.Id == "dump-coverage"), "dump-coverage should not be emitted when dumps are ready.");
    return Task.CompletedTask;
}

static Task RemediationCatalogSwitchesToCleanupWhenCaptureArmedAsync()
{
    var store = new FakeStateStore();
    var remediationService = new RemediationService(store, new ProcessRunner());
    var analyzer = new DiagnosticAnalyzer();
    var snapshot = SnapshotFixtures.CreateRecoverySnapshot(captureArmed: true);
    var findings = analyzer.Analyze(snapshot);
    var actions = remediationService.BuildCatalog(snapshot, findings);

    Assert(actions.Any(action => action.Id == "cleanup-boot-capture"), "cleanup-boot-capture action missing.");
    Assert(!actions.Any(action => action.Id == "arm-boot-capture"), "arm-boot-capture should not be offered when capture is already armed.");
    return Task.CompletedTask;
}

static async Task UpdateServiceParsesReleasePayloadAsync()
{
    using var client = new HttpClient(new StubHttpMessageHandler("""
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/musabhc/windows-black-screen-identifier/releases/tag/v1.2.3"
        }
        """))
    {
        BaseAddress = new Uri("https://api.github.com/")
    };

    var service = new GitHubReleaseService(client);
    var result = await service.CheckAsync(CancellationToken.None).ConfigureAwait(false);

    Assert(result.LatestVersion == "1.2.3", "latest version should be parsed.");
    Assert(result.Status == BlackScreenIdentifier.Core.Enums.UpdateCheckStatus.UpdateAvailable, "status should be UpdateAvailable for a newer release.");
}

static async Task ExportBundleCreatesZipAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "BlackScreenIdentifier.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var store = new FakeStateStore(root);
        var exportService = new ExportBundleService(store);
        var session = new DiagnosticSessionRecord
        {
            CreatedAt = new DateTimeOffset(2026, 03, 20, 10, 30, 00, TimeSpan.Zero),
            Snapshot = SnapshotFixtures.CreateAehdHeavySnapshot(),
            Findings = new DiagnosticAnalyzer().Analyze(SnapshotFixtures.CreateAehdHeavySnapshot()).ToList(),
            Actions =
            [
                new RemediationActionDescriptor
                {
                    Id = "prepare-dumps",
                    Title = "Dump hazırlığını güçlendir"
                }
            ]
        };

        var zipPath = await exportService.ExportAsync(session, CancellationToken.None).ConfigureAwait(false);

        Assert(File.Exists(zipPath), "exported zip should exist.");
        using var archive = ZipFile.OpenRead(zipPath);
        Assert(archive.Entries.Any(entry => entry.FullName == "session.json"), "session.json should be present.");
        Assert(archive.Entries.Any(entry => entry.FullName == "summary.txt"), "summary.txt should be present.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class FakeStateStore : IApplicationStateStore
{
    private readonly List<DiagnosticSessionRecord> sessions = [];
    private readonly Dictionary<string, RollbackRecord> rollbacks = new(StringComparer.OrdinalIgnoreCase);
    private CaptureState captureState = new();

    public FakeStateStore(string? dataRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(Path.GetTempPath(), "BlackScreenIdentifier.Tests");
        Directory.CreateDirectory(DataRoot);
    }

    public string DataRoot { get; }

    public Task SaveSessionAsync(DiagnosticSessionRecord session, CancellationToken cancellationToken)
    {
        sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiagnosticSessionRecord>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<DiagnosticSessionRecord>>(sessions.Take(count).ToList());
    }

    public Task SaveCaptureStateAsync(CaptureState state, CancellationToken cancellationToken)
    {
        captureState = state;
        return Task.CompletedTask;
    }

    public Task<CaptureState> GetCaptureStateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(captureState);
    }

    public Task SaveRollbackRecordAsync(RollbackRecord record, CancellationToken cancellationToken)
    {
        rollbacks[record.ActionId] = record;
        return Task.CompletedTask;
    }

    public Task<RollbackRecord?> GetLatestRollbackAsync(string actionId, CancellationToken cancellationToken)
    {
        rollbacks.TryGetValue(actionId, out var record);
        return Task.FromResult(record);
    }
}

file sealed class StubHttpMessageHandler(string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}
