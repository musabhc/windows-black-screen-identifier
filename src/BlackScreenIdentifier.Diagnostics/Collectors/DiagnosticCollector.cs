using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;
using BlackScreenIdentifier.Core.Services;
using BlackScreenIdentifier.Diagnostics.Infrastructure;
using Microsoft.Win32;

namespace BlackScreenIdentifier.Diagnostics.Collectors;

public sealed class DiagnosticCollector(ProcessRunner processRunner) : IDiagnosticCollector
{
    private static readonly string[] DeviceKeyInstance = ["instance id", "örnek kimliği"];
    private static readonly string[] DeviceKeyDescription = ["device description", "aygıt açıklaması"];
    private static readonly string[] DeviceKeyClass = ["class name", "sınıf adı"];
    private static readonly string[] DeviceKeyManufacturer = ["manufacturer name", "üretici adı"];
    private static readonly string[] DeviceKeyStatus = ["status", "durum"];
    private static readonly string[] DeviceKeyDriver = ["driver name", "sürücü adı"];

    public async Task<DiagnosticSnapshot> CollectAsync(SnapshotCollectionLevel level, CancellationToken cancellationToken)
    {
        var snapshot = new DiagnosticSnapshot
        {
            CollectionLevel = level,
            CapturedAt = DateTimeOffset.Now,
            IsElevated = IsElevated(),
            OsVersion = Environment.OSVersion.VersionString
        };

        PopulateMachineProfile(snapshot);
        PopulateCrashDumpSettings(snapshot);
        await PopulatePowerProfileAsync(snapshot, cancellationToken).ConfigureAwait(false);
        snapshot.DisplayAdapters = await CollectDevicesAsync("Display", snapshot.DiagnosticsNotes, cancellationToken).ConfigureAwait(false);
        snapshot.NetworkAdapters = await CollectDevicesAsync("Net", snapshot.DiagnosticsNotes, cancellationToken).ConfigureAwait(false);

        var systemEvents = await ReadEventsAsync(
            "System",
            "*[System[(EventID=41 or EventID=1001 or EventID=6008 or EventID=7000 or EventID=7001 or EventID=7009 or EventID=7026 or EventID=219 or EventID=10001 or EventID=10002 or EventID=4000 or EventID=4001)]]",
            160,
            snapshot.DiagnosticsNotes,
            cancellationToken).ConfigureAwait(false);

        var kernelBootEvents = await ReadEventsAsync(
            "Microsoft-Windows-Kernel-Boot/Operational",
            "*[System[(EventID=45 or EventID=51 or EventID=80 or EventID=82 or EventID=85)]]",
            100,
            snapshot.DiagnosticsNotes,
            cancellationToken).ConfigureAwait(false);

        snapshot.RecentEvents = systemEvents
            .Concat(kernelBootEvents)
            .OrderByDescending(record => record.TimeCreated)
            .ToList();

        snapshot.BootAttempts = BuildBootAttempts(snapshot.RecentEvents);
        snapshot.TrackedServices = CollectTrackedServices(snapshot.RecentEvents, snapshot.DiagnosticsNotes);

        return snapshot;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void PopulateMachineProfile(DiagnosticSnapshot snapshot)
    {
        using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        snapshot.SystemManufacturer = biosKey?.GetValue("SystemManufacturer")?.ToString() ?? string.Empty;
        snapshot.SystemModel = biosKey?.GetValue("SystemProductName")?.ToString() ?? string.Empty;
    }

    private static void PopulateCrashDumpSettings(DiagnosticSnapshot snapshot)
    {
        using var crashControl = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
        snapshot.CrashDumpSettings = new CrashDumpSettings
        {
            CrashDumpEnabled = ReadIntValue(crashControl, "CrashDumpEnabled"),
            DumpFile = crashControl?.GetValue("DumpFile")?.ToString() ?? @"%SystemRoot%\MEMORY.DMP",
            MinidumpDirectory = crashControl?.GetValue("MinidumpDir")?.ToString() ?? @"%SystemRoot%\Minidump",
            AlwaysKeepMemoryDump = ReadIntValue(crashControl, "AlwaysKeepMemoryDump") == 1,
            HasMemoryDumpFile = File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\MEMORY.DMP")),
            HasMinidumps = Directory.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Minidump")) &&
                           Directory.EnumerateFiles(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Minidump"), "*.dmp").Any()
        };
    }

    private async Task PopulatePowerProfileAsync(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var powerKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        using var hibernateKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
        var powerCfg = await processRunner.RunAsync("powercfg", "/a", cancellationToken).ConfigureAwait(false);
        snapshot.PowerProfile = new PowerProfile
        {
            FastStartupEnabled = ReadIntValue(powerKey, "HiberbootEnabled") == 1,
            HibernationEnabled = ReadIntValue(hibernateKey, "HibernateEnabled") == 1,
            RawPowerCfgOutput = powerCfg.StandardOutput.Trim()
        };

        foreach (var line in powerCfg.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("Standby", StringComparison.OrdinalIgnoreCase) || line.Contains("Hibernate", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.PowerProfile.AvailableSleepStates.Add(line.Trim());
            }
        }
    }

    private async Task<List<DeviceRecord>> CollectDevicesAsync(string className, List<string> notes, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("pnputil", $"/enum-devices /class {className}", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            notes.Add($"{className} cihaz envanteri okunamadı: {result.StandardError.Trim()}");
            return [];
        }

        var devices = new List<DeviceRecord>();
        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);

        void FlushCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            var device = new DeviceRecord
            {
                InstanceId = GetLocalizedValue(current, DeviceKeyInstance),
                Description = GetLocalizedValue(current, DeviceKeyDescription),
                ClassName = GetLocalizedValue(current, DeviceKeyClass, className),
                Manufacturer = GetLocalizedValue(current, DeviceKeyManufacturer),
                Status = GetLocalizedValue(current, DeviceKeyStatus),
                DriverName = GetLocalizedValue(current, DeviceKeyDriver)
            };

            if (!string.IsNullOrWhiteSpace(device.Description))
            {
                devices.Add(device);
            }

            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rawLine in result.StandardOutput.Split(Environment.NewLine))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushCurrent();
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            current[key] = value;
        }

        FlushCurrent();
        return devices;
    }

    private async Task<List<StructuredEventRecord>> ReadEventsAsync(
        string logName,
        string query,
        int maxCount,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var events = new List<StructuredEventRecord>();

        try
        {
            var result = await processRunner
                .RunAsync("wevtutil", $"qe \"{logName}\" /rd:true /c:{maxCount} /f:RenderedXml /q:\"{query}\"", cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                notes.Add($"{logName} okunamadı: {result.StandardError.Trim()}");
                return [];
            }

            var xml = $"<Events>{result.StandardOutput}</Events>";
            var document = XDocument.Parse(xml);
            foreach (var eventElement in document.Root?.Elements().Where(element => element.Name.LocalName == "Event") ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(ToStructuredRecord(eventElement));
            }
        }
        catch (Exception ex)
        {
            notes.Add($"{logName} okunamadı: {ex.Message}");
        }

        return events;
    }

    private static StructuredEventRecord ToStructuredRecord(XElement eventElement)
    {
        var systemNode = eventElement.Elements().FirstOrDefault(element => element.Name.LocalName == "System");
        var renderingNode = eventElement.Elements().FirstOrDefault(element => element.Name.LocalName == "RenderingInfo");
        var structured = new StructuredEventRecord
        {
            LogName = systemNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "Channel")?.Value ?? string.Empty,
            ProviderName = systemNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "Provider")?.Attribute("Name")?.Value ?? string.Empty,
            EventId = int.TryParse(systemNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "EventID")?.Value, out var eventId) ? eventId : 0,
            Level = renderingNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "Level")?.Value,
            TimeCreated = DateTimeOffset.TryParse(
                systemNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value,
                out var createdAt)
                ? createdAt
                : DateTimeOffset.MinValue
        };

        structured.Message = renderingNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "Message")?.Value ?? string.Empty;

        try
        {
            var dataElements = eventElement.Descendants()
                .Where(element => element.Name.LocalName is "Data" or "Binary");

            foreach (var element in dataElements)
            {
                var key = element.Attribute("Name")?.Value;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = $"data{structured.Data.Count}";
                }

                structured.Data[key] = element.Value;
            }
        }
        catch
        {
        }

        return structured;
    }

    private static List<ServiceRecord> CollectTrackedServices(IEnumerable<StructuredEventRecord> events, List<string> notes)
    {
        var interestingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aehd" };

        foreach (var record in events.Where(record => record.ProviderName.Equals("Service Control Manager", StringComparison.OrdinalIgnoreCase)))
        {
            var extractedName = ExtractServiceName(record.Message);
            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                interestingNames.Add(extractedName);
            }
        }

        var services = new List<ServiceRecord>();

        foreach (var name in interestingNames.OrderBy(name => name))
        {
            using var registryKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            if (registryKey is null)
            {
                continue;
            }

            services.Add(new ServiceRecord
            {
                ServiceName = name,
                DisplayName = registryKey.GetValue("DisplayName")?.ToString() ?? name,
                Status = events.Any(record => record.Message.Contains(name, StringComparison.OrdinalIgnoreCase))
                    ? "Observed in recent boot events"
                    : "Unknown",
                StartMode = MapStartMode(ReadIntValue(registryKey, "Start")),
                BinaryPath = registryKey?.GetValue("ImagePath")?.ToString() ?? string.Empty,
                IsDriver = ReadIntValue(registryKey, "Type") is 1 or 2
            });
        }

        if (services.Count == 0)
        {
            notes.Add("İzlenen servis listesi boş döndü.");
        }

        return services;
    }

    private static List<BootAttempt> BuildBootAttempts(IReadOnlyList<StructuredEventRecord> events)
    {
        var kernelMarkers = events
            .Where(record => record.LogName.Equals("Microsoft-Windows-Kernel-Boot/Operational", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.TimeCreated)
            .ToList();

        if (kernelMarkers.Count == 0)
        {
            return [];
        }

        var groupedStarts = new List<DateTimeOffset>();
        foreach (var marker in kernelMarkers)
        {
            if (groupedStarts.Count == 0 || (groupedStarts[^1] - marker.TimeCreated).Duration() > TimeSpan.FromMinutes(5))
            {
                groupedStarts.Add(marker.TimeCreated);
            }
        }

        var attempts = new List<BootAttempt>();
        for (var index = 0; index < groupedStarts.Count; index++)
        {
            var start = groupedStarts[index];
            var end = start.AddMinutes(20);
            var related = events
                .Where(record => record.TimeCreated >= start && record.TimeCreated <= end)
                .OrderBy(record => record.TimeCreated)
                .ToList();

            var failedDrivers = related
                .Where(record => record.EventId == 7026 || record.Message.Contains("failed to load", StringComparison.OrdinalIgnoreCase))
                .Select(record => ExtractTrailingItem(record.Message))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var timedOutServices = related
                .Where(record => record.EventId is 7000 or 7009)
                .Select(record => ExtractServiceName(record.Message))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var wlanSignals = related
                .Where(record => record.ProviderName.Contains("WLAN", StringComparison.OrdinalIgnoreCase))
                .Select(record => FirstLine(record.Message))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasRecovery = related.Any(record =>
                record.ProviderName.Contains("StartupRepair", StringComparison.OrdinalIgnoreCase) ||
                record.Message.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                record.Message.Contains("onarma", StringComparison.OrdinalIgnoreCase));

            attempts.Add(new BootAttempt
            {
                Sequence = index + 1,
                StartedAt = start,
                AttemptLabel = $"Boot {index + 1}",
                FailedDriverNames = failedDrivers,
                TimedOutServices = timedOutServices,
                WlanSignals = wlanSignals,
                HasRecoverySignal = hasRecovery,
                NotableEvents = related.Select(record => $"{record.ProviderName} #{record.EventId}: {FirstLine(record.Message)}").Take(5).ToList(),
                Summary = BuildBootSummary(failedDrivers.Count, timedOutServices.Count, wlanSignals.Count, hasRecovery)
            });
        }

        return attempts;
    }

    private static string BuildBootSummary(int failedDriverCount, int timedOutServiceCount, int wlanSignalCount, bool hasRecovery)
    {
        var parts = new List<string>();
        if (failedDriverCount > 0)
        {
            parts.Add($"{failedDriverCount} sürücü problemi");
        }

        if (timedOutServiceCount > 0)
        {
            parts.Add($"{timedOutServiceCount} servis timeout");
        }

        if (wlanSignalCount > 0)
        {
            parts.Add($"{wlanSignalCount} WLAN sinyali");
        }

        if (hasRecovery)
        {
            parts.Add("recovery izi");
        }

        return parts.Count == 0 ? "Belirgin boot sinyali yok." : string.Join(", ", parts);
    }

    private static string ExtractServiceName(string message)
    {
        var firstLine = FirstLine(message);
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return string.Empty;
        }

        var match = Regex.Match(firstLine, @"^(?<name>.+?)\s+(hizmeti|service)\b", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["name"].Value.Trim(' ', '(', ')');
        }

        match = Regex.Match(firstLine, @"^(?<name>.+?)\s+sunucusu", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
    }

    private static string ExtractTrailingItem(string message)
    {
        var lines = message
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.EndsWith(":", StringComparison.Ordinal))
            .ToList();

        return lines.Count == 0 ? string.Empty : lines[^1];
    }

    private static string FirstLine(string message)
    {
        return message
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string GetLocalizedValue(IReadOnlyDictionary<string, string> values, string[] candidateKeys, string fallback = "")
    {
        foreach (var pair in values)
        {
            var normalized = pair.Key.Trim().ToLowerInvariant();
            if (candidateKeys.Contains(normalized))
            {
                return pair.Value;
            }
        }

        return fallback;
    }

    private static int ReadIntValue(RegistryKey? key, string name)
    {
        try
        {
            return key?.GetValue(name) switch
            {
                int intValue => intValue,
                string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    private static string MapStartMode(int rawValue)
    {
        return rawValue switch
        {
            0 => "Boot",
            1 => "System",
            2 => "Automatic",
            3 => "Demand",
            4 => "Disabled",
            _ => "Unknown"
        };
    }
}
