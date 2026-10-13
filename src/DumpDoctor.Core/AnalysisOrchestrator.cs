namespace DumpDoctor.Core;

public sealed class AnalysisOrchestrator(
    IDumpSessionFactory sessionFactory,
    IEnumerable<IEvidenceCollector> collectors,
    IEnumerable<IDiagnosticRule> rules,
    RulePolicy? policy = null,
    string? toolVersion = null)
{
    private readonly IDumpSessionFactory _sessionFactory = sessionFactory;
    private readonly IReadOnlyList<IEvidenceCollector> _collectors = collectors.ToArray();
    private readonly IReadOnlyList<IDiagnosticRule> _rules = rules.ToArray();
    private readonly RulePolicy _policy = policy ?? new RulePolicy();
    private readonly string _toolVersion = string.IsNullOrWhiteSpace(toolVersion) ? "0.1.0" : toolVersion;

    public IReadOnlyList<IDiagnosticRule> Rules => _rules;

    public async Task<AnalysisReport> AnalyzeAsync(
        string command,
        AnalysisOptions options,
        bool includeFindings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.DumpPath))
        {
            throw new AnalysisException("A dump path is required.", ExitCode.InvalidArguments);
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        await using var session = await _sessionFactory.OpenAsync(
            options.DumpPath,
            options.CapturedAtOverrideUtc,
            cancellationToken);

        var context = new AnalysisContext(options, session, startedAtUtc, _toolVersion);
        var store = new EvidenceStore();
        store.Add(new ArtifactMetadataEvidence(
            Id: "artifact",
            Description: "Artifact metadata captured when the dump was opened.",
            CollectorId: "orchestrator",
            ArtifactId: session.Artifact.Id,
            Path: session.Artifact.Path,
            FileSizeBytes: session.Artifact.FileSizeBytes,
            Sha256: session.Artifact.Sha256,
            CaptureTimeUtc: session.Artifact.CaptureTimeUtc,
            CaptureTimeSource: session.Artifact.CaptureTimeSource,
            DumpKind: session.Artifact.DumpKind,
            RuntimeVersion: session.Artifact.RuntimeVersion,
            OperatingSystem: session.Artifact.OperatingSystem,
            Architecture: session.Artifact.Architecture));

        var collectorStatuses = new List<CollectorCapabilityStatus>();
        var collectorExecutions = new List<CollectorExecution>();
        var hasPartialResults = false;

        foreach (var collector in _collectors.Where(c => SupportsFocus(c.SupportedFocuses, options.Focus)))
        {
            if (!TryGetMissingCapability(session.Capabilities, collector.RequiredCapabilities, out var missingCapability))
            {
                collectorStatuses.Add(new CollectorCapabilityStatus(
                    collector.Id,
                    collector.Version.ToString(),
                    DiagnosticCapabilityStatus.Runnable,
                    "All required capabilities were available."));

                var collectorStarted = DateTimeOffset.UtcNow;
                try
                {
                    await collector.CollectAsync(context, store, cancellationToken);
                    collectorExecutions.Add(new CollectorExecution(
                        collector.Id,
                        collector.Version.ToString(),
                        Succeeded: true,
                        Message: "Completed successfully.",
                        Duration: DateTimeOffset.UtcNow - collectorStarted));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    hasPartialResults = true;
                    collectorExecutions.Add(new CollectorExecution(
                        collector.Id,
                        collector.Version.ToString(),
                        Succeeded: false,
                        Message: ex.Message,
                        Duration: DateTimeOffset.UtcNow - collectorStarted));
                }
            }
            else
            {
                collectorStatuses.Add(new CollectorCapabilityStatus(
                    collector.Id,
                    collector.Version.ToString(),
                    DiagnosticCapabilityStatus.NotPossible,
                    $"Missing capability: {missingCapability}."));
            }
        }

        var applicableRules = _rules.Where(rule => options.Focus is AnalysisFocus.All || rule.Focus == options.Focus).ToArray();
        var ruleStatuses = new List<RuleEvaluationStatus>();
        var findings = new List<DiagnosticFinding>();

        foreach (var rule in applicableRules)
        {
            var missingEvidence = rule.RequiredEvidence.Where(kind => !store.Has(kind)).ToArray();
            if (missingEvidence.Length > 0)
            {
                ruleStatuses.Add(new RuleEvaluationStatus(
                    rule.Id,
                    rule.Version.ToString(),
                    DiagnosticCapabilityStatus.NotPossible,
                    $"Missing evidence: {string.Join(", ", missingEvidence)}."));
                continue;
            }

            ruleStatuses.Add(new RuleEvaluationStatus(
                rule.Id,
                rule.Version.ToString(),
                DiagnosticCapabilityStatus.Runnable,
                "All required evidence was collected."));

            if (!includeFindings)
            {
                continue;
            }

            findings.AddRange(rule.Evaluate(store, _policy));
        }

        var runnableRuleCount = ruleStatuses.Count(status => status.Status == DiagnosticCapabilityStatus.Runnable);
        var completeness = new DiagnosticCompleteness(
            RunnableRules: runnableRuleCount,
            ApplicableRules: applicableRules.Length,
            Coverage: applicableRules.Length == 0 ? 1D : runnableRuleCount / (double)applicableRules.Length,
            Rules: ruleStatuses,
            Collectors: collectorStatuses);

        var completedAtUtc = DateTimeOffset.UtcNow;
        var manifest = new AnalysisManifest(
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            ToolVersion: _toolVersion,
            HostRuntimeVersion: Environment.Version.ToString(),
            Offline: options.Offline,
            NetworkAccessPerformed: false,
            CollectorVersions: _collectors.ToDictionary(collector => collector.Id, collector => collector.Version.ToString()),
            RuleVersions: _rules.ToDictionary(rule => rule.Id, rule => rule.Version.ToString()));

        return new AnalysisReport(
            SchemaVersion: "1.0.0",
            Command: command,
            Options: options,
            Artifact: session.Artifact,
            Capabilities: session.Capabilities,
            Manifest: manifest,
            Completeness: completeness,
            Collectors: collectorExecutions,
            Findings: findings
                .OrderByDescending(finding => finding.Severity)
                .ThenByDescending(finding => finding.Confidence)
                .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
                .ToArray(),
            Evidence: store.All,
            HasPartialResults: hasPartialResults);
    }

    private static bool SupportsFocus(IReadOnlySet<AnalysisFocus> supportedFocuses, AnalysisFocus requestedFocus) =>
        requestedFocus is AnalysisFocus.All || supportedFocuses.Contains(requestedFocus);

    private static bool TryGetMissingCapability(
        DumpCapabilities capabilities,
        IReadOnlySet<DumpCapabilityKind> requirements,
        out DumpCapabilityKind missingCapability)
    {
        foreach (var requirement in requirements)
        {
            if (!capabilities.Supports(requirement))
            {
                missingCapability = requirement;
                return true;
            }
        }

        missingCapability = default;
        return false;
    }
}
