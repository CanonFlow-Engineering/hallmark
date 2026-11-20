# Hallmark

Hallmark is an F# local delivery preview with one validated model for **Build,
Install, Configure, Migrate, Verify and Release**. Version `0.2.0-local.1`
executes trusted local workflows, reconciles Docker Compose deployments, runs
identified migration engines, records recovery evidence and produces portable
signed releases. Enterprise parity and production admission remain unestablished.

Start with the [commands](COMMANDS.md) and [qualification](QUALIFICATION.md). Local inspection and teardown are in
[RUNNING.md](RUNNING.md). Broader selections
are in [FEATURES.md](FEATURES.md); unfinished work is in [ROADMAP.md](ROADMAP.md).

The first planned Hallmark distribution includes a starter workspace, Dhall
authoring, the typed F# lifecycle, an installed CLI and local MCP, and a
clean-host path from .NET upgrade through recovery and signed release
verification. Wallet and orders qualify the reference profile. Python and
Kotlin/JVM workload qualification follows.
[Reference distribution](REQUIREMENTS.md#reference-distribution) · [Counterpart selections](COMPETITORS.md).

## Implemented

- A validated dependency graph with explicit effects, repetition, source,
  configuration, local-context, secret-version, executable and producer identities.
- Strict versioned JSON and packaged JSON Schema. Declared dependency output
  references are checked before execution; a rendered plan does not admit effects.
- Process, atomic file, artifact capture, Compose, bounded HTTP observation and
  signed release operations. Flyway/PostgreSQL is an exercised process recipe.
- Context locks, ownership claims, integrity-checked records, event chains,
  source/output drift checks and refusal of ambiguous imperative retries.
- Secret references with a local AES-GCM store, version-bound resume, explicit
  environment delivery, redacted logs and filtered ambient environment.
- Cancellation, subprocess deadlines/output limits, bounded state documents,
  resume, live Compose drift inspection and explicit convergence.
- SHA-256 artifact storage, immutable ECDSA releases, offline release bundles,
  explicit verifier trust keys, local channel pointers and owned Compose teardown.

Artifact storage deduplicates bytes; hermetic build caching is not established.
Local locks do not fence distributed workers. Manifests can launch programs with
the CLI user's authority and must be trusted.

## Build and use

Use the repository's exact SDK `10.0.301`. From this directory:

```bash
dotnet restore Hallmark.slnx --locked-mode
dotnet build Hallmark.slnx -c Release --no-restore
dotnet test Hallmark.slnx -c Release --no-build --no-restore
dotnet pack cli/Hallmark.Cli.fsproj -c Release --no-build --no-restore -o artifacts/packages
dotnet tool install Hallmark.Cli --version 0.2.0-local.1 --tool-path artifacts/tools --add-source artifacts/packages
artifacts/tools/hallmark --help
artifacts/tools/hallmark plan examples/local/hallmark.json --state-dir artifacts/state
artifacts/tools/hallmark apply examples/local/hallmark.json --state-dir artifacts/state
```

The local example has seven steps spanning all six stage kinds. Its migration is
an explicit local marker fixture. The Playground recipes use real Flyway and
PostgreSQL. Application source was staged separately and retained under its
chosen framework, language and SDK; packaging adaptations are recorded.

## Verification and home

The separate xUnit run passes **52 tests**, with no skipped cases. Real sample
suites pass 22 wallet tests, 55 C# behavior tests and 7 C# architecture tests.
See the [Playground preparation guide](examples/playground/README.md) for the
recipes and [qualification](QUALIFICATION.md) for their evidence scope.

`scripts/verify.sh TOOL_ROOT FRESH_EVIDENCE_DIR NUGET_CACHE` runs the compiler,
tests and actual FsAssay check. Report Assay evidence under the supported producer contract.
Its original outcome is **Inconclusive**, exit **2**, with five projects loaded and 14 files
analyzed. The candidate is dirty and FsAssay's supported MTP/TRX route does not
admit this xUnit v2 producer. Its 687 experimental/prototype observations remain
visible. Required tests, rule admission and suppressions were not weakened.
Zero admitted blocking rules is not a purity or readiness certificate.

Home: `CanonFlow-Engineering/hallmark`. Consumers: the local example and staged Playground
applications. Origin: the unpublished CanonFlowLabs experiment. Production or
assurance-kernel promotion must satisfy
the repository's promotion contract and remaining qualification. Stop: no
production deployment or distributed execution authority follows from this
preview. CanonFlow's assurance kernel remains independent. Repository publication
is an experimental source handoff, not production qualification.

## Standalone repository

This repository contains its SDK pin, central dependency versions, license and
all five F# projects. No Labs checkout is required to build or run the local
example. Historical receipts keep their original source identities; see
[Publication and evidence](PUBLICATION.md).
