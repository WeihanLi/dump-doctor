using DumpDoctor.Core;

namespace DumpDoctor.Core.Tests;

public sealed class AnalysisReportTests
{
    [Fact]
    public void DetermineExitCode_ReturnsPartialResult_WhenCollectorsFailed()
    {
        var report = CreateReport(hasPartialResults: true, failOn: Severity.High, findings: Array.Empty<DiagnosticFinding>());

        Assert.Equal(ExitCode.PartialResult, report.DetermineExitCode());
    }

    [Fact]
    public void DetermineExitCode_ReturnsFindingsAtOrAboveFailOn_WhenThresholdMatches()
    {
        DiagnosticFinding[] findings =
        [
            new DiagnosticFinding(
                "MEM-003",
                "1.0.0",
                "Large LOH",
                "Summary",
                FindingCategory.Memory,
                Severity.High,
                Confidence.Medium,
                ["loh:1"],
                ["Observed directly"],
                ["Single snapshot"],
                ["Capture a second dump"],
                [])
        ];

        var report = CreateReport(hasPartialResults: false, failOn: Severity.Medium, findings);

        Assert.Equal(ExitCode.FindingsAtOrAboveFailOn, report.DetermineExitCode());
    }

    private static AnalysisReport CreateReport(bool hasPartialResults, Severity? failOn, IReadOnlyList<DiagnosticFinding> findings) =>
        new(
            SchemaVersion: "1.0.0",
            Command: "analyze",
            Options: new AnalysisOptions("sample.dmp", AnalysisFocus.All, ReportFormat.Json, null, failOn, true, false, RedactionLevel.Standard, false, null),
            Artifact: new ArtifactMetadata("artifact", "sample.dmp", 1_024, "ABC", DateTimeOffset.UtcNow, "FileSystem", "WindowsDump", "8.0.0", "windows", "x64"),
            Capabilities: new DumpCapabilities(true, true, true, true, true, true, []),
            Manifest: new AnalysisManifest(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "0.1.0", "10.0.0", true, false, new Dictionary<string, string>(), new Dictionary<string, string>()),
            Completeness: new DiagnosticCompleteness(1, 1, 1.0, [], []),
            Collectors: [],
            Findings: findings,
            Evidence: [],
            HasPartialResults: hasPartialResults);
}
