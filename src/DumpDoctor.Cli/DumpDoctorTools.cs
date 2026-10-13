using System.ComponentModel;
using DumpDoctor.Core;
using DumpDoctor.Hosting;
using ModelContextProtocol.Server;

namespace DumpDoctor.Cli;

[McpServerToolType]
public sealed class DumpDoctorTools(AnalysisService analysisService)
{
    [McpServerTool, Description("Lists the diagnostic rules available in the local DumpDoctor installation.")]
    public IReadOnlyList<RuleCatalogEntry> ListRules() =>
        analysisService.Rules
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .Select(rule => new RuleCatalogEntry(
                rule.Id,
                rule.Title,
                rule.Focus.ToString(),
                rule.Category.ToString(),
                rule.RequiredEvidence.OrderBy(kind => kind.ToString()).Select(kind => kind.ToString()).ToArray(),
                rule.Thresholds))
            .ToArray();

    [McpServerTool, Description("Analyzes a local .NET dump and returns its evidence-backed diagnostic report as JSON. DumpDoctor does not upload the dump or its contents.")]
    public async Task<string> AnalyzeDumpAsync(
        [Description("Absolute or current-working-directory-relative path to a local .dmp or core dump.")] string dumpPath,
        [Description("Optional analysis focus: All, Memory, Threads, or Cpu.")] string focus = "All",
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(dumpPath, focus);
        var report = await analysisService.AnalyzeAsync("analyze", options, includeFindings: true, cancellationToken);
        return new DumpDoctor.Reporting.JsonReportRenderer().Render(report);
    }

    [McpServerTool, Description("Collects raw evidence from a local .NET dump without evaluating diagnostic findings, and returns the report as JSON.")]
    public async Task<string> InspectDumpAsync(
        [Description("Absolute or current-working-directory-relative path to a local .dmp or core dump.")] string dumpPath,
        [Description("Optional analysis focus: All, Memory, Threads, or Cpu.")] string focus = "All",
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(dumpPath, focus);
        var report = await analysisService.AnalyzeAsync("inspect", options, includeFindings: false, cancellationToken);
        return new DumpDoctor.Reporting.JsonReportRenderer().Render(report);
    }

    private static AnalysisOptions CreateOptions(string dumpPath, string focus)
    {
        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            throw new ArgumentException("A local dump path is required.", nameof(dumpPath));
        }

        if (!Enum.TryParse<AnalysisFocus>(focus, ignoreCase: true, out var parsedFocus))
        {
            throw new ArgumentException(
                $"Unsupported focus '{focus}'. Use All, Memory, Threads, or Cpu.",
                nameof(focus));
        }

        return AnalysisOptions.CreateDefault(Path.GetFullPath(dumpPath)) with { Focus = parsedFocus };
    }
}

public sealed record RuleCatalogEntry(
    string Id,
    string Title,
    string Focus,
    string Category,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyDictionary<string, string> Thresholds);
