# DumpDoctor

**Evidence-driven analysis of .NET memory dumps.** DumpDoctor opens a .NET dump, collects runtime evidence, and produces ranked, explainable hypotheses for memory leaks, blocked threads, ThreadPool starvation, deadlocks, and memory exhaustion. It tells you what it found, what supports it, and what it could not determine.

> **Status: design phase (pre-alpha).** The CLI and commands below describe the planned interface. Nothing here is released yet. See [`docs/design.md`](./docs/design.md) for the full design.

---

## Why DumpDoctor?

Tools like `dotnet-dump` + SOS, WinDbg, and PerfView are powerful, but they are expert-driven and manual. DumpDoctor is an automated triage layer built on top of them:

- **Evidence before conclusions.** Every finding links to the observations behind it.
- **Honest about uncertainty.** A large cache is not automatically a leak, and one blocked thread is not starvation. Findings carry a confidence level, the reasons for it, and what would raise it.
- **Tells you what's missing.** A dump is a snapshot. Reports state which analyses were impossible with the artifacts you provided (for example, historical CPU attribution from a dump alone).
- **CI-friendly.** Stable JSON output and exit codes.
- **Offline and private by default.** Dumps can contain secrets. Nothing is uploaded, and reports redact sensitive values.

## What it will detect

| Area | Examples | Needs |
|---|---|---|
| Memory | Heap composition, LOH/pinned footprint, unbounded growth, retention paths | One dump; growth needs 2+ (3+ preferred) |
| Threads | Blocking stack patterns, sync-over-async (`.Result`, `.Wait()`), lock waits, deadlock candidates | One dump |
| ThreadPool starvation | Blocked workers with queue growth | Dump + runtime metrics (planned) |
| CPU / GC | Hot methods, GC pressure | `.nettrace` / metrics (planned) |
| OOM | Managed heap exhaustion, limit mismatches | One dump |

A dump alone cannot prove a leak, sustained high CPU, or evolving starvation. DumpDoctor will say so rather than guess.

## Planned usage

```bash
# Analyze one dump
dumpdoctor analyze ./app.dmp

# Focus on memory and write a Markdown report
dumpdoctor analyze ./app.dmp --focus memory --format markdown --output ./report.md

# Compare snapshots in capture order (Phase 2)
dumpdoctor compare ./t0.dmp ./t1.dmp ./t2.dmp

# Add trace and metrics evidence (Phase 3)
dumpdoctor analyze ./app.dmp --trace ./cpu.nettrace --metrics ./metrics.json

# CI gate: exit non-zero on High-severity findings
dumpdoctor analyze ./app.dmp --format json --output ./report.json --fail-on high

# Inspect raw collected evidence, or list rules
dumpdoctor inspect ./app.dmp
dumpdoctor rules list
```

### Exit codes (proposed)

| Code | Meaning |
|---|---|
| 0 | Completed; no finding at or above `--fail-on` |
| 1 | Completed; finding at or above `--fail-on` |
| 2 | Invalid arguments or configuration |
| 3 | Input missing, corrupt, or unsupported |
| 4 | Partial result (some analyzers failed or timed out) |
| 5 | Unexpected internal error |
| 130 | Cancelled |

## Capturing useful dumps

Findings are only as good as the dumps:

- Use the same dump type (full or heap dumps recommended) for every snapshot.
- Capture at comparable points in the workload, after warm-up.
- Take at least three snapshots for trend detection.
- Optionally force a GC beforehand so unreachable garbage doesn't skew comparisons.
- Note the capture time. Use `--captured-at` if file timestamps may have changed.

## Privacy and security

- Processing is local. No dump contents are sent anywhere by default.
- Dumps are treated as untrusted input. Malformed files should produce a clean error, not a crash.
- Symbol and DAC downloads are **off by default** (`--offline`). Enable `--allow-symbol-server` only if you accept network access.
- Report redaction levels: `strict`, `standard` (default), `none`.
- Type names, method names, and paths can be sensitive. Review reports before sharing them.
- Externally invoked tools run with your OS-level permissions.

## Supported environments

_To be finalized (see design decision 1)._ Proposed: DumpDoctor runs on .NET 10 and analyzes modern .NET dumps from Windows and Linux. .NET Framework, NativeAOT, and Mono dumps are out of scope for the first release.

## Roadmap

| Phase | Focus |
|---|---|
| 1. Dump inspection MVP | CLI, ClrMD integration, heap stats, large objects, GC state, thread stacks, JSON/Markdown reports |
| 2. Root-cause analysis | Reference paths, retained-size estimates, ThreadPool and lock analysis, multi-dump comparison, rule engine |
| 3. CPU and runtime correlation | `.nettrace` and metrics, CPU hot paths, starvation detection |
| 4. Advanced diagnostics | Trend detection, native memory integrations, baselines, optional UI |

## Architecture at a glance

```text
Artifacts → Orchestrator → Evidence collectors → Evidence store → Rules → Findings → Report
```

- **Collectors** (ClrMD-based) gather facts from dumps. **Rules** are pure functions over evidence, so they are deterministic and testable without a dump.
- Reporting renders results and contains no diagnostic logic.

See the [design document](./DumpDoctor-Design.md) for details.

## Repository layout

```text
src/
  DumpDoctor.Cli/
  DumpDoctor.Core/
  DumpDoctor.Collectors/
  DumpDoctor.Rules/
  DumpDoctor.Integrations.ClrMD/
  DumpDoctor.Reporting/
tests/
samples/            # intentionally broken apps used to generate test dumps
```

## Building from source

_Not yet available. This section will be filled in once the first project is scaffolded (prerequisite: .NET 10 SDK)._

## Contributing

The project is in the design phase, so the most useful contributions right now are:

- Review of the design document and the open decisions in §19.
- Sample scenarios and sanitized dumps for the test corpus.
- Real-world diagnostic cases the rules should cover.

Please do **not** attach real production dumps to issues or pull requests. They may contain credentials and personal data.

## Acknowledgments

DumpDoctor builds on [ClrMD](https://github.com/microsoft/clrmd) and the .NET diagnostics ecosystem (`dotnet-dump`, `dotnet-trace`, `dotnet-counters`, SOS).
