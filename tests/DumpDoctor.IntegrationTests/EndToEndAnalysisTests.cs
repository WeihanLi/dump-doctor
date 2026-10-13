using DumpDoctor.Collectors;
using DumpDoctor.Core;
using DumpDoctor.Reporting;
using DumpDoctor.Rules;

namespace DumpDoctor.IntegrationTests;

public sealed class EndToEndAnalysisTests
{
    [Fact]
    public async Task AnalyzeAsync_ProducesDeterministicMarkdownReport()
    {
        var orchestrator = new AnalysisOrchestrator(
            new FakeDumpSessionFactory(),
            [
                new HeapStatisticsCollector(),
                new GcAndHandleCollector(),
                new ThreadSnapshotCollector()
            ],
            [
                new LargeObjectFootprintRule(),
                new SyncOverAsyncRule()
            ],
            toolVersion: "test");

        var options = new AnalysisOptions(
            "fake.dmp",
            AnalysisFocus.All,
            ReportFormat.Markdown,
            null,
            Severity.High,
            true,
            false,
            RedactionLevel.Standard,
            false,
            null);

        var report = await orchestrator.AnalyzeAsync("analyze", options, includeFindings: true, CancellationToken.None);
        var markdown = new MarkdownReportRenderer().Render(report);

        Assert.Contains("MEM-003", report.Findings.Select(finding => finding.RuleId));
        Assert.Contains("TP-002", report.Findings.Select(finding => finding.RuleId));
        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("MEM-003", markdown, StringComparison.Ordinal);
        Assert.Contains("TP-002", markdown, StringComparison.Ordinal);
    }

    private sealed class FakeDumpSessionFactory : IDumpSessionFactory
    {
        public Task<IDumpSession> OpenAsync(string dumpPath, DateTimeOffset? capturedAtOverrideUtc, CancellationToken cancellationToken)
        {
            IDumpSession session = new FakeDumpSession(capturedAtOverrideUtc ?? DateTimeOffset.Parse("2026-10-13T00:00:00Z"));
            return Task.FromResult(session);
        }
    }

    private sealed class FakeDumpSession(DateTimeOffset capturedAtUtc) : IDumpSession
    {
        public ArtifactMetadata Artifact { get; } = new(
            "artifact",
            "fake.dmp",
            42,
            "HASH",
            capturedAtUtc,
            "UserSupplied",
            "WindowsDump",
            "8.0.0",
            "windows",
            "x64");

        public DumpCapabilities Capabilities { get; } = new(true, true, true, true, true, true, []);

        public IEnumerable<HeapObjectInfo> EnumerateHeapObjects(CancellationToken cancellationToken)
        {
            yield return new HeapObjectInfo(0x1, "System.Byte[]", 125_000_000, true, 2);
            yield return new HeapObjectInfo(0x2, "MyApp.CacheEntry", 32, false, 2);
        }

        public IEnumerable<PinnedHandleInfo> EnumeratePinnedHandles(CancellationToken cancellationToken)
        {
            yield return new PinnedHandleInfo(0x10, 0x1, "System.Byte[]", 125_000_000);
        }

        public IEnumerable<ThreadSnapshotData> EnumerateThreads(CancellationToken cancellationToken)
        {
            for (var i = 0; i < 3; i++)
            {
                yield return new ThreadSnapshotData(
                    (uint)i,
                    i,
                    true,
                    true,
                    "Running",
                    [
                        new StackFrameEvidence(0, 0, "System.Threading.Tasks.Task.Wait()"),
                        new StackFrameEvidence(0, 0, "MyApp.Worker.Execute()")
                    ]);
            }
        }

        public GcSnapshotData? GetGcSnapshot(CancellationToken cancellationToken) => new(true, true, 4, 8);

        public ThreadPoolSnapshotData? GetThreadPoolSnapshot(CancellationToken cancellationToken) => new(4, 200, 12, 10, 2, 95);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
