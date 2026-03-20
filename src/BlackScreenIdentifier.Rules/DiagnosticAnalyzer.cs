using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;

namespace BlackScreenIdentifier.Rules;

public sealed class DiagnosticAnalyzer : IDiagnosticAnalyzer
{
    public IReadOnlyList<Finding> Analyze(DiagnosticSnapshot snapshot)
    {
        var findings = new List<Finding>();

        AddHybridGraphicsFinding(snapshot, findings);
        AddAehdFinding(snapshot, findings);
        AddMediaTekFinding(snapshot, findings);
        AddServiceTimeoutFinding(snapshot, findings);
        AddWudfNoiseFinding(snapshot, findings);
        AddDumpFinding(snapshot, findings);
        AddRecoveryFinding(snapshot, findings);
        AddCaptureRecommendation(snapshot, findings);

        return findings
            .OrderByDescending(finding => finding.Severity)
            .ThenByDescending(finding => finding.ConfidencePercent)
            .ToList();
    }

    private static void AddHybridGraphicsFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var hasAmd = snapshot.DisplayAdapters.Any(adapter => adapter.Description.Contains("AMD", StringComparison.OrdinalIgnoreCase));
        var hasNvidia = snapshot.DisplayAdapters.Any(adapter => adapter.Description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (!hasAmd || !hasNvidia)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "hybrid-graphics-handoff",
            Title = "Hibrit grafik handoff zinciri cold-boot için aday kök neden",
            Summary = "AMD iGPU + NVIDIA dGPU kombinasyonu boot sırasında ekran handoff sorunlarına yatkın. Sorun çalışma sırasında değil, açılışta görünüyorsa öncelikli adaylardan biridir.",
            WhyItMatters = "Cold-boot siyah ekran, login öncesi ya da çok erken oturum anında oluşuyorsa GPU el değiştirmesi ve sürücü sıralaması ağ semptomlarından daha olası kök neden olabilir.",
            Severity = FindingSeverity.High,
            Area = FindingArea.Graphics,
            ConfidencePercent = 78,
            IsSeededForCurrentMachine = snapshot.SystemManufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase),
            RecommendedActionIds = ["arm-boot-capture", "prepare-dumps"],
            Evidence =
            [
                new FindingEvidence
                {
                    Source = "display-adapters",
                    Label = "AMD adaptörü",
                    Detail = snapshot.DisplayAdapters.First(adapter => adapter.Description.Contains("AMD", StringComparison.OrdinalIgnoreCase)).Description
                },
                new FindingEvidence
                {
                    Source = "display-adapters",
                    Label = "NVIDIA adaptörü",
                    Detail = snapshot.DisplayAdapters.First(adapter => adapter.Description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)).Description
                }
            ]
        });
    }

    private static void AddAehdFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var matches = snapshot.RecentEvents
            .Where(record => record.Message.Contains("aehd", StringComparison.OrdinalIgnoreCase) ||
                             record.Data.Values.Any(value => value.Contains("aehd", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(record => record.TimeCreated)
            .ToList();

        if (matches.Count == 0)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "aehd-boot-driver",
            Title = "Android Emulator hypervisor sürücüsü boot sırasında yüklenemiyor",
            Summary = "Tekrarlayan `aehd` system-start/boot-start hataları cold-boot zincirine gereksiz sürücü gürültüsü sokuyor. Bu sürücü opsiyonelse başlangıç türünün düşürülmesi güvenli aday aksiyonlardan biri.",
            WhyItMatters = "Sistem başlangıcında başarısız olan üçüncü parti kernel sürücüleri siyah ekranın tek sebebi olmayabilir ama boot akışını bozup birincil sorunu tetikleyebilir veya görünür hale getirebilir.",
            Severity = matches.Count >= 2 ? FindingSeverity.High : FindingSeverity.Medium,
            Area = FindingArea.Drivers,
            ConfidencePercent = matches.Count >= 3 ? 92 : 75,
            IsSeededForCurrentMachine = true,
            RecommendedActionIds = ["set-aehd-demand-start", "arm-boot-capture"],
            Evidence = matches.Take(3).Select(record => new FindingEvidence
            {
                Source = $"{record.ProviderName}#{record.EventId}",
                Label = record.TimeCreated.LocalDateTime.ToString("g"),
                Detail = string.IsNullOrWhiteSpace(record.Message) ? "aehd sinyali" : record.Message.Trim()
            }).ToList()
        });
    }

    private static void AddMediaTekFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var hasMediaTek = snapshot.NetworkAdapters.Any(adapter =>
            adapter.Description.Contains("MediaTek", StringComparison.OrdinalIgnoreCase) ||
            adapter.Manufacturer.Contains("MediaTek", StringComparison.OrdinalIgnoreCase));

        if (!hasMediaTek)
        {
            return;
        }

        var bootSignals = snapshot.BootAttempts.SelectMany(attempt => attempt.WlanSignals).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        findings.Add(new Finding
        {
            Id = "mediatek-wlan-startup",
            Title = "MediaTek WLAN başlangıç zinciri ikincil semptom üretmiş olabilir",
            Summary = "Wi‑Fi bağlanamama mesajı kök neden olmak zorunda değil; ancak MediaTek MT7922 ailesi boot sonrası ağa geç geliyorsa siyah ekran zincirinin yan etkisini görünür kılabilir.",
            WhyItMatters = "Ağ semptomu siyah ekranın nedeni değil sonucu olabilir. Bu nedenle uygulama bunu ikincil ama korelasyon değeri yüksek bir bulgu olarak işaretler.",
            Severity = FindingSeverity.Medium,
            Area = FindingArea.Network,
            ConfidencePercent = bootSignals.Count > 0 ? 74 : 58,
            IsSeededForCurrentMachine = true,
            RecommendedActionIds = ["arm-boot-capture"],
            Evidence = bootSignals.Take(3).Select(signal => new FindingEvidence
            {
                Source = "wlan-autoconfig",
                Label = "Boot sinyali",
                Detail = signal
            }).ToList()
        });
    }

    private static void AddServiceTimeoutFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var timeouts = snapshot.BootAttempts.SelectMany(attempt => attempt.TimedOutServices).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (timeouts.Count == 0)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "service-timeouts",
            Title = "Üçüncü parti servis timeout olayları boot gürültüsü üretiyor",
            Summary = "Google Update ve benzeri servis timeout’ları asıl kök neden olmayabilir; uygulama bunları boot gürültüsü olarak ayırıp önceliği düşük tutar.",
            WhyItMatters = "Servis timeout’ları gerçek kök nedeni gizleyebilir. Önceliği doğru kurmak için bu olaylar düşük/orta etkiyle etiketlenir.",
            Severity = FindingSeverity.Low,
            Area = FindingArea.Services,
            ConfidencePercent = 41,
            RecommendedActionIds = ["arm-boot-capture"],
            Evidence = timeouts.Select(service => new FindingEvidence
            {
                Source = "service-control-manager",
                Label = "Timeout servisi",
                Detail = service
            }).ToList()
        });
    }

    private static void AddWudfNoiseFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var wudfEvents = snapshot.RecentEvents
            .Where(record => record.Message.Contains("WUDFRd", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (wudfEvents.Count == 0)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "wudfrd-noise",
            Title = "WUDFRd cihaz yükleme hataları muhtemelen yan gürültü",
            Summary = "HID/webcam sınıfındaki WUDFRd yükleme hataları boot anında gürültü üretir fakat tek başına black screen açıklaması zayıftır.",
            WhyItMatters = "Yanlış kök neden seçiminden kaçınmak için bu bulgu bilgi amaçlı tutulur ve agresif otomasyona sokulmaz.",
            Severity = FindingSeverity.Informational,
            Area = FindingArea.Drivers,
            ConfidencePercent = 33,
            Evidence = wudfEvents.Select(record => new FindingEvidence
            {
                Source = $"{record.ProviderName}#{record.EventId}",
                Label = record.TimeCreated.LocalDateTime.ToString("g"),
                Detail = record.Message
            }).ToList()
        });
    }

    private static void AddDumpFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        if (snapshot.CrashDumpSettings.CrashDumpEnabled != 0 &&
            (snapshot.CrashDumpSettings.HasMemoryDumpFile || snapshot.CrashDumpSettings.HasMinidumps))
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "dump-coverage",
            Title = "Crash dump kapsamı zayıf; cold-boot hatası görünür kalıyor",
            Summary = "Windows normal çalışırken çökmedikçe minidump üretmiyor ve boot anındaki siyah ekran için doğrudan kanıt bırakmıyor. Dump hazırlığını artırmak ve boot-capture kullanmak gerekli.",
            WhyItMatters = "Tanı gücünü artırmak için dump yapılandırması ile boot capture birlikte kurulmalı. Tek başına dump beklemek bu senaryoda yeterli değil.",
            Severity = FindingSeverity.Medium,
            Area = FindingArea.Dumps,
            ConfidencePercent = 80,
            RecommendedActionIds = ["prepare-dumps", "arm-boot-capture"],
            Evidence =
            [
                new FindingEvidence
                {
                    Source = "crash-control",
                    Label = "CrashDumpEnabled",
                    Detail = snapshot.CrashDumpSettings.CrashDumpEnabled.ToString()
                },
                new FindingEvidence
                {
                    Source = "filesystem",
                    Label = "Dump dosyaları",
                    Detail = snapshot.CrashDumpSettings.HasMinidumps || snapshot.CrashDumpSettings.HasMemoryDumpFile
                        ? "Mevcut"
                        : "Dump dosyası bulunmadı"
                }
            ]
        });
    }

    private static void AddRecoveryFinding(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var attempts = snapshot.BootAttempts.Where(attempt => attempt.HasRecoverySignal).ToList();
        if (attempts.Count == 0)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "recovery-trace",
            Title = "Boot zincirinde recovery/startup repair izi var",
            Summary = "Sistem en az bir açılış denemesinde onarım veya recovery izleri göstermiş. Bu durum cold-boot akışının gerçekten bozulduğunu doğrulayan güçlü bir sinyal.",
            WhyItMatters = "Kullanıcı gözlemi ile log zinciri uyuşuyorsa teşhis daha güvenilir hale gelir; bu bulgu siyah ekranın yalnızca algısal olmadığını destekler.",
            Severity = FindingSeverity.High,
            Area = FindingArea.Recovery,
            ConfidencePercent = 71,
            RecommendedActionIds = ["arm-boot-capture", "prepare-dumps"],
            Evidence = attempts.Select(attempt => new FindingEvidence
            {
                Source = "boot-attempt",
                Label = attempt.AttemptLabel,
                Detail = attempt.Summary,
                Timestamp = attempt.StartedAt
            }).ToList()
        });
    }

    private static void AddCaptureRecommendation(DiagnosticSnapshot snapshot, ICollection<Finding> findings)
    {
        var alreadyRecommended = findings.Any(finding => finding.RecommendedActionIds.Contains("arm-boot-capture", StringComparer.OrdinalIgnoreCase));
        if (snapshot.CaptureState.IsArmed || alreadyRecommended)
        {
            return;
        }

        findings.Add(new Finding
        {
            Id = "capture-recommendation",
            Title = "Kesin boot korelasyonu için rehberli capture önerilir",
            Summary = "Pasif loglar temel adayları çıkarıyor ama sonraki cold-boot denemesinde otomatik ingest çalışan capture modu daha nokta atışı sonuç verecek.",
            WhyItMatters = "Bu uygulamanın v1’de en güçlü yönü reboot destekli post-boot ingest. Sorun boot’a özgü olduğu için ikinci faz kanıt toplama büyük değer katıyor.",
            Severity = FindingSeverity.Informational,
            Area = FindingArea.Capture,
            ConfidencePercent = 60,
            RecommendedActionIds = ["arm-boot-capture"]
        });
    }
}
