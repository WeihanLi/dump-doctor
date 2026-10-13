# DumpDoctor

**Evidence-driven analysis of .NET memory dumps.** DumpDoctor opens a .NET dump, collects runtime evidence, and produces explainable findings for memory pressure and blocking patterns. It tells you what it found, what supports it, and what it could not determine from the artifacts you supplied.

> **Status: pre-alpha MVP.** The repository now contains a working .NET 10 CLI with ClrMD-backed dump inspection, JSON/Markdown reporting, and an initial evidence/rule pipeline. See [`docs/design.md`](./docs/design.md) for the full design and planned phases that are still in progress.

---

## Why DumpDoctor?

Tools like `dotnet-dump` + SOS, WinDbg, and PerfView are powerful, but they are expert-driven and manual. DumpDoctor is an automated triage layer built on top of them:

- **Evidence before conclusions.** Every finding links to the observations behind it.
- **Honest about uncertainty.** A large cache is not automatically a leak, and one blocked thread is not starvation. Findings carry a confidence level, the reasons for it, and what would raise it.
- **Tells you what's missing.** A dump is a snapshot. Reports state which analyses were impossible with the artifacts you provided (for example, historical CPU attribution from a dump alone).
- **CI-friendly.** Stable JSON output and exit codes.
- **Offline and private by default.** Dumps can contain secrets. Nothing is uploaded, and reports redact sensitive values.

## What it detects today

| Area | Examples | Needs |
|---|---|---|
| Memory | Heap composition, top types, LOH-sized objects, pinned handles, GC state | One dump |
| Threads | Managed thread stacks, repeated sync-over-async style waits (`.Result`, `.Wait()`, blocking waits) | One dump |
| Reports | JSON and Markdown reports, evidence inventory, diagnostic completeness, stable exit codes | One dump |

A dump alone cannot prove a leak, sustained high CPU, or evolving starvation. DumpDoctor says so rather than guessing. Multi-dump comparison, retained-size analysis, CPU trace correlation, and stronger starvation diagnosis are still planned.

## Current CLI usage

```bash
# Analyze one dump
dumpdoctor analyze ./app.dmp

# Focus on memory and write a Markdown report
dumpdoctor analyze ./app.dmp --focus memory --format markdown --output ./report.md

# CI gate: exit non-zero on High-severity findings
dumpdoctor analyze ./app.dmp --format json --output ./report.json --fail-on high

# Inspect raw collected evidence, or list rules
dumpdoctor inspect ./app.dmp
dumpdoctor rules list
```

### Implemented commands

- `analyze`: open one dump, run collectors and rules, and emit JSON or Markdown.
- `inspect`: emit collected evidence without evaluating rules.
- `rules list`: print the shipped rule catalog and default thresholds.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Completed; no finding at or above `--fail-on` |
| 1 | Completed; finding at or above `--fail-on` |
| 2 | Invalid arguments or configuration |
| 3 | Input missing, corrupt, or unsupported |
| 4 | Partial result (some collectors failed) |
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

Current implementation target:

- DumpDoctor runs on **.NET 10**.
- The MVP uses **ClrMD** and is intended for modern .NET dumps.
- `.NET Framework`, NativeAOT, Mono, multi-dump comparison, and trace/metrics ingestion are not implemented yet.
- Symbol/DAC download is still conservative: the CLI exposes `--offline` / `--allow-symbol-server`, but the current implementation relies on what ClrMD can resolve locally when opening the dump.

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

See the [design document](./docs/design.md) for details.

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
```

## Building from source

Prerequisite: **.NET SDK 10.0.401** or later in the 10.0 feature band.

```bash
dotnet build dump-doctor.slnx
dotnet test dump-doctor.slnx
dotnet run --project src/DumpDoctor.Cli -- --help
```

Example analysis run:

```bash
dotnet run --project src/DumpDoctor.Cli -- analyze ./app.dmp --format markdown --output ./report.md
```

## Contributing

The most useful contributions right now are:

- Sample scenarios and sanitized dumps for the test corpus.
- Additional deterministic rules and evidence collectors.
- Review of the design document and the open decisions in §19.

Please do **not** attach real production dumps to issues or pull requests. They may contain credentials and personal data.

## Acknowledgments

DumpDoctor builds on [ClrMD](https://github.com/microsoft/clrmd) and the .NET diagnostics ecosystem (`dotnet-dump`, `dotnet-trace`, `dotnet-counters`, SOS).
