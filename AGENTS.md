# AGENTS.md

Instructions for coding agents working on **DumpDoctor**, a C#/.NET application for analyzing .NET dumps and related diagnostic artifacts.

## 1. Project mission

Build a reliable, evidence-driven diagnostic tool that helps developers investigate:

- Managed memory growth, object retention, and suspected memory leaks.
- High CPU and excessive garbage collection.
- ThreadPool starvation, blocked threads, lock contention, and hangs.
- Out-of-memory conditions and selected native-memory issues when supported by available evidence.

The initial product is a **local-first CLI**. Prioritize correct evidence collection, explainable findings, reproducible reports, and safe handling of dump files over a broad UI or speculative diagnosis.

A dump is a point-in-time snapshot. Do not claim that one dump proves historical CPU consumption, a memory leak, or ThreadPool starvation unless the available evidence genuinely supports that conclusion. Use traces, runtime counters, or multiple comparable snapshots when necessary.

## 2. Core engineering principles

1. **Evidence before conclusions.** Every diagnostic finding must cite the observations that support it.
2. **Do not overstate certainty.** Distinguish direct observations, hypotheses, and confirmed causes.
3. **Reuse established diagnostics.** Prefer ClrMD, SOS, EventPipe, and supported runtime diagnostic APIs over custom dump-format parsing.
4. **Keep boundaries clean.** Domain logic must not depend on CLI concerns, process execution, or vendor-specific output formats.
5. **Fail gracefully.** One unavailable analyzer should not discard successful independent results.
6. **Make behavior reproducible.** Record relevant artifact, runtime, tool, configuration, and rule versions.
7. **Secure by default.** Treat dumps and traces as sensitive data. Keep analysis local unless the user explicitly opts into another workflow.
8. **Test the evidence and the conclusion.** Tests should verify both collected facts and the diagnostic rules applied to those facts.
9. **Prefer simple, explainable rules first.** Do not introduce machine learning or an LLM into the critical diagnostic path without a specific, testable requirement.
10. **Avoid speculative implementation.** Verify APIs against the target framework and installed package versions; do not invent library methods.

## 3. Repository and architecture boundaries

Follow the repository's existing structure if it has already been established. Otherwise, use the intended separation below:

- `src/cDumpDoctor.Cli`: argument parsing, command handlers, console output, exit codes.
- `src/DumpDoctor.Core`: domain models, abstractions, orchestration contracts, findings, and shared analysis logic.
- `src/DumpDoctor.Analyzers.Memory`: heap statistics, retention analysis, and snapshot comparison.
- `src/DumpDoctor.Analyzers.Threads`: thread stacks, ThreadPool evidence, waits, and locks.
- `src/DumpDoctor.Analyzers.Cpu`: CPU samples, hot paths, and GC correlation.
- `src/DumpDoctor.Integrations.ClrMD`: ClrMD-backed managed dump inspection.
- `src/DumpDoctor.Integrations.DotNetDump`: optional SOS/`dotnet-dump` integration.
- `src/DumpDoctor.Integrations.EventPipe`: trace and runtime-event ingestion.
- `src/DumpDoctor.Reporting`: JSON, Markdown, and later HTML output.
- `tests/`: unit, integration, regression, and end-to-end tests.
- `samples/`: controlled applications that reproduce diagnostic scenarios.

### Dependency rules

- Core domain code must not depend on the CLI or reporting implementation.
- Analyzers should depend on core abstractions and normalized evidence, not raw command output.
- Integrations adapt external libraries and tools to the shared evidence model.
- Reporting consumes analysis results; it must not perform dump inspection or diagnosis.
- Avoid circular project references.
- Add a new project only when it creates a useful, stable boundary.

## 4. Language and framework conventions

- Use the repository's pinned .NET SDK and target frameworks. If none are pinned yet, propose and document the initial choice before broad implementation.
- Use modern C# features supported by the selected target framework, but prioritize readability and compatibility over novelty.
- Enable nullable reference types.
- Use implicit usings only if they match repository conventions.
- Use asynchronous APIs for I/O and process orchestration; do not add artificial async wrappers around CPU-bound work.
- Pass `CancellationToken` through long-running and asynchronous operations.
- Dispose streams, processes, dump/runtime handles, and other disposable resources deterministically.
- Use `System.Text.Json` for JSON unless there is a documented reason not to.
- Prefer immutable result records and explicit domain types for findings, severity, categories, and evidence.
- Avoid unexplained magic numbers. Make thresholds configurable and document their rationale.
- Use clear, descriptive names. Public APIs require XML documentation where their intent or constraints are not obvious.
- Keep methods focused and avoid premature abstractions.

## 5. Diagnostic correctness requirements

### Evidence model

Normalize data from ClrMD, SOS, EventPipe, and runtime counters before applying cross-source rules.

Evidence should carry as much of the following as is applicable:

- Artifact identifier and source.
- Capture timestamp or snapshot identity.
- Runtime version, OS, architecture, and tool version.
- Metric name, numeric value, and unit.
- Whether the value is directly observed or derived.
- Collection limitations or missing prerequisites.

Do not treat missing evidence as zero. Represent unavailable, unsupported, and failed collection separately from a measured zero.

### Findings

Each finding should provide:

- Stable rule/finding ID and rule version.
- Title, summary, category, and severity.
- Confidence or an explicit confidence label with documented meaning.
- Evidence references that can be traced to collected data.
- Limitations and plausible alternative explanations.
- Recommended confirmation steps or remediation.
- Related findings where correlation adds value.

Use enums or equivalent constrained types for severity and categories. Validate confidence values if represented numerically. Do not use confidence as a substitute for explaining the evidence.

### Memory analysis

- Distinguish shallow size, retained size, and process memory; never label them interchangeably.
- A type's instance count or shallow size alone does not prove a leak.
- Interpret heap deltas only when snapshots are reasonably comparable.
- Account for workload changes, cache warm-up, and expected long-lived allocations.
- Use GC-root/reference-path evidence to explain retention when available.
- Make expensive root-path searches bounded or cancellable where practical.
- Avoid dumping raw object contents into reports.

### CPU analysis

- A dump's thread stacks are not a historical CPU profile.
- Attribute hot paths from supported samples or trace data.
- State sampling limitations and missing symbol information.
- Distinguish process CPU saturation, thread blocking, GC overhead, and low-CPU latency where evidence permits.

### ThreadPool and concurrency analysis

- A blocked thread or `.Result`/`.Wait()` frame alone does not prove ThreadPool starvation.
- Correlate blocked workers with queue length, worker count, completed work, CPU utilization, and time-series evidence when available.
- Distinguish a lock wait, a general wait, and a proven deadlock.
- Do not claim a deadlock without enough ownership/wait-cycle evidence.
- Account for runtime and platform differences in exposed ThreadPool state.

### OOM and native memory

- Do not assume that process memory equals managed heap size.
- State when native memory analysis is incomplete or requires external tooling.
- Separate observed allocation/heap failures from inferred root causes.
- Clearly report when a dump lacks the information needed for a reliable conclusion.

## 6. External tools and integrations

- Invoke external tools through a small, testable process-runner abstraction.
- Use argument lists rather than shell-concatenated command strings where the APIs allow it.
- Capture standard output and standard error separately.
- Apply configurable timeouts and cancellation.
- Handle missing executables, non-zero exit codes, malformed output, and incompatible tool versions.
- Avoid parsing localized or unstable human-readable output if a supported structured interface is available.
- Do not assume SOS command output is identical across runtime versions.
- Never silently discard tool errors; include actionable diagnostics in the analysis manifest or report.
- Do not log secrets or raw dump contents.

When using ClrMD or another dependency, verify the exact API for the package version in the repository. Keep version-specific behavior behind the integration boundary.

## 7. CLI and reporting requirements

- Keep commands predictable and documented (`analyze`, `compare`, `inspect`, `rules list`, as implemented).
- Use stable exit codes for success, findings detected, invalid input, partial analysis, and execution failure. Document the exact mapping before relying on it in automation.
- Send human-facing summaries to standard output and diagnostics to standard error where appropriate.
- Support explicit output paths and formats.
- Do not overwrite user files without an intentional, documented behavior.
- Ensure JSON output is machine-readable and versioned if it becomes a public integration contract.
- Include analysis completeness and skipped/failed analyzers in reports.
- Reports must distinguish observed facts from hypotheses and recommendations.
- Avoid reporting raw object values, credentials, tokens, or unnecessary memory addresses.

## 8. Security and resource management

Dump files and traces may contain credentials, tokens, personal information, and application payloads.

- Process artifacts locally by default.
- Do not upload artifacts or send their contents to external APIs unless the user explicitly requests and approves that behavior.
- Treat artifact paths and tool output as untrusted input.
- Validate file existence, readability, and supported formats before analysis.
- Apply configurable limits for artifact size, execution time, concurrency, and expensive operations.
- Avoid opening multiple large dumps concurrently unless explicitly configured.
- Clean up temporary files and child processes reliably.
- Do not execute commands constructed from dump contents.
- Document that diagnostic tools run with the current process's OS permissions.
- Avoid persisting sensitive evidence unnecessarily; define retention behavior for generated reports and temporary data.

## 9. Testing expectations

Every meaningful behavior change should include appropriate tests.

### Unit tests

Test:

- Rule predicates and boundary cases.
- Snapshot comparisons and growth calculations.
- Confidence/severity mapping.
- Evidence correlation and missing-data handling.
- JSON schema and report rendering.
- Cancellation, timeout, and partial-failure behavior where applicable.

### Integration tests

Use small, controlled .NET sample applications to reproduce:

- Long-lived object retention or an intentionally unbounded cache.
- ThreadPool blocking and sync-over-async.
- CPU-intensive loops.
- Large allocations and LOH pressure.
- Lock contention and, where safe, controlled deadlocks.

Capture the required dumps/traces in an isolated test environment and assert on stable evidence rather than absolute memory addresses or brittle exact stack strings.

### Regression tests

- Include malformed, truncated, incomplete, and unsupported artifacts.
- Keep any checked-in dump fixtures small, sanitized, and legally redistributable.
- Do not commit production dumps or real customer data.
- Verify behavior against the documented runtime/OS matrix.
- Avoid tests that depend on nondeterministic timing without explicit tolerances.

### Test quality

- Tests must be deterministic wherever practical.
- Do not weaken a test merely to make an implementation pass; fix the underlying behavior or document the unavoidable nondeterminism.
- Use descriptive test names that state the scenario and expected result.
- Add a regression test for every fixed bug when feasible.

## 10. Performance and reliability

- Treat heap enumeration and GC-root searches as potentially expensive operations.
- Report progress for long-running stages where useful.
- Make long-running operations cancellable.
- Avoid retaining duplicate copies of large evidence collections.
- Stream large outputs when practical.
- Bound concurrency and memory use.
- Record duration and failure details per analyzer.
- Prefer partial, explicitly qualified results over losing all findings because one independent stage failed.

Do not add concurrency just for its own sake. First measure whether a stage is a bottleneck and ensure the underlying diagnostic APIs support concurrent use.

## 11. Documentation requirements

Update documentation when a change affects:

- CLI syntax or exit codes.
- Supported runtimes, operating systems, or architectures.
- Diagnostic rule semantics or thresholds.
- Report schemas.
- Configuration defaults.
- External tool prerequisites.
- Security or artifact-retention behavior.

Document important limitations prominently, especially the distinction between dump snapshots and time-series evidence.

## 12. Coding-agent workflow

Before implementation:

1. Inspect the repository, existing instructions, project files, SDK pinning, and current tests.
2. Read the relevant design/specification and identify the exact scope of the task.
3. Check the actual APIs and versions available in the repository before designing around them.
4. Write down assumptions or unresolved decisions that materially affect correctness.
5. Prefer the smallest coherent change that meets the acceptance criteria.

While implementing:

1. Follow the architecture and dependency boundaries.
2. Add or update tests alongside the implementation.
3. Keep findings grounded in explicit evidence.
4. Handle cancellation, resource disposal, and partial failures.
5. Avoid unrelated refactors and dependency additions.
6. Do not introduce external network calls into offline analysis paths.

Before finishing:

1. Build the affected projects.
2. Run relevant unit and integration tests.
3. Run formatting and static analysis if configured.
4. Review the diff for accidental secrets, large artifacts, unrelated changes, and misleading diagnostic claims.
5. Update docs and examples when behavior changes.
6. Summarize what changed, tests executed, known limitations, and any commands that could not be run.

If a build or test cannot run because of environment limitations, state that clearly. Do not claim it passed.

## 13. Definition of done

A change is complete when applicable items below are satisfied:

- [ ] Acceptance criteria are met.
- [ ] Architecture and dependency rules are preserved.
- [ ] Relevant tests are added or updated.
- [ ] Build and tests pass, or environment-related blockers are documented.
- [ ] Errors and missing evidence are handled explicitly.
- [ ] Diagnostic claims are appropriately qualified.
- [ ] Cancellation and resource cleanup are correct for long-running operations.
- [ ] User-facing documentation and report schemas are updated as needed.
- [ ] No sensitive artifacts, secrets, or unrelated generated files are included.
- [ ] Final summary lists implementation details, validation performed, and remaining limitations.

## 14. Decision-making defaults

Unless a task or accepted specification says otherwise:

- Prefer local CLI analysis over a hosted service.
- Prefer ClrMD for programmatic managed dump inspection and SOS as a complementary integration.
- Prefer structured evidence and deterministic rules over opaque diagnosis.
- Prefer JSON plus Markdown reports for the first release.
- Prefer explicit “insufficient evidence” findings over unsupported conclusions.
- Prefer configurable thresholds with documented defaults over universal hard-coded limits.
- Defer UI, remote analysis, automatic remediation, and LLM-based diagnosis until the core analysis pipeline is reliable.

When a requirement conflicts with these instructions, follow higher-priority repository or user instructions and call out the conflict if it affects correctness or security.
