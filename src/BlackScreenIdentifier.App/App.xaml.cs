using System.Windows;
using BlackScreenIdentifier.Actions.Actions;
using BlackScreenIdentifier.Actions.Infrastructure;
using BlackScreenIdentifier.App.ViewModels;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Diagnostics.Collectors;
using BlackScreenIdentifier.Rules;
using DiagnosticsProcessRunner = BlackScreenIdentifier.Diagnostics.Infrastructure.ProcessRunner;
using ActionsProcessRunner = BlackScreenIdentifier.Actions.Infrastructure.ProcessRunner;

namespace BlackScreenIdentifier.App;

public partial class App : Application
{
    private readonly IApplicationStateStore stateStore = new JsonApplicationStateStore();
    private readonly DiagnosticsProcessRunner diagnosticsProcessRunner = new();
    private readonly ActionsProcessRunner actionsProcessRunner = new();
    private readonly IDiagnosticCollector collector;
    private readonly IDiagnosticAnalyzer analyzer = new DiagnosticAnalyzer();
    private readonly IRemediationService remediationService;
    private readonly IUpdateService updateService = new GitHubReleaseService();
    private readonly IExportBundleService exportBundleService;

    public App()
    {
        collector = new DiagnosticCollector(diagnosticsProcessRunner);
        remediationService = new RemediationService(stateStore, actionsProcessRunner);
        exportBundleService = new ExportBundleService(stateStore);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var handledExitCode = await TryHandleHeadlessModeAsync(e.Args).ConfigureAwait(true);
        if (handledExitCode.HasValue)
        {
            Shutdown(handledExitCode.Value);
            return;
        }

        var mainViewModel = new MainViewModel(
            collector,
            analyzer,
            remediationService,
            stateStore,
            updateService,
            exportBundleService);

        var window = new MainWindow(mainViewModel);
        MainWindow = window;
        window.Show();
    }

    private async Task<int?> TryHandleHeadlessModeAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0].Equals("--post-boot-ingest", StringComparison.OrdinalIgnoreCase))
        {
            var session = await CreateSessionAsync(SnapshotCollectionLevel.PostBootIngest, CancellationToken.None).ConfigureAwait(false);
            await stateStore.SaveSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            await remediationService.ApplyAsync("cleanup-boot-capture", CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (args[0].Equals("--apply", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            var result = await remediationService.ApplyAsync(args[1], CancellationToken.None).ConfigureAwait(false);
            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("--rollback", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            var result = await remediationService.RollbackAsync(args[1], CancellationToken.None).ConfigureAwait(false);
            return result.Succeeded ? 0 : 1;
        }

        return null;
    }

    private async Task<DiagnosticSessionRecord> CreateSessionAsync(SnapshotCollectionLevel level, CancellationToken cancellationToken)
    {
        var snapshot = await collector.CollectAsync(level, cancellationToken).ConfigureAwait(false);
        snapshot.CaptureState = await stateStore.GetCaptureStateAsync(cancellationToken).ConfigureAwait(false);
        var findings = analyzer.Analyze(snapshot);
        var actions = remediationService.BuildCatalog(snapshot, findings);

        return new DiagnosticSessionRecord
        {
            CreatedAt = DateTimeOffset.Now,
            Snapshot = snapshot,
            Findings = findings.ToList(),
            Actions = actions.ToList()
        };
    }
}
