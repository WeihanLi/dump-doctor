using DumpDoctor.Core;

namespace DumpDoctor.Collectors;

public sealed class HeapStatisticsCollector : IEvidenceCollector
{
    public string Id => "collector.heap-stats";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<AnalysisFocus> SupportedFocuses { get; } = new HashSet<AnalysisFocus> { AnalysisFocus.All, AnalysisFocus.Memory };
    public IReadOnlySet<DumpCapabilityKind> RequiredCapabilities { get; } = new HashSet<DumpCapabilityKind> { DumpCapabilityKind.EnumerateHeap };

    public Task CollectAsync(AnalysisContext context, IEvidenceStore sink, CancellationToken cancellationToken)
    {
        var perType = new Dictionary<string, (long Count, ulong Size)>(StringComparer.Ordinal);
        var artifactId = context.Session.Artifact.Id;

        foreach (var item in context.Session.EnumerateHeapObjects(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!perType.TryGetValue(item.TypeName, out var aggregate))
            {
                aggregate = (0, 0);
            }

            perType[item.TypeName] = (aggregate.Count + 1, aggregate.Size + item.SizeBytes);

            if (item.IsLargeObject)
            {
                sink.Add(new LargeObjectEvidence(
                    Id: $"loh:{item.Address:x}",
                    Description: $"Large object '{item.TypeName}' with shallow size {item.SizeBytes:N0} bytes.",
                    CollectorId: Id,
                    ArtifactId: artifactId,
                    Address: item.Address,
                    TypeName: item.TypeName,
                    SizeBytes: item.SizeBytes,
                    Generation: item.Generation));
            }
        }

        foreach (var entry in perType.OrderByDescending(entry => entry.Value.Size).Take(100))
        {
            sink.Add(new HeapTypeStatEvidence(
                Id: $"heap-type:{entry.Key}",
                Description: $"Aggregated heap statistics for type '{entry.Key}'.",
                CollectorId: Id,
                ArtifactId: artifactId,
                TypeName: entry.Key,
                InstanceCount: entry.Value.Count,
                ShallowSizeBytes: entry.Value.Size,
                Basis: ReachabilityBasis.AllObjects));
        }

        return Task.CompletedTask;
    }
}

public sealed class GcAndHandleCollector : IEvidenceCollector
{
    public string Id => "collector.gc-state";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<AnalysisFocus> SupportedFocuses { get; } = new HashSet<AnalysisFocus> { AnalysisFocus.All, AnalysisFocus.Memory, AnalysisFocus.Threads };
    public IReadOnlySet<DumpCapabilityKind> RequiredCapabilities { get; } = new HashSet<DumpCapabilityKind> { DumpCapabilityKind.ReadGcState };

    public Task CollectAsync(AnalysisContext context, IEvidenceStore sink, CancellationToken cancellationToken)
    {
        var artifactId = context.Session.Artifact.Id;
        var gcSnapshot = context.Session.GetGcSnapshot(cancellationToken);
        if (gcSnapshot is not null)
        {
            sink.Add(new GcSnapshotEvidence(
                Id: "gc:snapshot",
                Description: "GC heap configuration observed in the dump.",
                CollectorId: Id,
                ArtifactId: artifactId,
                CanWalkHeap: gcSnapshot.CanWalkHeap,
                IsServerGc: gcSnapshot.IsServerGc,
                HeapCount: gcSnapshot.HeapCount,
                SegmentCount: gcSnapshot.SegmentCount));
        }

        if (context.Session.Capabilities.CanReadThreadPool)
        {
            var threadPool = context.Session.GetThreadPoolSnapshot(cancellationToken);
            if (threadPool is not null)
            {
                sink.Add(new ThreadPoolSnapshotEvidence(
                    Id: "threadpool:snapshot",
                    Description: "ThreadPool configuration observed in the dump.",
                    CollectorId: Id,
                    ArtifactId: artifactId,
                    MinThreads: threadPool.MinThreads,
                    MaxThreads: threadPool.MaxThreads,
                    TotalThreads: threadPool.TotalThreads,
                    RunningThreads: threadPool.RunningThreads,
                    IdleThreads: threadPool.IdleThreads,
                    CpuUtilizationLimit: threadPool.CpuUtilizationLimit));
            }
        }

        if (!context.Session.Capabilities.CanEnumerateHandles)
        {
            return Task.CompletedTask;
        }

        foreach (var handle in context.Session.EnumeratePinnedHandles(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink.Add(new PinnedHandleEvidence(
                Id: $"pin:{handle.HandleAddress:x}",
                Description: $"Pinned handle for '{handle.TypeName}'.",
                CollectorId: Id,
                ArtifactId: artifactId,
                HandleAddress: handle.HandleAddress,
                ObjectAddress: handle.ObjectAddress,
                TypeName: handle.TypeName,
                SizeBytes: handle.SizeBytes));
        }

        return Task.CompletedTask;
    }
}

public sealed class ThreadSnapshotCollector : IEvidenceCollector
{
    public string Id => "collector.threads";
    public Version Version => new(1, 0, 0);
    public IReadOnlySet<AnalysisFocus> SupportedFocuses { get; } = new HashSet<AnalysisFocus> { AnalysisFocus.All, AnalysisFocus.Threads };
    public IReadOnlySet<DumpCapabilityKind> RequiredCapabilities { get; } = new HashSet<DumpCapabilityKind> { DumpCapabilityKind.EnumerateThreads };

    public Task CollectAsync(AnalysisContext context, IEvidenceStore sink, CancellationToken cancellationToken)
    {
        var artifactId = context.Session.Artifact.Id;
        foreach (var thread in context.Session.EnumerateThreads(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink.Add(new ThreadSnapshotEvidence(
                Id: $"thread:{thread.OsThreadId:x}",
                Description: $"Managed stack snapshot for OS thread 0x{thread.OsThreadId:x}.",
                CollectorId: Id,
                ArtifactId: artifactId,
                OsThreadId: thread.OsThreadId,
                ManagedThreadId: thread.ManagedThreadId,
                IsAlive: thread.IsAlive,
                IsThreadPoolWorker: thread.IsThreadPoolWorker,
                State: thread.State,
                Frames: thread.Frames));
        }

        return Task.CompletedTask;
    }
}
