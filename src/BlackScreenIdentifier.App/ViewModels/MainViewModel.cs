using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;

namespace BlackScreenIdentifier.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDiagnosticCollector collector;
    private readonly IDiagnosticAnalyzer analyzer;
    private readonly IRemediationService remediationService;
    private readonly IApplicationStateStore stateStore;
    private readonly IUpdateService updateService;
    private readonly IExportBundleService exportBundleService;

    private DiagnosticSessionRecord? currentSession;
    private string statusText = "Hazır";
    private string updateStatusText = "Sürüm kontrolü bekliyor";
    private string versionText = "v0.1.0";
    private string machineDescriptor = "Makine bilgisi okunuyor";

    public MainViewModel(
        IDiagnosticCollector collector,
        IDiagnosticAnalyzer analyzer,
        IRemediationService remediationService,
        IApplicationStateStore stateStore,
        IUpdateService updateService,
        IExportBundleService exportBundleService)
    {
        this.collector = collector;
        this.analyzer = analyzer;
        this.remediationService = remediationService;
        this.stateStore = stateStore;
        this.updateService = updateService;
        this.exportBundleService = exportBundleService;

        QuickScanCommand = new AsyncRelayCommand(_ => RefreshAsync(SnapshotCollectionLevel.Quick));
        DeepScanCommand = new AsyncRelayCommand(_ => RefreshAsync(SnapshotCollectionLevel.Deep));
        CheckUpdatesCommand = new AsyncRelayCommand(_ => CheckUpdatesAsync(showMessage: true));
        ExportBundleCommand = new AsyncRelayCommand(_ => ExportBundleAsync(), _ => currentSession is not null);
        ApplyActionCommand = new AsyncRelayCommand(ApplyActionAsync);
        RollbackActionCommand = new AsyncRelayCommand(RollbackActionAsync);
    }

    public ObservableCollection<SummaryCardItem> SummaryCards { get; } = [];
    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];
    public ObservableCollection<ActionItemViewModel> Actions { get; } = [];
    public ObservableCollection<BootAttemptItemViewModel> BootAttempts { get; } = [];
    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public AsyncRelayCommand QuickScanCommand { get; }
    public AsyncRelayCommand DeepScanCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand ExportBundleCommand { get; }
    public AsyncRelayCommand ApplyActionCommand { get; }
    public AsyncRelayCommand RollbackActionCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string UpdateStatusText
    {
        get => updateStatusText;
        private set => SetProperty(ref updateStatusText, value);
    }

    public string VersionText
    {
        get => versionText;
        private set => SetProperty(ref versionText, value);
    }

    public string MachineDescriptor
    {
        get => machineDescriptor;
        private set => SetProperty(ref machineDescriptor, value);
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync(SnapshotCollectionLevel.Quick).ConfigureAwait(true);
        await CheckUpdatesAsync(showMessage: false).ConfigureAwait(true);
    }

    private async Task RefreshAsync(SnapshotCollectionLevel level)
    {
        try
        {
            StatusText = level == SnapshotCollectionLevel.Deep ? "Derin analiz çalışıyor" : "Hızlı tarama çalışıyor";
            var snapshot = await collector.CollectAsync(level, CancellationToken.None).ConfigureAwait(true);
            snapshot.CaptureState = await stateStore.GetCaptureStateAsync(CancellationToken.None).ConfigureAwait(true);

            var findings = analyzer.Analyze(snapshot);
            var actions = remediationService.BuildCatalog(snapshot, findings);

            currentSession = new DiagnosticSessionRecord
            {
                CreatedAt = DateTimeOffset.Now,
                Snapshot = snapshot,
                Findings = findings.ToList(),
                Actions = actions.ToList()
            };

            await stateStore.SaveSessionAsync(currentSession, CancellationToken.None).ConfigureAwait(true);
            await LoadRecentSessionsAsync().ConfigureAwait(true);

            RenderSnapshot(currentSession);
            StatusText = $"Son tarama: {currentSession.CreatedAt:dd.MM.yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"Tarama başarısız: {ex.Message}";
            MessageBox.Show(ex.Message, "Tarama başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckUpdatesAsync(bool showMessage)
    {
        var info = await updateService.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        UpdateStatusText = info.StatusMessage;
        VersionText = $"v{info.CurrentVersion}";

        if (showMessage)
        {
            MessageBox.Show(info.StatusMessage, "Sürüm durumu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExportBundleAsync()
    {
        if (currentSession is null)
        {
            return;
        }

        var path = await exportBundleService.ExportAsync(currentSession, CancellationToken.None).ConfigureAwait(true);
        MessageBox.Show($"Tanı paketi oluşturuldu:\n{path}", "Export tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ApplyActionAsync(object? parameter)
    {
        if (parameter is not ActionItemViewModel action)
        {
            return;
        }

        if (action.RequiresElevation && currentSession?.Snapshot.IsElevated != true)
        {
            var exitCode = await RunElevatedProcessAsync($"--apply {action.Id}").ConfigureAwait(true);
            if (exitCode == 0)
            {
                await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
            }

            return;
        }

        var result = await remediationService.ApplyAsync(action.Id, CancellationToken.None).ConfigureAwait(true);
        MessageBox.Show(result.Message, action.Title, MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
    }

    private async Task RollbackActionAsync(object? parameter)
    {
        if (parameter is not ActionItemViewModel action)
        {
            return;
        }

        if (action.RequiresElevation && currentSession?.Snapshot.IsElevated != true)
        {
            var exitCode = await RunElevatedProcessAsync($"--rollback {action.Id}").ConfigureAwait(true);
            if (exitCode == 0)
            {
                await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
            }

            return;
        }

        var result = await remediationService.RollbackAsync(action.Id, CancellationToken.None).ConfigureAwait(true);
        MessageBox.Show(result.Message, $"{action.Title} rollback", MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
    }

    private async Task<int?> RunElevatedProcessAsync(string arguments)
    {
        try
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path bulunamadı.");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync().ConfigureAwait(true);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Yükseltme başarısız", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void RenderSnapshot(DiagnosticSessionRecord session)
    {
        MachineDescriptor = $"{session.Snapshot.SystemManufacturer} {session.Snapshot.SystemModel}".Trim();

        SummaryCards.Clear();
        SummaryCards.Add(new SummaryCardItem("Birincil Aday", session.Findings.FirstOrDefault()?.Title ?? "Kanıt toplanıyor", "En yüksek güvenli bulgu", Brushes.DarkSlateBlue));
        SummaryCards.Add(new SummaryCardItem("Boot Denemeleri", session.Snapshot.BootAttempts.Count.ToString(), session.Snapshot.BootAttempts.FirstOrDefault()?.Summary ?? "Korelasyon yok", Brushes.DarkGoldenrod));
        SummaryCards.Add(new SummaryCardItem("Dump Kapsamı", session.Snapshot.CrashDumpSettings.HasMinidumps || session.Snapshot.CrashDumpSettings.HasMemoryDumpFile ? "Hazır" : "Yetersiz", "Cold-boot dump’a bağımlı değil; capture önerilir", Brushes.DarkCyan));
        SummaryCards.Add(new SummaryCardItem("Capture Durumu", session.Snapshot.CaptureState.IsArmed ? "Kurulu" : "Pasif", session.Snapshot.CaptureState.IsArmed ? "Sonraki boot ingest bekleniyor" : "Rehberli boot capture hazır", Brushes.IndianRed));

        Findings.Clear();
        foreach (var finding in session.Findings)
        {
            Findings.Add(FindingItemViewModel.FromFinding(finding));
        }

        Actions.Clear();
        foreach (var action in session.Actions)
        {
            Actions.Add(ActionItemViewModel.FromDescriptor(action));
        }

        BootAttempts.Clear();
        foreach (var attempt in session.Snapshot.BootAttempts.Take(6))
        {
            BootAttempts.Add(BootAttemptItemViewModel.FromAttempt(attempt));
        }
    }

    private async Task LoadRecentSessionsAsync()
    {
        var sessions = await stateStore.GetRecentSessionsAsync(6, CancellationToken.None).ConfigureAwait(true);
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(new SessionItemViewModel(
                session.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
                $"{session.Findings.Count} bulgu, {session.Actions.Count} aksiyon, {session.Snapshot.BootAttempts.Count} boot denemesi"));
        }
    }
}
