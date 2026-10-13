using System.Security.Cryptography;
using DumpDoctor.Core;
using Microsoft.Diagnostics.Runtime;

namespace DumpDoctor.Integrations.ClrMD;

public sealed class ClrMdDumpSessionFactory : IDumpSessionFactory
{
    public async Task<IDumpSession> OpenAsync(
        string dumpPath,
        DateTimeOffset? capturedAtOverrideUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dataTarget = DataTarget.LoadDump(dumpPath);
            var clrInfo = dataTarget.ClrVersions.FirstOrDefault();
            if (clrInfo is null)
            {
                dataTarget.Dispose();
                throw new AnalysisException(
                    "No CLR runtime was found in the dump.",
                    ExitCode.InputMissingCorruptOrUnsupported);
            }

            var runtime = clrInfo.CreateRuntime();
            var fileInfo = new FileInfo(dumpPath);
            var captureTimeUtc = capturedAtOverrideUtc ?? fileInfo.LastWriteTimeUtc;
            var captureTimeSource = capturedAtOverrideUtc.HasValue ? "UserSupplied" : "FileSystem";
            var sha256 = await ComputeSha256Async(dumpPath, cancellationToken);
            var artifact = new ArtifactMetadata(
                Id: Path.GetFileNameWithoutExtension(dumpPath),
                Path: Path.GetFullPath(dumpPath),
                FileSizeBytes: fileInfo.Length,
                Sha256: sha256,
                CaptureTimeUtc: captureTimeUtc,
                CaptureTimeSource: captureTimeSource,
                DumpKind: InferDumpKind(dumpPath),
                RuntimeVersion: clrInfo.Version.ToString(),
                OperatingSystem: dataTarget.DataReader.TargetPlatform.ToString(),
                Architecture: dataTarget.DataReader.Architecture.ToString());

            var capabilities = new DumpCapabilities(
                CanOpenRuntime: true,
                CanEnumerateHeap: runtime.Heap.CanWalkHeap,
                CanEnumerateThreads: runtime.Threads.Length >= 0,
                CanEnumerateHandles: true,
                CanReadGcState: true,
                CanReadThreadPool: runtime.ThreadPool is not null,
                Limitations: Array.Empty<string>());

            return new ClrMdDumpSession(dataTarget, runtime, artifact, capabilities);
        }
        catch (AnalysisException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AnalysisException(
                $"Failed to open dump '{dumpPath}': {ex.Message}",
                ExitCode.InputMissingCorruptOrUnsupported,
                ex);
        }
    }

    private static string InferDumpKind(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
            ? "WindowsDump"
            : extension.Equals(".core", StringComparison.OrdinalIgnoreCase)
                ? "ElfCore"
                : "Unknown";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}

internal sealed class ClrMdDumpSession(
    DataTarget dataTarget,
    ClrRuntime runtime,
    ArtifactMetadata artifact,
    DumpCapabilities capabilities) : IDumpSession
{
    public ArtifactMetadata Artifact { get; } = artifact;
    public DumpCapabilities Capabilities { get; } = capabilities;

    public IEnumerable<HeapObjectInfo> EnumerateHeapObjects(CancellationToken cancellationToken)
    {
        foreach (var clrObject in runtime.Heap.EnumerateObjects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeName = clrObject.Type?.Name ?? "<unknown>";
            yield return new HeapObjectInfo(
                Address: clrObject.Address,
                TypeName: typeName,
                SizeBytes: clrObject.Size,
                IsLargeObject: clrObject.Size >= 85_000,
                Generation: TryGetGeneration(clrObject));
        }
    }

    public IEnumerable<PinnedHandleInfo> EnumeratePinnedHandles(CancellationToken cancellationToken)
    {
        foreach (var handle in runtime.EnumerateHandles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!handle.IsPinned || handle.Object == 0)
            {
                continue;
            }

            var clrObject = runtime.Heap.GetObject(handle.Object);
            yield return new PinnedHandleInfo(
                HandleAddress: handle.Address,
                ObjectAddress: handle.Object,
                TypeName: clrObject.Type?.Name ?? "<unknown>",
                SizeBytes: clrObject.Size);
        }
    }

    public IEnumerable<ThreadSnapshotData> EnumerateThreads(CancellationToken cancellationToken)
    {
        foreach (var thread in runtime.Threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frames = thread.EnumerateStackTrace()
                .Select(frame => new StackFrameEvidence(
                    StackPointer: frame.StackPointer,
                    InstructionPointer: frame.InstructionPointer,
                    DisplayString: frame.FrameName ?? frame.Method?.Signature ?? "<unknown frame>"))
                .ToArray();

            yield return new ThreadSnapshotData(
                OsThreadId: thread.OSThreadId,
                ManagedThreadId: thread.ManagedThreadId,
                IsAlive: thread.IsAlive,
                IsThreadPoolWorker: IsLikelyThreadPoolWorker(frames),
                State: thread.State.ToString(),
                Frames: frames);
        }
    }

    public GcSnapshotData? GetGcSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new GcSnapshotData(
            CanWalkHeap: runtime.Heap.CanWalkHeap,
            IsServerGc: runtime.Heap.IsServer,
            HeapCount: runtime.Heap.SubHeaps.Length,
            SegmentCount: runtime.Heap.Segments.Length);
    }

    public ThreadPoolSnapshotData? GetThreadPoolSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var threadPool = runtime.ThreadPool;
            if (threadPool is null)
            {
                return null;
            }

            return new ThreadPoolSnapshotData(
                MinThreads: threadPool.MinThreads,
                MaxThreads: threadPool.MaxThreads,
                TotalThreads: threadPool.ActiveWorkerThreads + threadPool.IdleWorkerThreads + threadPool.RetiredWorkerThreads,
                RunningThreads: threadPool.ActiveWorkerThreads,
                IdleThreads: threadPool.IdleWorkerThreads,
                CpuUtilizationLimit: threadPool.CpuUtilization);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        dataTarget.Dispose();
        return ValueTask.CompletedTask;
    }

    private static int? TryGetGeneration(ClrObject clrObject)
    {
        try
        {
            var segment = clrObject.Type?.Heap.GetSegmentByAddress(clrObject.Address);
            return segment is null ? null : (int)segment.GetGeneration(clrObject.Address);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLikelyThreadPoolWorker(IReadOnlyList<StackFrameEvidence> frames) =>
        frames.Any(frame =>
            frame.DisplayString.Contains("ThreadPool", StringComparison.Ordinal) ||
            frame.DisplayString.Contains("PortableThreadPool", StringComparison.Ordinal));
}
