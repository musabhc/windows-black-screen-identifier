using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Core.Utilities;
using BlackScreenIdentifier.Actions.Infrastructure;
using Microsoft.Win32;

namespace BlackScreenIdentifier.Actions.Actions;

public sealed class RemediationService(
    IApplicationStateStore stateStore,
    ProcessRunner processRunner) : IRemediationService
{
    public IReadOnlyList<RemediationActionDescriptor> BuildCatalog(DiagnosticSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var actions = new List<RemediationActionDescriptor>();

        if (findings.Any(finding => finding.RecommendedActionIds.Contains("prepare-dumps", StringComparer.OrdinalIgnoreCase)))
        {
            actions.Add(new RemediationActionDescriptor
            {
                Id = "prepare-dumps",
                Title = "Dump hazırlığını güçlendir",
                Summary = "Crash dump ayarlarını zenginleştirip sonraki hata için daha fazla kanıt bırak.",
                Preview = "CrashDumpEnabled=7, AlwaysKeepMemoryDump=1, MinidumpDir doğrulaması.",
                Area = FindingArea.Dumps,
                RiskLevel = RiskLevel.Safe,
                RequiresElevation = true,
                IsReversible = true,
                IsRecommended = true
            });
        }

        if (!snapshot.CaptureState.IsArmed)
        {
            actions.Add(new RemediationActionDescriptor
            {
                Id = "arm-boot-capture",
                Title = "Sonraki açılış için boot capture kur",
                Summary = "Boot logging ve post-boot ingest görevini kurarak sonraki cold-boot denemesini yakala.",
                Preview = "bcdedit bootlog açılır, tek kullanımlık görev yazılır, reboot sonrası ingest yapılır.",
                Area = FindingArea.Capture,
                RiskLevel = RiskLevel.Safe,
                RequiresElevation = true,
                IsReversible = true,
                IsRecommended = true
            });
        }
        else
        {
            actions.Add(new RemediationActionDescriptor
            {
                Id = "cleanup-boot-capture",
                Title = "Mevcut boot capture kurulumunu temizle",
                Summary = "Kurulu ingest görevi ve boot logging ayarını kaldır.",
                Preview = "Scheduled task silinir, bootlog ayarı eski haline döndürülür.",
                Area = FindingArea.Capture,
                RiskLevel = RiskLevel.Safe,
                RequiresElevation = true,
                IsReversible = false,
                IsRecommended = false
            });
        }

        if (findings.Any(finding => finding.Id == "aehd-boot-driver"))
        {
            actions.Add(new RemediationActionDescriptor
            {
                Id = "set-aehd-demand-start",
                Title = "`aehd` başlangıç türünü demand yap",
                Summary = "Android Emulator hypervisor sürücüsünü boot/system start yerine isteğe bağlı başlat.",
                Preview = "`sc config aehd start= demand` uygulanır. Android emulator ihtiyaç duyduğunda servis yeniden etkinleştirilebilir.",
                Area = FindingArea.Drivers,
                RiskLevel = RiskLevel.Medium,
                RequiresElevation = true,
                IsReversible = true,
                IsRecommended = true
            });
        }

        return actions;
    }

    public async Task<ActionResult> ApplyAsync(string actionId, CancellationToken cancellationToken)
    {
        return actionId switch
        {
            "prepare-dumps" => await ApplyPrepareDumpsAsync(cancellationToken).ConfigureAwait(false),
            "arm-boot-capture" => await ApplyArmBootCaptureAsync(cancellationToken).ConfigureAwait(false),
            "cleanup-boot-capture" => await ApplyCleanupBootCaptureAsync(cancellationToken).ConfigureAwait(false),
            "set-aehd-demand-start" => await ApplyAehdDemandStartAsync(cancellationToken).ConfigureAwait(false),
            _ => new ActionResult { ActionId = actionId, Message = "Bilinmeyen aksiyon.", Succeeded = false }
        };
    }

    public async Task<ActionResult> RollbackAsync(string actionId, CancellationToken cancellationToken)
    {
        return actionId switch
        {
            "prepare-dumps" => await RollbackPrepareDumpsAsync(cancellationToken).ConfigureAwait(false),
            "arm-boot-capture" or "cleanup-boot-capture" => await ApplyCleanupBootCaptureAsync(cancellationToken).ConfigureAwait(false),
            "set-aehd-demand-start" => await RollbackAehdAsync(cancellationToken).ConfigureAwait(false),
            _ => new ActionResult { ActionId = actionId, Message = "Bu aksiyon için rollback tanımlı değil.", Succeeded = false }
        };
    }

    private async Task<ActionResult> ApplyPrepareDumpsAsync(CancellationToken cancellationToken)
    {
        using var crashKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
        var rollback = new RollbackRecord
        {
            ActionId = "prepare-dumps",
            Values =
            {
                ["CrashDumpEnabled"] = ReadValue(crashKey, "CrashDumpEnabled"),
                ["AlwaysKeepMemoryDump"] = ReadValue(crashKey, "AlwaysKeepMemoryDump"),
                ["LogEvent"] = ReadValue(crashKey, "LogEvent"),
                ["Overwrite"] = ReadValue(crashKey, "Overwrite"),
                ["MinidumpDir"] = ReadValue(crashKey, "MinidumpDir")
            }
        };

        await stateStore.SaveRollbackRecordAsync(rollback, cancellationToken).ConfigureAwait(false);

        crashKey.SetValue("CrashDumpEnabled", 7, RegistryValueKind.DWord);
        crashKey.SetValue("AlwaysKeepMemoryDump", 1, RegistryValueKind.DWord);
        crashKey.SetValue("LogEvent", 1, RegistryValueKind.DWord);
        crashKey.SetValue("Overwrite", 1, RegistryValueKind.DWord);
        crashKey.SetValue("MinidumpDir", @"%SystemRoot%\Minidump", RegistryValueKind.ExpandString);

        return new ActionResult
        {
            ActionId = "prepare-dumps",
            Succeeded = true,
            Message = "Dump hazırlığı güçlendirildi."
        };
    }

    private async Task<ActionResult> RollbackPrepareDumpsAsync(CancellationToken cancellationToken)
    {
        var rollback = await stateStore.GetLatestRollbackAsync("prepare-dumps", cancellationToken).ConfigureAwait(false);
        if (rollback is null)
        {
            return new ActionResult { ActionId = "prepare-dumps", Message = "Rollback kaydı bulunamadı.", Succeeded = false };
        }

        using var crashKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
        WriteIntValue(crashKey, "CrashDumpEnabled", rollback.Values.GetValueOrDefault("CrashDumpEnabled", "0"));
        WriteIntValue(crashKey, "AlwaysKeepMemoryDump", rollback.Values.GetValueOrDefault("AlwaysKeepMemoryDump", "0"));
        WriteIntValue(crashKey, "LogEvent", rollback.Values.GetValueOrDefault("LogEvent", "0"));
        WriteIntValue(crashKey, "Overwrite", rollback.Values.GetValueOrDefault("Overwrite", "0"));
        crashKey.SetValue("MinidumpDir", rollback.Values.GetValueOrDefault("MinidumpDir", @"%SystemRoot%\Minidump"), RegistryValueKind.ExpandString);

        return new ActionResult
        {
            ActionId = "prepare-dumps",
            Succeeded = true,
            Message = "Dump ayarları geri alındı."
        };
    }

    private async Task<ActionResult> ApplyArmBootCaptureAsync(CancellationToken cancellationToken)
    {
        var previousState = await stateStore.GetCaptureStateAsync(cancellationToken).ConfigureAwait(false);
        var rollback = new RollbackRecord
        {
            ActionId = "arm-boot-capture",
            Values =
            {
                ["WasArmed"] = previousState.IsArmed.ToString(),
                ["BootLogEnabled"] = previousState.BootLogEnabled.ToString()
            }
        };

        await stateStore.SaveRollbackRecordAsync(rollback, cancellationToken).ConfigureAwait(false);

        await processRunner.RunAsync("bcdedit", "/set {current} bootlog Yes", cancellationToken).ConfigureAwait(false);
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path bulunamadı.");
        var taskArguments = $"/Create /TN \"{ApplicationMetadata.CaptureTaskName}\" /TR \"\\\"{executablePath}\\\" --post-boot-ingest\" /SC ONLOGON /RL HIGHEST /F";
        var taskResult = await processRunner.RunAsync("schtasks", taskArguments, cancellationToken).ConfigureAwait(false);

        if (!taskResult.Succeeded)
        {
            return new ActionResult
            {
                ActionId = "arm-boot-capture",
                Succeeded = false,
                Message = $"Görev oluşturulamadı: {taskResult.StandardError}"
            };
        }

        await stateStore.SaveCaptureStateAsync(new CaptureState
        {
            IsArmed = true,
            BootLogEnabled = true,
            ScheduledTaskName = ApplicationMetadata.CaptureTaskName,
            ArmedAt = DateTimeOffset.Now
        }, cancellationToken).ConfigureAwait(false);

        return new ActionResult
        {
            ActionId = "arm-boot-capture",
            Succeeded = true,
            RequiresRestart = true,
            Message = "Boot capture kuruldu. Sonraki cold-boot denemesinden sonra ingest otomatik çalışacak."
        };
    }

    private async Task<ActionResult> ApplyCleanupBootCaptureAsync(CancellationToken cancellationToken)
    {
        await processRunner.RunAsync("schtasks", $"/Delete /TN \"{ApplicationMetadata.CaptureTaskName}\" /F", cancellationToken).ConfigureAwait(false);
        await processRunner.RunAsync("bcdedit", "/deletevalue {current} bootlog", cancellationToken).ConfigureAwait(false);
        await stateStore.SaveCaptureStateAsync(new CaptureState(), cancellationToken).ConfigureAwait(false);

        return new ActionResult
        {
            ActionId = "cleanup-boot-capture",
            Succeeded = true,
            Message = "Boot capture kurulumu temizlendi."
        };
    }

    private async Task<ActionResult> ApplyAehdDemandStartAsync(CancellationToken cancellationToken)
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\aehd");
        var rollback = new RollbackRecord
        {
            ActionId = "set-aehd-demand-start",
            Values =
            {
                ["Start"] = ReadValue(serviceKey, "Start")
            }
        };

        await stateStore.SaveRollbackRecordAsync(rollback, cancellationToken).ConfigureAwait(false);
        var result = await processRunner.RunAsync("sc", "config aehd start= demand", cancellationToken).ConfigureAwait(false);

        return new ActionResult
        {
            ActionId = "set-aehd-demand-start",
            Succeeded = result.Succeeded,
            Message = result.Succeeded
                ? "`aehd` başlangıç türü demand olarak ayarlandı."
                : $"`aehd` güncellenemedi: {result.StandardError}"
        };
    }

    private async Task<ActionResult> RollbackAehdAsync(CancellationToken cancellationToken)
    {
        var rollback = await stateStore.GetLatestRollbackAsync("set-aehd-demand-start", cancellationToken).ConfigureAwait(false);
        if (rollback is null)
        {
            return new ActionResult { ActionId = "set-aehd-demand-start", Succeeded = false, Message = "Rollback kaydı bulunamadı." };
        }

        var startMode = rollback.Values.GetValueOrDefault("Start", "1") switch
        {
            "0" => "boot",
            "1" => "system",
            "2" => "auto",
            "3" => "demand",
            "4" => "disabled",
            _ => "system"
        };

        var result = await processRunner.RunAsync("sc", $"config aehd start= {startMode}", cancellationToken).ConfigureAwait(false);
        return new ActionResult
        {
            ActionId = "set-aehd-demand-start",
            Succeeded = result.Succeeded,
            Message = result.Succeeded ? "`aehd` başlangıç türü geri alındı." : result.StandardError
        };
    }

    private static string ReadValue(RegistryKey? key, string name)
    {
        return key?.GetValue(name)?.ToString() ?? string.Empty;
    }

    private static void WriteIntValue(RegistryKey key, string name, string value)
    {
        if (int.TryParse(value, out var parsed))
        {
            key.SetValue(name, parsed, RegistryValueKind.DWord);
        }
    }
}
