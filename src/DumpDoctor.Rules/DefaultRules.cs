using DumpDoctor.Core;

namespace DumpDoctor.Rules;

public sealed class LargeObjectFootprintRule : IDiagnosticRule
{
    public string Id => "MEM-003";
    public Version Version => new(1, 0, 0);
    public string Title => "Large LOH or pinned-object footprint";
    public string Summary => "Flags large large-object-heap or pinned-object footprint in a single dump.";
    public FindingCategory Category => FindingCategory.Memory;
    public AnalysisFocus Focus => AnalysisFocus.Memory;
    public IReadOnlySet<EvidenceKind> RequiredEvidence { get; } = new HashSet<EvidenceKind> { EvidenceKind.GcSnapshot, EvidenceKind.LargeObject };
    public IReadOnlyDictionary<string, string> Thresholds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["medium.largeObjectBytes"] = RulePolicy.DefaultLargeObjectThresholdBytes.ToString(),
        ["high.largeObjectBytes"] = RulePolicy.DefaultHighLargeObjectThresholdBytes.ToString()
    };

    public IReadOnlyList<DiagnosticFinding> Evaluate(IEvidenceStore evidence, RulePolicy policy)
    {
        var largeObjects = evidence.Get<LargeObjectEvidence>();
        if (largeObjects.Count == 0)
        {
            return [];
        }

        var totalLargeObjectBytes = largeObjects.Sum(item => (decimal)item.SizeBytes);
        var pinnedHandles = evidence.Get<PinnedHandleEvidence>();
        var totalPinnedBytes = pinnedHandles.Sum(item => (decimal)item.SizeBytes);
        var totalFootprint = totalLargeObjectBytes + totalPinnedBytes;

        if (totalFootprint < policy.LargeObjectBytesThreshold)
        {
            return [];
        }

        var severity = totalFootprint >= policy.HighLargeObjectBytesThreshold ? Severity.High : Severity.Medium;
        var topLargeObjectTypes = largeObjects
            .GroupBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(group => new { TypeName = group.Key, Bytes = group.Sum(item => (decimal)item.SizeBytes) })
            .OrderByDescending(entry => entry.Bytes)
            .Take(3)
            .Select(entry => $"{entry.TypeName} ({entry.Bytes:N0} bytes)")
            .ToArray();

        return
        [
            new DiagnosticFinding(
                RuleId: Id,
                RuleVersion: Version.ToString(),
                Title: Title,
                Summary: $"Observed {totalLargeObjectBytes:N0} bytes of large objects and {totalPinnedBytes:N0} bytes behind pinned handles. Dominant types: {string.Join(", ", topLargeObjectTypes)}.",
                Category: Category,
                Severity: severity,
                Confidence: Confidence.Medium,
                EvidenceIds: largeObjects.Take(10).Select(item => item.Id).Concat(pinnedHandles.Take(5).Select(item => item.Id)).ToArray(),
                ConfidenceRationale:
                [
                    "The dump directly shows large-object instances and pinned handles.",
                    "A single snapshot shows footprint, but not whether the workload expects it."
                ],
                Limitations:
                [
                    "A large LOH footprint can be legitimate for buffers, caches, or batch workloads.",
                    "The MVP report uses shallow size and does not yet compute retained size."
                ],
                Recommendations:
                [
                    "Inspect the top retaining types and buffer ownership before classifying this as a leak.",
                    "Capture comparable follow-up dumps to confirm whether the footprint persists or grows."
                ],
                RelatedFindings: []),
        ];
    }
}

public sealed class SyncOverAsyncRule : IDiagnosticRule
{
    private static readonly string[] SyncWaitPatterns =
    [
        "Task.Wait",
        "Task`1.get_Result",
        ".Result",
        "GetAwaiter().GetResult",
        "ManualResetEventSlim.Wait",
        "SemaphoreSlim.Wait"
    ];

    public string Id => "TP-002";
    public Version Version => new(1, 0, 0);
    public string Title => "Repeated synchronous waits on async work";
    public string Summary => "Highlights thread stacks that repeatedly block on async completions or wait handles.";
    public FindingCategory Category => FindingCategory.Threads;
    public AnalysisFocus Focus => AnalysisFocus.Threads;
    public IReadOnlySet<EvidenceKind> RequiredEvidence { get; } = new HashSet<EvidenceKind> { EvidenceKind.ThreadSnapshot };
    public IReadOnlyDictionary<string, string> Thresholds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["medium.threadCount"] = RulePolicy.DefaultSyncWaitThreadThreshold.ToString(),
        ["high.threadCount"] = RulePolicy.DefaultSevereSyncWaitThreadThreshold.ToString()
    };

    public IReadOnlyList<DiagnosticFinding> Evaluate(IEvidenceStore evidence, RulePolicy policy)
    {
        var matchingThreads = evidence
            .Get<ThreadSnapshotEvidence>()
            .Where(IsSyncWaitThread)
            .ToArray();

        if (matchingThreads.Length < policy.SyncWaitThreadThreshold)
        {
            return [];
        }

        var groupedPatterns = matchingThreads
            .Select(thread => new
            {
                Thread = thread,
                Signature = string.Join(" | ", thread.Frames.Take(3).Select(frame => frame.DisplayString))
            })
            .GroupBy(item => item.Signature, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToArray();

        var topGroup = groupedPatterns[0];
        var severity = matchingThreads.Length >= policy.SevereSyncWaitThreadThreshold ? Severity.High : Severity.Medium;
        var recommendation = matchingThreads.Any(thread => thread.IsThreadPoolWorker)
            ? "Inspect these worker stacks for sync-over-async or synchronous I/O; combine with runtime counters to confirm starvation."
            : "Review the waiting call sites and their async boundaries to reduce blocking joins.";

        return
        [
            new DiagnosticFinding(
                RuleId: Id,
                RuleVersion: Version.ToString(),
                Title: Title,
                Summary: $"Found {matchingThreads.Length} threads with synchronous wait patterns. The largest repeated stack signature appears on {topGroup.Count()} threads.",
                Category: Category,
                Severity: severity,
                Confidence: Confidence.Medium,
                EvidenceIds: matchingThreads.Take(10).Select(thread => thread.Id).ToArray(),
                ConfidenceRationale:
                [
                    "Thread stacks directly show blocking wait patterns.",
                    "The diagnosis is stronger when correlated with queue growth or throughput metrics, which a dump alone does not provide."
                ],
                Limitations:
                [
                    "A blocked stack is a clue, not proof of process-wide ThreadPool starvation.",
                    "The dump is a snapshot and does not show whether the blocking pattern is sustained."
                ],
                Recommendations:
                [
                    recommendation,
                    "Collect runtime metrics or a trace if you need to prove starvation or throughput collapse."
                ],
                RelatedFindings: []),
        ];
    }

    private static bool IsSyncWaitThread(ThreadSnapshotEvidence thread) =>
        thread.Frames.Any(frame => SyncWaitPatterns.Any(pattern => frame.DisplayString.Contains(pattern, StringComparison.Ordinal)));
}
