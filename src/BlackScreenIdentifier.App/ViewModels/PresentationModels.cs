using System.Windows.Media;
using BlackScreenIdentifier.Core.Enums;
using BlackScreenIdentifier.Core.Models;

namespace BlackScreenIdentifier.App.ViewModels;

public sealed record SummaryCardItem(string Label, string Value, string Subtitle, Brush AccentBrush);

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
            AccentBrush = finding.Severity switch
            {
                FindingSeverity.Critical => Brushes.IndianRed,
                FindingSeverity.High => Brushes.DarkOrange,
                FindingSeverity.Medium => Brushes.DarkCyan,
                FindingSeverity.Low => Brushes.SteelBlue,
                _ => Brushes.Gray
            },
            EvidenceLines = finding.Evidence.Select(evidence => $"{evidence.Label}: {evidence.Detail}").ToList()
        };
    }
}

public sealed class ActionItemViewModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Preview { get; init; }
    public required bool RequiresElevation { get; init; }
    public required string RiskLabel { get; init; }
    public required string RequirementLabel { get; init; }

    public static ActionItemViewModel FromDescriptor(RemediationActionDescriptor descriptor)
    {
        return new ActionItemViewModel
        {
            Id = descriptor.Id,
            Title = descriptor.Title,
            Preview = descriptor.Preview,
            RequiresElevation = descriptor.RequiresElevation,
            RiskLabel = descriptor.RiskLevel switch
            {
                RiskLevel.Safe => "Güvenli",
                RiskLevel.Medium => "Orta risk",
                _ => "Yüksek risk"
            },
            RequirementLabel = descriptor.RequiresElevation ? "Admin gerekir" : "Standart kullanıcı"
        };
    }
}

public sealed class BootAttemptItemViewModel
{
    public required string Label { get; init; }
    public required string StartedAt { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Highlights { get; init; }

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
            Highlights = highlights.Take(6).ToList()
        };
    }
}

public sealed record SessionItemViewModel(string Title, string Subtitle);
