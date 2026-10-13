using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DumpDoctor.Core;

namespace DumpDoctor.Reporting;

public interface IReportRenderer
{
    ReportFormat Format { get; }
    string Render(AnalysisReport report);
}

public sealed class JsonReportRenderer : IReportRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ReportFormat Format => ReportFormat.Json;

    public string Render(AnalysisReport report) => JsonSerializer.Serialize(report, SerializerOptions);
}

public sealed class MarkdownReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Markdown;

    public string Render(AnalysisReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# DumpDoctor report");
        builder.AppendLine();
        builder.AppendLine($"- **Command:** `{report.Command}`");
        builder.AppendLine($"- **Artifact:** `{report.Artifact.Path}`");
        builder.AppendLine($"- **Runtime:** {report.Artifact.RuntimeVersion}");
        builder.AppendLine($"- **OS / arch:** {report.Artifact.OperatingSystem} / {report.Artifact.Architecture}");
        builder.AppendLine($"- **Capture time:** {report.Artifact.CaptureTimeUtc:O} ({report.Artifact.CaptureTimeSource})");
        builder.AppendLine($"- **Completeness:** {report.Completeness.RunnableRules}/{report.Completeness.ApplicableRules} runnable rules ({report.Completeness.Coverage:P0})");
        builder.AppendLine();

        builder.AppendLine("## Findings");
        builder.AppendLine();
        if (report.Findings.Count == 0)
        {
            builder.AppendLine("No findings were produced for the collected evidence.");
            builder.AppendLine();
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                builder.AppendLine($"### {finding.RuleId} — {finding.Title}");
                builder.AppendLine();
                builder.AppendLine($"- **Severity:** {finding.Severity}");
                builder.AppendLine($"- **Confidence:** {finding.Confidence}");
                builder.AppendLine($"- **Category:** {finding.Category}");
                builder.AppendLine($"- **Summary:** {finding.Summary}");

                if (finding.Limitations.Count > 0)
                {
                    builder.AppendLine("- **Limitations:**");
                    foreach (var limitation in finding.Limitations)
                    {
                        builder.AppendLine($"  - {limitation}");
                    }
                }

                if (finding.Recommendations.Count > 0)
                {
                    builder.AppendLine("- **Recommended actions:**");
                    foreach (var recommendation in finding.Recommendations)
                    {
                        builder.AppendLine($"  - {recommendation}");
                    }
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Collector status");
        builder.AppendLine();
        foreach (var collector in report.Collectors)
        {
            builder.AppendLine($"- **{collector.CollectorId}** `{collector.Version}` — {(collector.Succeeded ? "ok" : "failed")} ({collector.Message})");
        }

        builder.AppendLine();
        builder.AppendLine("## Missing evidence");
        builder.AppendLine();
        var missingRules = report.Completeness.Rules.Where(rule => rule.Status != DiagnosticCapabilityStatus.Runnable).ToArray();
        if (missingRules.Length == 0)
        {
            builder.AppendLine("All applicable rules had the required evidence.");
        }
        else
        {
            foreach (var rule in missingRules)
            {
                builder.AppendLine($"- **{rule.RuleId}** — {rule.Reason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Evidence summary");
        builder.AppendLine();
        foreach (var group in report.Evidence.GroupBy(item => item.Kind).OrderBy(group => group.Key))
        {
            builder.AppendLine($"- **{group.Key}:** {group.Count()}");
        }

        return builder.ToString();
    }
}
