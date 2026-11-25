# Hallmark qualification

Hallmark splits responsibilities across two formal components ([Architecture](docs/ARCHITECTURE.md),
[CF-067](REQUIREMENTS.md#cf-067--unify-the-hallmark-cli-rust-and-hallmark-lifecycle-engine-f)):

| Formal name | Responsibility |
|---|---|
| **Hallmark CLI (Rust)** | Operator commands, pinned worker launch and isolation, independent bundle verification |
| **Hallmark Lifecycle Engine (F#)** | Planning, execution, the run journal, recovery decisions, and release admission |

## Observed Qualification (2026-11-24)

The qualification checks use .NET 10.0.115 (`Hallmark.slnx`), Rust 1.100.0
(`rust/Cargo.toml`), and pinned local rootless Podman images for PostgreSQL 18.7
(`sha256:2ea8630df176ad090ec92136dee850397c5af8781e4bcf2e4162593691c61bbc`) and
Flyway 13.12.1
(`sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657`).

| Evidence | Observed result |
|---|---|
| **FsAssay (`Hallmark.slnx`)** | **Pass (`authoritative: true`)**, exit 0, across all 5 F# projects and 17 analyzed source files, with **67/67** registered TRX behavioral, DSL parity, MCP, three-owner release coordination, operator triage, and admission tests passing. |
| **F# Workflow DSL & Local `stdio` MCP (`HM-09`)** | `hallmarkWorkflow { ... }` Computation Expression declarations and hand-authored JSON manifests normalize to identical canonical JSON (`Wire.toJson`) and produce the exact same frozen `planDigest`. The local `stdio` MCP server (`hallmark mcp` / `runtime/Mcp.fs`) returns identical `planDigest`s and Gate B decisions, and refuses `hallmark_apply`/`hallmark_resume` unless `--allow-effects` is enabled. |
| **Three-Owner Release Coordination & VERA Correction Fixture (`HM-10`)** | [`examples/three-owner/fixture.json`](examples/three-owner/fixture.json) and `Admission.evaluateThreeOwnerCutover` exercise three component owners (`postgres-ledger-team`, `kafka-stream-team`, `opensearch-projection-team`) plus an isolated fourth component (`backoffice-reporting`). Unresolved or VERA-withdrawn compatibility receipts hold cutover and notify downstream handoffs across deduplicated dependency paths (`CF-043–CF-045, CF-077, CF-090`). |
| **Operator Incident Triage & Cold-Start Observation (`HM-11`)** | [`scripts/verify-operator-drill.sh`](scripts/verify-operator-drill.sh), `hallmark engine triage`, and MCP `hallmark_triage` report committed target-ledger effects (`migrate` V2), the responsible blocking owner (`opensearch-projection-team`, `VERA-CORR-2026-09`), and the safe `resume` command in one deterministic query. A cold-start isolated operator agent identified all three answers from the prepared incident workspace without prior context; an external human operator study outside the project remains a separate human-observation milestone under `CF-065`. |
| **RustAssay (`rust/src`)** | **Pass**, exit 0, across all 5 `#![no_std]` Rust core files (`0` findings), alongside **16/16** Cargo workspace tests and `cargo clippy --all-targets -- -D warnings` (`0` warnings). |
| **Rootless Podman Worker Launch** | `HALLMARK_WORKER_IMAGE=sha256:... hallmark engine plan` enforces a `sha256:` image digest, verifies rootless Podman (`{{.Host.Security.Rootless}} == true`), maps the host UID/GID via `--userns=keep-id:uid=1654,gid=1654`, and bind-mounts host manifest and state directories. |
| **Six-Stage Wallet Recovery & Release Drill (`HM-08`)** | [`scripts/verify-wallet-engine.sh`](scripts/verify-wallet-engine.sh) runs the full 6-stage Wallet release (`build`, `artifact`, `install`, `configure`, `migrate` via `postgres-migrate`, `verify`, `release`) on rootless Podman PostgreSQL 18.7. Killing the runner after Flyway commits V2 leaves the run journal in `"running"` state. `hallmark engine resume` refuses a tampered V2 checksum, reconciles the committed migration from `public.flyway_schema_history` readback with **1** total Flyway dispatch, refuses release signing when bound Gate B receipts (`FsAssay`, `PostgresAssay`) are missing or have a mismatched `targetDigest`, and seals the release once valid bound receipts are present. |
| **Independent Bundle Verification** | Both the standalone **Hallmark CLI (Rust)** (`hallmark bundle-verify`) and an isolated `--network none --read-only` rootless Podman consumer container verify the signed release bundle (`wallet-release.bundle.zip`), its ECDSA P-256 signature, its attested step evidence, and its 4 artifact SHA-256 digests using only the bundle and trusted public key. |
| **Rust Wallet Migration Reference** | [`scripts/verify-wallet-rust.sh`](scripts/verify-wallet-rust.sh) continues to pass on both Docker and rootless Podman (`podman`). |

Use [`scripts/verify-wallet-engine.sh`](scripts/verify-wallet-engine.sh) to reproduce the six-stage Wallet recovery and release promotion drill, and [`scripts/verify-wallet-rust.sh`](scripts/verify-wallet-rust.sh) for the standalone Rust migration pilot.

## Retained application evidence

The 2026-11-16 reference checks built and deployed the unchanged wallet and
orders sources in prepared fixtures. The wallet exercised seven HTTP probes and
22 independent tests. Orders exercised six HTTP probes, 55 behavior tests and
seven architecture tests. The wallet recipe used Flyway and PostgreSQL. The
original application source, compiler and runtime identities bound those runs.

The GSTFlow NativeAOT build produced a captured binary. Its startup failed with
missing F# `Printf` specialization code and the required verification blocked
release. The 23 passing rule-engine tests describe a separate scope. [Roadmap](ROADMAP.md).
