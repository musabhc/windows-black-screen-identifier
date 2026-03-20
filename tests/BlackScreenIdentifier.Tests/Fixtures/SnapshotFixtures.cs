using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;

namespace BlackScreenIdentifier.Tests.Fixtures;

internal static class SnapshotFixtures
{
    public static DiagnosticSnapshot CreateAehdHeavySnapshot()
    {
        return new DiagnosticSnapshot
        {
            SystemManufacturer = "ASUSTeK COMPUTER INC.",
            SystemModel = "G513RW",
            CollectionLevel = SnapshotCollectionLevel.Deep,
            DisplayAdapters =
            [
                new DeviceRecord { Description = "AMD Radeon(TM) Graphics", Manufacturer = "Advanced Micro Devices, Inc." },
                new DeviceRecord { Description = "NVIDIA GeForce RTX 3070 Ti Laptop GPU", Manufacturer = "NVIDIA" }
            ],
            NetworkAdapters =
            [
                new DeviceRecord { Description = "MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz Wireless LAN Card", Manufacturer = "MediaTek" }
            ],
            CrashDumpSettings = new CrashDumpSettings
            {
                CrashDumpEnabled = 0,
                HasMemoryDumpFile = false,
                HasMinidumps = false
            },
            BootAttempts =
            [
                new BootAttempt
                {
                    AttemptLabel = "Boot 1",
                    Summary = "1 sürücü problemi, 1 WLAN sinyali",
                    FailedDriverNames = ["aehd"],
                    WlanSignals = ["Kablosuz Yerel Ağ Otomatik Yapılandırma hizmeti başarıyla başlatıldı."]
                }
            ],
            RecentEvents =
            [
                new StructuredEventRecord
                {
                    ProviderName = "Service Control Manager",
                    EventId = 7026,
                    Message = "Şu önyükleme başlatma veya sistem başlatma sürücüsü yüklenmedi:\naehd",
                    TimeCreated = DateTimeOffset.Now.AddMinutes(-10)
                },
                new StructuredEventRecord
                {
                    ProviderName = "Service Control Manager",
                    EventId = 7009,
                    Message = "Google Güncelleme Hizmeti hizmetinin bağlanması beklenirken zaman aşımı oluştu.",
                    TimeCreated = DateTimeOffset.Now.AddMinutes(-9)
                }
            ]
        };
    }

    public static DiagnosticSnapshot CreateRecoverySnapshot(bool captureArmed = false)
    {
        return new DiagnosticSnapshot
        {
            SystemManufacturer = "ASUSTeK COMPUTER INC.",
            SystemModel = "G513RW",
            CollectionLevel = SnapshotCollectionLevel.Deep,
            CaptureState = new CaptureState
            {
                IsArmed = captureArmed,
                BootLogEnabled = captureArmed,
                ScheduledTaskName = captureArmed ? @"BlackScreenIdentifier\PostBootIngest" : string.Empty
            },
            DisplayAdapters =
            [
                new DeviceRecord { Description = "AMD Radeon(TM) Graphics", Manufacturer = "Advanced Micro Devices, Inc." },
                new DeviceRecord { Description = "NVIDIA GeForce RTX 3070 Ti Laptop GPU", Manufacturer = "NVIDIA" }
            ],
            NetworkAdapters =
            [
                new DeviceRecord { Description = "MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz Wireless LAN Card", Manufacturer = "MediaTek" }
            ],
            CrashDumpSettings = new CrashDumpSettings
            {
                CrashDumpEnabled = 7,
                HasMemoryDumpFile = true,
                HasMinidumps = true
            },
            BootAttempts =
            [
                new BootAttempt
                {
                    AttemptLabel = "Boot 1",
                    StartedAt = DateTimeOffset.Now.AddMinutes(-25),
                    Summary = "1 WLAN sinyali, recovery izi",
                    HasRecoverySignal = true,
                    WlanSignals = ["Kablosuz ağa bağlanılamadı."]
                }
            ],
            RecentEvents =
            [
                new StructuredEventRecord
                {
                    ProviderName = "Microsoft-Windows-WLAN-AutoConfig",
                    EventId = 4001,
                    Message = "Kablosuz ağa bağlanılamadı.",
                    TimeCreated = DateTimeOffset.Now.AddMinutes(-25)
                }
            ]
        };
    }
}
