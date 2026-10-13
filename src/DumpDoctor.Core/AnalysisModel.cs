using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DumpDoctor.Core;

public enum AnalysisFocus
{
    All,
    Memory,
    Threads,
    Cpu
}

public enum ReportFormat
{
    Json,
    Markdown
}

public enum RedactionLevel
{
    Strict,
    Standard,
    None
}

public enum Severity
{
    Informational = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum Confidence
{
    Low,
    Medium,
    High
}

public enum FindingCategory
{
    Memory,
    Threads,
    Cpu,
    Gc,
    Runtime
}

public enum Provenance
{
    Observed,
    Derived
}

public enum EvidenceKind
{
    ArtifactMetadata,
    HeapTypeStat,
    LargeObject,
    PinnedHandle,
    ThreadSnapshot,
    GcSnapshot,
    ThreadPoolSnapshot
}

public enum ReachabilityBasis
{
    AllObjects,
    ReachableObjects
}

public enum DiagnosticCapabilityStatus
{
    Runnable,
    Degraded,
    NotPossible
}

public enum DumpCapabilityKind
{
    OpenRuntime,
    EnumerateHeap,
    EnumerateThreads,
    EnumerateHandles,
    ReadGcState,
    ReadThreadPool
}

public enum ExitCode
{
    Success = 0,
    FindingsAtOrAboveFailOn = 1,
    InvalidArguments = 2,
    InputMissingCorruptOrUnsupported = 3,
    PartialResult = 4,
    UnexpectedInternalError = 5,
    Cancelled = 130
}

public sealed record ArtifactMetadata(
    string Id,
    string Path,
    long FileSizeBytes,
    string Sha256,
    DateTimeOffset CaptureTimeUtc,
    string CaptureTimeSource,
    string DumpKind,
    string RuntimeVersion,
    string OperatingSystem,
    string Architecture);

public sealed record DumpCapabilities(
    bool CanOpenRuntime,
    bool CanEnumerateHeap,
    bool CanEnumerateThreads,
    bool CanEnumerateHandles,
    bool CanReadGcState,
    bool CanReadThreadPool,
    IReadOnlyList<string> Limitations)
{
    public bool Supports(DumpCapabilityKind capability) => capability switch
    {
        DumpCapabilityKind.OpenRuntime => CanOpenRuntime,
        DumpCapabilityKind.EnumerateHeap => CanEnumerateHeap,
        DumpCapabilityKind.EnumerateThreads => CanEnumerateThreads,
        DumpCapabilityKind.EnumerateHandles => CanEnumerateHandles,
        DumpCapabilityKind.ReadGcState => CanReadGcState,
        DumpCapabilityKind.ReadThreadPool => CanReadThreadPool,
        _ => false
    };
}

public sealed record HeapObjectInfo(
    ulong Address,
    string TypeName,
    ulong SizeBytes,
    bool IsLargeObject,
    int? Generation);

public sealed record PinnedHandleInfo(
    ulong HandleAddress,
    ulong ObjectAddress,
    string TypeName,
    ulong SizeBytes);

public sealed record StackFrameEvidence(
    ulong StackPointer,
    ulong InstructionPointer,
    string DisplayString);

public sealed record ThreadSnapshotData(
    uint OsThreadId,
    int ManagedThreadId,
    bool IsAlive,
    bool IsThreadPoolWorker,
    string State,
    IReadOnlyList<StackFrameEvidence> Frames);

public sealed record GcSnapshotData(
    bool CanWalkHeap,
    bool IsServerGc,
    int HeapCount,
    int SegmentCount);

public sealed record ThreadPoolSnapshotData(
    int? MinThreads,
    int? MaxThreads,
    int? TotalThreads,
    int? RunningThreads,
    int? IdleThreads,
    int? CpuUtilizationLimit);

public sealed record AnalysisOptions(
    string DumpPath,
    AnalysisFocus Focus,
    ReportFormat Format,
    string? OutputPath,
    Severity? FailOn,
    bool Offline,
    bool AllowSymbolServer,
    RedactionLevel Redaction,
    bool Verbose,
    DateTimeOffset? CapturedAtOverrideUtc)
{
    public static AnalysisOptions CreateDefault(string dumpPath) =>
        new(
            dumpPath,
            AnalysisFocus.All,
            ReportFormat.Json,
            null,
            null,
            Offline: true,
            AllowSymbolServer: false,
            RedactionLevel.Standard,
            Verbose: false,
            CapturedAtOverrideUtc: null);
}

public sealed record AnalysisContext(
    AnalysisOptions Options,
    IDumpSession Session,
    DateTimeOffset StartedAtUtc,
    string ToolVersion);

public sealed record CollectorExecution(
    string CollectorId,
    string Version,
    bool Succeeded,
    string Message,
    TimeSpan Duration);

public sealed record RuleEvaluationStatus(
    string RuleId,
    string Version,
    DiagnosticCapabilityStatus Status,
    string Reason);

public sealed record CollectorCapabilityStatus(
    string CollectorId,
    string Version,
    DiagnosticCapabilityStatus Status,
    string Reason);

public sealed record DiagnosticCompleteness(
    int RunnableRules,
    int ApplicableRules,
    double Coverage,
    IReadOnlyList<RuleEvaluationStatus> Rules,
    IReadOnlyList<CollectorCapabilityStatus> Collectors);

public sealed record AnalysisManifest(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ToolVersion,
    string HostRuntimeVersion,
    bool Offline,
    bool NetworkAccessPerformed,
    IReadOnlyDictionary<string, string> CollectorVersions,
    IReadOnlyDictionary<string, string> RuleVersions);

public sealed record DiagnosticFinding(
    string RuleId,
    string RuleVersion,
    string Title,
    string Summary,
    FindingCategory Category,
    Severity Severity,
    Confidence Confidence,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> ConfidenceRationale,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> RelatedFindings);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ArtifactMetadataEvidence), "artifactMetadata")]
[JsonDerivedType(typeof(HeapTypeStatEvidence), "heapTypeStat")]
[JsonDerivedType(typeof(LargeObjectEvidence), "largeObject")]
[JsonDerivedType(typeof(PinnedHandleEvidence), "pinnedHandle")]
[JsonDerivedType(typeof(ThreadSnapshotEvidence), "threadSnapshot")]
[JsonDerivedType(typeof(GcSnapshotEvidence), "gcSnapshot")]
[JsonDerivedType(typeof(ThreadPoolSnapshotEvidence), "threadPoolSnapshot")]
public abstract record EvidenceItem(
    string Id,
    EvidenceKind Kind,
    string Description,
    string CollectorId,
    Provenance Provenance,
    string ArtifactId);

public sealed record ArtifactMetadataEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    string Path,
    long FileSizeBytes,
    string Sha256,
    DateTimeOffset CaptureTimeUtc,
    string CaptureTimeSource,
    string DumpKind,
    string RuntimeVersion,
    string OperatingSystem,
    string Architecture)
    : EvidenceItem(Id, EvidenceKind.ArtifactMetadata, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record HeapTypeStatEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    string TypeName,
    long InstanceCount,
    ulong ShallowSizeBytes,
    ReachabilityBasis Basis)
    : EvidenceItem(Id, EvidenceKind.HeapTypeStat, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record LargeObjectEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    ulong Address,
    string TypeName,
    ulong SizeBytes,
    int? Generation)
    : EvidenceItem(Id, EvidenceKind.LargeObject, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record PinnedHandleEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    ulong HandleAddress,
    ulong ObjectAddress,
    string TypeName,
    ulong SizeBytes)
    : EvidenceItem(Id, EvidenceKind.PinnedHandle, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record ThreadSnapshotEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    uint OsThreadId,
    int ManagedThreadId,
    bool IsAlive,
    bool IsThreadPoolWorker,
    string State,
    IReadOnlyList<StackFrameEvidence> Frames)
    : EvidenceItem(Id, EvidenceKind.ThreadSnapshot, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record GcSnapshotEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    bool CanWalkHeap,
    bool IsServerGc,
    int HeapCount,
    int SegmentCount)
    : EvidenceItem(Id, EvidenceKind.GcSnapshot, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record ThreadPoolSnapshotEvidence(
    string Id,
    string Description,
    string CollectorId,
    string ArtifactId,
    int? MinThreads,
    int? MaxThreads,
    int? TotalThreads,
    int? RunningThreads,
    int? IdleThreads,
    int? CpuUtilizationLimit)
    : EvidenceItem(Id, EvidenceKind.ThreadPoolSnapshot, Description, CollectorId, Provenance.Observed, ArtifactId);

public sealed record AnalysisReport(
    string SchemaVersion,
    string Command,
    AnalysisOptions Options,
    ArtifactMetadata Artifact,
    DumpCapabilities Capabilities,
    AnalysisManifest Manifest,
    DiagnosticCompleteness Completeness,
    IReadOnlyList<CollectorExecution> Collectors,
    IReadOnlyList<DiagnosticFinding> Findings,
    IReadOnlyList<EvidenceItem> Evidence,
    bool HasPartialResults)
{
    public ExitCode DetermineExitCode()
    {
        if (HasPartialResults)
        {
            return ExitCode.PartialResult;
        }

        if (Options.FailOn is { } threshold && Findings.Any(f => f.Severity >= threshold))
        {
            return ExitCode.FindingsAtOrAboveFailOn;
        }

        return ExitCode.Success;
    }
}

public sealed class AnalysisException : Exception
{
    public AnalysisException(string message, ExitCode exitCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    public ExitCode ExitCode { get; }
}

public interface IDumpSessionFactory
{
    Task<IDumpSession> OpenAsync(string dumpPath, DateTimeOffset? capturedAtOverrideUtc, CancellationToken cancellationToken);
}

public interface IDumpSession : IAsyncDisposable
{
    ArtifactMetadata Artifact { get; }
    DumpCapabilities Capabilities { get; }
    IEnumerable<HeapObjectInfo> EnumerateHeapObjects(CancellationToken cancellationToken);
    IEnumerable<PinnedHandleInfo> EnumeratePinnedHandles(CancellationToken cancellationToken);
    IEnumerable<ThreadSnapshotData> EnumerateThreads(CancellationToken cancellationToken);
    GcSnapshotData? GetGcSnapshot(CancellationToken cancellationToken);
    ThreadPoolSnapshotData? GetThreadPoolSnapshot(CancellationToken cancellationToken);
}

public interface IEvidenceCollector
{
    string Id { get; }
    Version Version { get; }
    IReadOnlySet<AnalysisFocus> SupportedFocuses { get; }
    IReadOnlySet<DumpCapabilityKind> RequiredCapabilities { get; }
    Task CollectAsync(AnalysisContext context, IEvidenceStore sink, CancellationToken cancellationToken);
}

public interface IDiagnosticRule
{
    string Id { get; }
    Version Version { get; }
    string Title { get; }
    string Summary { get; }
    FindingCategory Category { get; }
    AnalysisFocus Focus { get; }
    IReadOnlySet<EvidenceKind> RequiredEvidence { get; }
    IReadOnlyDictionary<string, string> Thresholds { get; }
    IReadOnlyList<DiagnosticFinding> Evaluate(IEvidenceStore evidence, RulePolicy policy);
}

public interface IEvidenceStore
{
    IReadOnlyList<EvidenceItem> All { get; }
    void Add(EvidenceItem item);
    IReadOnlyList<TEvidence> Get<TEvidence>() where TEvidence : EvidenceItem;
    bool Has(EvidenceKind kind);
}

public sealed class EvidenceStore : IEvidenceStore
{
    private readonly List<EvidenceItem> _items = [];

    public IReadOnlyList<EvidenceItem> All => new ReadOnlyCollection<EvidenceItem>(_items);

    public void Add(EvidenceItem item) => _items.Add(item);

    public IReadOnlyList<TEvidence> Get<TEvidence>() where TEvidence : EvidenceItem =>
        _items.OfType<TEvidence>().ToArray();

    public bool Has(EvidenceKind kind) => _items.Any(item => item.Kind == kind);
}

public sealed class RulePolicy
{
    public const long DefaultLargeObjectThresholdBytes = 100 * 1024 * 1024;
    public const long DefaultHighLargeObjectThresholdBytes = 512 * 1024 * 1024;
    public const int DefaultSyncWaitThreadThreshold = 3;
    public const int DefaultSevereSyncWaitThreadThreshold = 10;

    public long LargeObjectBytesThreshold { get; init; } = DefaultLargeObjectThresholdBytes;
    public long HighLargeObjectBytesThreshold { get; init; } = DefaultHighLargeObjectThresholdBytes;
    public int SyncWaitThreadThreshold { get; init; } = DefaultSyncWaitThreadThreshold;
    public int SevereSyncWaitThreadThreshold { get; init; } = DefaultSevereSyncWaitThreadThreshold;
}
