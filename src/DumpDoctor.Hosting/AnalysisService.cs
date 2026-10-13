using DumpDoctor.Collectors;
using DumpDoctor.Core;
using DumpDoctor.Integrations.ClrMD;
using DumpDoctor.Rules;

namespace DumpDoctor.Hosting;

public sealed class AnalysisService
{
    private readonly AnalysisOrchestrator _orchestrator;

    public AnalysisService()
    {
        var toolVersion = typeof(AnalysisService).Assembly.GetName().Version?.ToString() ?? "0.1.0";
        _orchestrator = new AnalysisOrchestrator(
            new ClrMdDumpSessionFactory(),
            [
                new HeapStatisticsCollector(),
                new GcAndHandleCollector(),
                new ThreadSnapshotCollector()
            ],
            [
                new LargeObjectFootprintRule(),
                new SyncOverAsyncRule()
            ],
            toolVersion: toolVersion);
    }

    public IReadOnlyList<IDiagnosticRule> Rules => _orchestrator.Rules;

    public Task<AnalysisReport> AnalyzeAsync(
        string command,
        AnalysisOptions options,
        bool includeFindings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.DumpPath))
        {
            throw new AnalysisException(
                $"The dump file '{options.DumpPath}' does not exist.",
                ExitCode.InputMissingCorruptOrUnsupported);
        }

        return _orchestrator.AnalyzeAsync(command, options, includeFindings, cancellationToken);
    }
}
