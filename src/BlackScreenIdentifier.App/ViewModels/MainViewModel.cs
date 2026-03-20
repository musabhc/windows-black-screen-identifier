using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private DiagnosticSessionRecord? selectedSession;
    private IncidentListItemViewModel? selectedIncidentItem;
    private BootAttemptItemViewModel? selectedBootAttempt;
    private SelectedIncidentViewModel selectedIncident = SelectedIncidentViewModel.Empty;
    private CaptureStatusViewModel captureStatus = CaptureStatusViewModel.Empty;
    private ExportStatusViewModel exportStatus = ExportStatusViewModel.Empty;
    private string statusText = "Hazır";
    private string updateStatusText = "Henüz kontrol edilmedi";
    private string updateBadgeText = "Bekleniyor";
    private Brush updateAccentBrush = Brushes.SlateGray;
    private string versionText = "v0.1.0";
    private string machineDescriptor = "Makine bilgisi bekleniyor";
    private int selectedDetailTabIndex;
    private string? lastExportedSessionId;
    private string lastExportPath = string.Empty;

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
        ExportBundleCommand = new AsyncRelayCommand(_ => ExportBundleAsync(), _ => GetExportSession() is not null);
        ApplyActionCommand = new AsyncRelayCommand(ApplyActionAsync);
        RollbackActionCommand = new AsyncRelayCommand(RollbackActionAsync);
    }

    public ObservableCollection<IncidentListItemViewModel> IncidentHistory { get; } = [];
    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];
    public ObservableCollection<ActionItemViewModel> Actions { get; } = [];
    public ObservableCollection<BootAttemptItemViewModel> BootAttempts { get; } = [];
    public ObservableCollection<EvidenceGroupViewModel> EvidenceGroups { get; } = [];

    public AsyncRelayCommand QuickScanCommand { get; }
    public AsyncRelayCommand DeepScanCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand ExportBundleCommand { get; }
    public AsyncRelayCommand ApplyActionCommand { get; }
    public AsyncRelayCommand RollbackActionCommand { get; }

    public IncidentListItemViewModel? SelectedIncidentItem
    {
        get => selectedIncidentItem;
        set
        {
            if (SetProperty(ref selectedIncidentItem, value))
            {
                ApplySelectedSession(value?.Session);
            }
        }
    }

    public BootAttemptItemViewModel? SelectedBootAttempt
    {
        get => selectedBootAttempt;
        set
        {
            if (SetProperty(ref selectedBootAttempt, value))
            {
                RefreshEvidenceGroups();
            }
        }
    }

    public SelectedIncidentViewModel SelectedIncident
    {
        get => selectedIncident;
        private set => SetProperty(ref selectedIncident, value);
    }

    public CaptureStatusViewModel CaptureStatus
    {
        get => captureStatus;
        private set => SetProperty(ref captureStatus, value);
    }

    public ExportStatusViewModel ExportStatus
    {
        get => exportStatus;
        private set => SetProperty(ref exportStatus, value);
    }

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

    public string UpdateBadgeText
    {
        get => updateBadgeText;
        private set => SetProperty(ref updateBadgeText, value);
    }

    public Brush UpdateAccentBrush
    {
        get => updateAccentBrush;
        private set => SetProperty(ref updateAccentBrush, value);
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

    public int SelectedDetailTabIndex
    {
        get => selectedDetailTabIndex;
        set => SetProperty(ref selectedDetailTabIndex, value);
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
            var recentSessions = await stateStore.GetRecentSessionsAsync(10, CancellationToken.None).ConfigureAwait(true);
            RenderSessionCollections(recentSessions, currentSession.SessionId);

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
        UpdateBadgeText = info.Status switch
        {
            UpdateCheckStatus.UpToDate => "Güncel",
            UpdateCheckStatus.UpdateAvailable => "Yeni sürüm",
            UpdateCheckStatus.NoPublishedRelease => "Stable yok",
            UpdateCheckStatus.Failed => "Kontrol başarısız",
            _ => "Bilinmiyor"
        };
        UpdateAccentBrush = PresentationPalette.ForUpdate(info.Status);
        VersionText = $"v{info.CurrentVersion}";

        if (showMessage)
        {
            MessageBox.Show(info.StatusMessage, "Sürüm durumu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExportBundleAsync()
    {
        var session = GetExportSession();
        if (session is null)
        {
            return;
        }

        var path = await exportBundleService.ExportAsync(session, CancellationToken.None).ConfigureAwait(true);
        lastExportPath = path;
        lastExportedSessionId = session.SessionId;
        ExportStatus = ExportStatusViewModel.FromSession(session, lastExportedSessionId, lastExportPath);
        StatusText = $"Tanı paketi oluşturuldu: {Path.GetFileName(path)}";
        ExportBundleCommand.RaiseCanExecuteChanged();

        MessageBox.Show($"Tanı paketi oluşturuldu:\n{path}", "Export tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ApplyActionAsync(object? parameter)
    {
        if (parameter is not ActionItemViewModel action)
        {
            return;
        }

        if (action.RequiresElevation && !(currentSession?.Snapshot.IsElevated ?? selectedSession?.Snapshot.IsElevated ?? false))
        {
            var exitCode = await RunElevatedProcessAsync($"--apply {action.Id}").ConfigureAwait(true);
            if (exitCode == 0)
            {
                SelectedDetailTabIndex = 2;
                await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
            }

            return;
        }

        var result = await remediationService.ApplyAsync(action.Id, CancellationToken.None).ConfigureAwait(true);
        MessageBox.Show(result.Message, action.Title, MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        SelectedDetailTabIndex = 2;
        await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
    }

    private async Task RollbackActionAsync(object? parameter)
    {
        if (parameter is not ActionItemViewModel action)
        {
            return;
        }

        if (action.RequiresElevation && !(currentSession?.Snapshot.IsElevated ?? selectedSession?.Snapshot.IsElevated ?? false))
        {
            var exitCode = await RunElevatedProcessAsync($"--rollback {action.Id}").ConfigureAwait(true);
            if (exitCode == 0)
            {
                SelectedDetailTabIndex = 2;
                await RefreshAsync(SnapshotCollectionLevel.Deep).ConfigureAwait(true);
            }

            return;
        }

        var result = await remediationService.RollbackAsync(action.Id, CancellationToken.None).ConfigureAwait(true);
        MessageBox.Show(result.Message, $"{action.Title} rollback", MessageBoxButton.OK, result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        SelectedDetailTabIndex = 2;
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

    private void RenderSessionCollections(IReadOnlyList<DiagnosticSessionRecord> sessions, string preferredSessionId)
    {
        var orderedSessions = sessions
            .OrderByDescending(session => session.CreatedAt)
            .ToList();

        IncidentHistory.Clear();
        foreach (var session in orderedSessions)
        {
            IncidentHistory.Add(IncidentListItemViewModel.FromSession(session));
        }

        SelectedIncidentItem = IncidentHistory.FirstOrDefault(item => item.Session.SessionId == preferredSessionId)
            ?? IncidentHistory.FirstOrDefault();

        ExportBundleCommand.RaiseCanExecuteChanged();
    }

    private void ApplySelectedSession(DiagnosticSessionRecord? session)
    {
        selectedSession = session;
        MachineDescriptor = session is null
            ? "Makine bilgisi bekleniyor"
            : $"{session.Snapshot.SystemManufacturer} {session.Snapshot.SystemModel}".Trim();

        Findings.Clear();
        Actions.Clear();
        BootAttempts.Clear();

        if (session is null)
        {
            SelectedIncident = SelectedIncidentViewModel.Empty;
            CaptureStatus = CaptureStatusViewModel.Empty;
            ExportStatus = ExportStatusViewModel.Empty;
            SelectedBootAttempt = null;
            RefreshEvidenceGroups();
            ExportBundleCommand.RaiseCanExecuteChanged();
            return;
        }

        SelectedIncident = SelectedIncidentViewModel.FromSession(session);
        foreach (var finding in session.Findings)
        {
            Findings.Add(FindingItemViewModel.FromFinding(finding));
        }

        foreach (var action in session.Actions)
        {
            Actions.Add(ActionItemViewModel.FromDescriptor(action));
        }

        foreach (var attempt in session.Snapshot.BootAttempts.Take(12))
        {
            BootAttempts.Add(BootAttemptItemViewModel.FromAttempt(attempt));
        }

        CaptureStatus = CaptureStatusViewModel.FromSession(session);
        ExportStatus = ExportStatusViewModel.FromSession(session, lastExportedSessionId, lastExportPath);
        SelectedBootAttempt = BootAttempts.FirstOrDefault();
        if (SelectedBootAttempt is null)
        {
            RefreshEvidenceGroups();
        }

        ExportBundleCommand.RaiseCanExecuteChanged();
    }

    private void RefreshEvidenceGroups()
    {
        EvidenceGroups.Clear();
        if (selectedSession is null)
        {
            return;
        }

        foreach (var group in EvidenceGroupViewModel.Build(selectedSession, SelectedBootAttempt))
        {
            EvidenceGroups.Add(group);
        }
    }

    private DiagnosticSessionRecord? GetExportSession()
    {
        return selectedSession ?? currentSession ?? IncidentHistory.FirstOrDefault()?.Session;
    }
}
