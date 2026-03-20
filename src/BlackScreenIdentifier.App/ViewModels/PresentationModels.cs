using System.Windows.Media;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;

namespace BlackScreenIdentifier.App.ViewModels;

internal static class PresentationPalette
{
    public static Brush ForSeverity(FindingSeverity severity)
    {
        return severity switch
        {
            FindingSeverity.Critical => Brushes.Firebrick,
            FindingSeverity.High => Brushes.DarkOrange,
            FindingSeverity.Medium => Brushes.Teal,
            FindingSeverity.Low => Brushes.SteelBlue,
            _ => Brushes.SlateGray
        };
    }

    public static Brush ForUpdate(UpdateCheckStatus status)
    {
        return status switch
        {
            UpdateCheckStatus.UpToDate => Brushes.SeaGreen,
            UpdateCheckStatus.UpdateAvailable => Brushes.DarkOrange,
            UpdateCheckStatus.NoPublishedRelease => Brushes.SlateBlue,
            UpdateCheckStatus.Failed => Brushes.Firebrick,
            _ => Brushes.SlateGray
        };
    }
}

internal static class PresentationFormatting
{
    public static string CollectionLabel(SnapshotCollectionLevel level)
    {
        return level switch
        {
            SnapshotCollectionLevel.Quick => "Hızlı Tarama",
            SnapshotCollectionLevel.Deep => "Derin Analiz",
            SnapshotCollectionLevel.PostBootIngest => "Post-Boot Ingest",
            _ => "Tanı"
        };
    }

    public static string DumpSummary(DiagnosticSnapshot snapshot)
    {
        return snapshot.CrashDumpSettings.HasMinidumps || snapshot.CrashDumpSettings.HasMemoryDumpFile
            ? "Dump altyapısı hazır"
            : "Dump kanıtı henüz yok";
    }

    public static string CaptureSummary(DiagnosticSnapshot snapshot)
    {
        return snapshot.CaptureState.IsArmed
            ? "Capture kurulu"
            : "Capture pasif";
    }
}

public sealed class IncidentListItemViewModel
{
    public required DiagnosticSessionRecord Session { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string StatusLabel { get; init; }
    public required string PrimaryFinding { get; init; }
    public required Brush AccentBrush { get; init; }

    public static IncidentListItemViewModel FromSession(DiagnosticSessionRecord session)
    {
        var primaryFinding = session.Findings.FirstOrDefault();
        var accentBrush = primaryFinding is null
            ? Brushes.SlateGray
            : PresentationPalette.ForSeverity(primaryFinding.Severity);

        return new IncidentListItemViewModel
        {
            Session = session,
            Title = session.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
            Subtitle = $"{PresentationFormatting.CollectionLabel(session.Snapshot.CollectionLevel)} · {session.Snapshot.BootAttempts.Count} boot denemesi",
            StatusLabel = primaryFinding is null
                ? PresentationFormatting.DumpSummary(session.Snapshot)
                : $"{primaryFinding.Severity} · Güven %{primaryFinding.ConfidencePercent}",
            PrimaryFinding = primaryFinding?.Title ?? "Henüz birincil aday yok",
            AccentBrush = accentBrush
        };
    }
}

public sealed class SelectedIncidentViewModel
{
    public static SelectedIncidentViewModel Empty { get; } = new()
    {
        Title = "Son tarama bekleniyor",
        Subtitle = "Bir incident seçildiğinde detaylar burada görünür.",
        PrimaryFinding = "Henüz seçim yok",
        PrimarySummary = "Hızlı veya derin tarama tamamlandığında seçili incident özetlenecek.",
        WhyItMatters = "Teşhis, kanıtlar ve aksiyonlar tek detay panelinde toplanır.",
        ConfidenceLabel = "Güven bekleniyor",
        CollectionLabel = "Tanı",
        LatestActionLabel = "Öneri bekleniyor",
        CaptureLabel = "Capture durumu bilinmiyor",
        AccentBrush = Brushes.SlateGray,
        Facts = ["Tarama yapıldığında makine ve boot bilgileri burada listelenir."]
    };

    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string PrimaryFinding { get; init; }
    public required string PrimarySummary { get; init; }
    public required string WhyItMatters { get; init; }
    public required string ConfidenceLabel { get; init; }
    public required string CollectionLabel { get; init; }
    public required string LatestActionLabel { get; init; }
    public required string CaptureLabel { get; init; }
    public required Brush AccentBrush { get; init; }
    public required IReadOnlyList<string> Facts { get; init; }

    public static SelectedIncidentViewModel FromSession(DiagnosticSessionRecord session)
    {
        var primaryFinding = session.Findings.FirstOrDefault();
        var latestAction = session.Actions.FirstOrDefault();
        var facts = new List<string>
        {
            $"Makine: {session.Snapshot.SystemManufacturer} {session.Snapshot.SystemModel}".Trim(),
            $"İşletim sistemi: {session.Snapshot.OsVersion}",
            $"Bulgular: {session.Findings.Count}",
            $"Önerilen eylemler: {session.Actions.Count}",
            $"Boot denemeleri: {session.Snapshot.BootAttempts.Count}",
            $"Dump: {PresentationFormatting.DumpSummary(session.Snapshot)}"
        };

        return new SelectedIncidentViewModel
        {
            Title = primaryFinding?.Title ?? "Birincil aday oluşmadı",
            Subtitle = $"{session.CreatedAt:dd.MM.yyyy HH:mm} · {PresentationFormatting.CollectionLabel(session.Snapshot.CollectionLevel)}",
            PrimaryFinding = primaryFinding?.Title ?? "Bulgu yok",
            PrimarySummary = primaryFinding?.Summary ?? "Bu oturumda belirgin bir birincil aday çıkmadı.",
            WhyItMatters = primaryFinding?.WhyItMatters ?? "Daha fazla bağlam için diğer sekmelerde kanıtlar ve boot zinciri incelenebilir.",
            ConfidenceLabel = primaryFinding is null ? "Güven yok" : $"Güven %{primaryFinding.ConfidencePercent}",
            CollectionLabel = PresentationFormatting.CollectionLabel(session.Snapshot.CollectionLevel),
            LatestActionLabel = latestAction is null ? "Önerilen eylem yok" : latestAction.Title,
            CaptureLabel = PresentationFormatting.CaptureSummary(session.Snapshot),
            AccentBrush = primaryFinding is null ? Brushes.SlateGray : PresentationPalette.ForSeverity(primaryFinding.Severity),
            Facts = facts
        };
    }
}

public sealed class FindingItemViewModel
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string WhyItMatters { get; init; }
    public required string SeverityLabel { get; init; }
    public required string ConfidenceLabel { get; init; }
    public required Brush AccentBrush { get; init; }
    public required IReadOnlyList<string> EvidenceLines { get; init; }

    public static FindingItemViewModel FromFinding(Finding finding)
    {
        return new FindingItemViewModel
        {
            Title = finding.Title,
            Summary = finding.Summary,
            WhyItMatters = finding.WhyItMatters,
            SeverityLabel = finding.Severity.ToString(),
            ConfidenceLabel = $"Güven %{finding.ConfidencePercent}",
            AccentBrush = PresentationPalette.ForSeverity(finding.Severity),
            EvidenceLines = finding.Evidence.Select(evidence => $"{evidence.Label}: {evidence.Detail}").ToList()
        };
    }
}

public sealed class ActionItemViewModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Preview { get; init; }
    public required bool RequiresElevation { get; init; }
    public required string RiskLabel { get; init; }
    public required string RequirementLabel { get; init; }
    public required string AreaLabel { get; init; }

    public static ActionItemViewModel FromDescriptor(RemediationActionDescriptor descriptor)
    {
        return new ActionItemViewModel
        {
            Id = descriptor.Id,
            Title = descriptor.Title,
            Summary = descriptor.Summary,
            Preview = descriptor.Preview,
            RequiresElevation = descriptor.RequiresElevation,
            RiskLabel = descriptor.RiskLevel switch
            {
                RiskLevel.Safe => "Güvenli",
                RiskLevel.Medium => "Orta risk",
                _ => "Yüksek risk"
            },
            RequirementLabel = descriptor.RequiresElevation ? "Admin gerekir" : "Standart kullanıcı",
            AreaLabel = descriptor.Area.ToString()
        };
    }
}

public sealed class BootAttemptItemViewModel
{
    public required string Label { get; init; }
    public required string StartedAt { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Highlights { get; init; }
    public required Brush AccentBrush { get; init; }

    public static BootAttemptItemViewModel FromAttempt(BootAttempt attempt)
    {
        var highlights = new List<string>();
        highlights.AddRange(attempt.FailedDriverNames.Select(driver => $"Sürücü: {driver}"));
        highlights.AddRange(attempt.TimedOutServices.Select(service => $"Servis: {service}"));
        highlights.AddRange(attempt.WlanSignals.Select(signal => $"WLAN: {signal}"));
        highlights.AddRange(attempt.NotableEvents);

        return new BootAttemptItemViewModel
        {
            Label = attempt.AttemptLabel,
            StartedAt = attempt.StartedAt.ToString("dd.MM.yyyy HH:mm"),
            Summary = attempt.Summary,
            Highlights = highlights.Take(6).ToList(),
            AccentBrush = attempt.HasRecoverySignal
                ? Brushes.Firebrick
                : attempt.FailedDriverNames.Count > 0
                    ? Brushes.DarkOrange
                    : Brushes.SteelBlue
        };
    }
}

public sealed class EvidenceGroupViewModel
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Lines { get; init; }

    public static IReadOnlyList<EvidenceGroupViewModel> Build(DiagnosticSessionRecord session, BootAttemptItemViewModel? selectedBootAttempt)
    {
        var groups = new List<EvidenceGroupViewModel>();
        var topFindings = session.Findings.Take(3).ToList();
        if (topFindings.Count > 0)
        {
            groups.Add(new EvidenceGroupViewModel
            {
                Title = "Öne Çıkan Bulgular",
                Summary = "Birincil ve ikincil adaylar",
                Lines = topFindings
                    .Select(finding => $"{finding.Title} · Güven %{finding.ConfidencePercent}")
                    .ToList()
            });
        }

        var selectedAttempt = session.Snapshot.BootAttempts
            .FirstOrDefault(attempt => attempt.AttemptLabel == selectedBootAttempt?.Label);

        if (selectedAttempt is not null)
        {
            var lines = new List<string> { selectedAttempt.Summary };
            lines.AddRange(selectedAttempt.FailedDriverNames.Select(driver => $"Sürücü: {driver}"));
            lines.AddRange(selectedAttempt.TimedOutServices.Select(service => $"Servis timeout: {service}"));
            lines.AddRange(selectedAttempt.WlanSignals.Select(signal => $"WLAN: {signal}"));
            lines.AddRange(selectedAttempt.NotableEvents.Take(4));

            groups.Add(new EvidenceGroupViewModel
            {
                Title = selectedAttempt.AttemptLabel,
                Summary = "Seçili boot denemesinin sinyalleri",
                Lines = lines
            });
        }

        groups.Add(new EvidenceGroupViewModel
        {
            Title = "Makine Durumu",
            Summary = "Donanım ve tanı altyapısı özeti",
            Lines =
            [
                $"Makine: {session.Snapshot.SystemManufacturer} {session.Snapshot.SystemModel}".Trim(),
                $"GPU: {string.Join(", ", session.Snapshot.DisplayAdapters.Select(adapter => adapter.Description).Take(2))}",
                $"Ağ: {string.Join(", ", session.Snapshot.NetworkAdapters.Select(adapter => adapter.Description).Take(2))}",
                $"Dump: {PresentationFormatting.DumpSummary(session.Snapshot)}",
                $"Capture: {PresentationFormatting.CaptureSummary(session.Snapshot)}"
            ]
        });

        return groups;
    }
}

public sealed class CaptureStatusViewModel
{
    public static CaptureStatusViewModel Empty { get; } = new()
    {
        Title = "Capture durumu bekleniyor",
        StateLabel = "Bekleniyor",
        Summary = "Capture kurulduğunda veya kapatıldığında bu panel güncellenecek.",
        DetailLines = ["Aktif bir session seçildiğinde capture özeti burada görünür."],
        AccentBrush = Brushes.SlateGray
    };

    public required string Title { get; init; }
    public required string StateLabel { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> DetailLines { get; init; }
    public required Brush AccentBrush { get; init; }

    public static CaptureStatusViewModel FromSession(DiagnosticSessionRecord session)
    {
        var armed = session.Snapshot.CaptureState.IsArmed;
        var detailLines = new List<string>
        {
            $"Collection level: {PresentationFormatting.CollectionLabel(session.Snapshot.CollectionLevel)}",
            $"Boot logging: {(session.Snapshot.CaptureState.BootLogEnabled ? "Açık" : "Kapalı")}",
            $"Görev adı: {session.Snapshot.CaptureState.ScheduledTaskName}",
            $"Armed at: {(session.Snapshot.CaptureState.ArmedAt?.ToString("dd.MM.yyyy HH:mm") ?? "-")}"
        };

        return new CaptureStatusViewModel
        {
            Title = armed ? "Capture kurulu" : "Capture pasif",
            StateLabel = armed ? "Armed" : "Idle",
            Summary = armed
                ? "Sonraki boot sonrasında ingest bekleniyor veya son capture tamamlanıp temizlenmiş olabilir."
                : "Şu anda aktif capture kurulumu görünmüyor.",
            DetailLines = detailLines,
            AccentBrush = armed ? Brushes.DarkOrange : Brushes.SteelBlue
        };
    }
}

public sealed class ExportStatusViewModel
{
    public static ExportStatusViewModel Empty { get; } = new()
    {
        Title = "Dışa aktar bekleniyor",
        StateLabel = "Hazır değil",
        Summary = "Bir session seçildiğinde tanı paketi bu panelden üretilebilir.",
        ButtonLabel = "Tanı Paketini Oluştur",
        IsReady = false,
        DetailLines = ["Henüz seçili bir session yok."]
    };

    public required string Title { get; init; }
    public required string StateLabel { get; init; }
    public required string Summary { get; init; }
    public required string ButtonLabel { get; init; }
    public required bool IsReady { get; init; }
    public required IReadOnlyList<string> DetailLines { get; init; }

    public static ExportStatusViewModel FromSession(DiagnosticSessionRecord? session, string? lastExportedSessionId, string? lastExportPath)
    {
        if (session is null)
        {
            return Empty;
        }

        var detailLines = new List<string>
        {
            $"Session: {session.SessionId}",
            $"Oluşturulma: {session.CreatedAt:dd.MM.yyyy HH:mm:ss}",
            $"Bulgular: {session.Findings.Count}",
            $"Eylemler: {session.Actions.Count}"
        };

        if (!string.IsNullOrWhiteSpace(lastExportPath) &&
            string.Equals(lastExportedSessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            detailLines.Add($"Son paket: {lastExportPath}");
        }

        return new ExportStatusViewModel
        {
            Title = "Tanı Paketi",
            StateLabel = "Hazır",
            Summary = "Seçili session için JSON özetli sıkıştırılmış tanı paketi oluşturur.",
            ButtonLabel = "Tanı Paketini Oluştur",
            IsReady = true,
            DetailLines = detailLines
        };
    }
}
