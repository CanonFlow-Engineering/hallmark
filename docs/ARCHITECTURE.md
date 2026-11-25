# Hallmark Architecture Decision Record: Hallmark CLI (Rust) & Hallmark Lifecycle Engine (F#)

- **Status:** Proposed Architecture (Implemented in `Hallmark.slnx` and `rust/Cargo.toml`; Promotion Gate: Live Clean-Host Wallet Drill `HM-08`)
- **Date:** 2026-11-24
- **Requirement:** [CF-067](../REQUIREMENTS.md#cf-067--unify-the-hallmark-cli-rust-and-hallmark-lifecycle-engine-f)

---

## 1. Component Ownership

| Formal name | Responsibility |
|---|---|
| **Hallmark CLI (Rust)** | Operator commands, pinned worker launch and isolation, independent bundle verification |
| **Hallmark Lifecycle Engine (F#)** | Planning, execution, the run journal, recovery decisions, and release admission |

### Transition and Promotion Rule

For the six-stage Hallmark product (`Build`, `Install`, `Configure`, `Migrate`, `Verify`, `Release`), the **Hallmark Lifecycle Engine (F#)** (`Hallmark.slnx`) owns the single run journal, workflow planning, execution, target-aware recovery decisions, convergence, and Gate B release admission. The **Hallmark CLI (Rust)** (`rust/Cargo.toml`) owns the installed operator entry point (`hallmark`), rootless Podman worker launch and isolation (`Containerfile.worker`), and independent verification of signed release bundles.

The existing Rust Wallet migration pilot remains the qualified migration path until the F# PostgreSQL adapter passes the clean-host Wallet promotion drill (`HM-08`):
1. Missing or mismatched bound Gate B receipts prevent release signing.
2. An interruption after PostgreSQL commits recovers from target ledger readback with **one** migration dispatch.
3. A separate consumer verifies the signed release bundle against its trust inputs.

---

## 2. Boundary Diagram

```text
================================================================================
                        OPERATOR & CONTAINER BOUNDARY
================================================================================
                                     │
                                     ▼
   ┌─────────────────────────────────────────────────────────────────────────┐
   │  Hallmark CLI (Rust) — `rust/cli/src/main.rs` (`hallmark`)              │
   │  - Operator command parsing and UTF-8 argument validation               │
   │  - Environment sanitization (`env_clear()` + approved whitelist)        │
   │  - Pinned rootless Podman worker launch (`HALLMARK_WORKER_IMAGE`        │
   │    with `sha256:` digest enforcement, `--userns=keep-id:uid=1654`,      │
   │    `--cap-drop=all`, `--security-opt=no-new-privileges`, `--read-only`, │
   │    and explicit workspace/state bind mounts)                            │
   │  - Independent bundle signature and SHA-256 verification                │
   └─────────────────────────────────┬───────────────────────────────────────┘
                                     │
                    Strict JSON / NDJSON Wire Contract
                                     │
=====================================▼==========================================
                  Hallmark Lifecycle Engine (F#) — `Hallmark.slnx`
              (Executed in Rootless Podman Worker `Containerfile.worker`)
================================================================================

   ┌─────────────────────────────────────────────────────────────────────────┐
   │  1. Six-Stage Planner & Execution Engine (`Planning.fs`, `Runner.fs`)   │
   │  - Six-stage DAG: Build -> Install -> Configure -> Migrate -> Verify -> │
   │    Release (with strict Release Ancestor Coverage)                      │
   │  - Single run journal (`Storage.fs`) with `fsync` file + directory      │
   │    durability and target-aware `ExternalLedger` recovery readback       │
   │  - Live service, container, and file drift inspection + `converge`      │
   │  - Pure F# `hallmark { ... }` Computation Expression workflow DSL       │
   ├─────────────────────────────────────────────────────────────────────────┤
   │  2. Gate B Semilattice & Release Admission (`Admission.fs`)             │
   │  - Gate B dominance semilattice: ToolFailure > Fail > Inconclusive > Pass│
   │  - Scoped to the Release stage's transitive Verify ancestors            │
   │  - Verifies Assay receipts bound to commit/source, policy, plan, and    │
   │    target digests (`evaluateBoundReleaseGate`) before signing           │
   │  - Migration Ledger v2 (`auditMigrationLedgerV2`): verifies script      │
   │    ordering, checksums, `ExpectedCatalogDigest`, live catalog drift,    │
   │    and active sessions                                                  │
   │  - Bundle v2 (`HM\x01`) target fingerprint admission & VERA lineage     │
   ├─────────────────────────────────────────────────────────────────────────┤
   │  3. Producer Verification Gates (`FsAssay` + `RustAssay`)               │
   │  - `FsAssay` (v1.0.4 L4 FCS Typed AST + registered TRX test execution)  │
   │  - `RustAssay` (verifies the `#![no_std]` Rust kernel in `rust/src`)    │
   └─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Architecture Decisions (`D0`–`D4`)

### Decision 0: Formal Ownership Split & Workflow Authoring
- **Hallmark CLI (Rust):** Operator commands, pinned worker launch and isolation, and independent bundle verification.
- **Hallmark Lifecycle Engine (F#):** Planning, execution, the run journal, recovery decisions, and release admission.
- **Workflow Authoring:** Pure F# Computation Expression DSL (`hallmark { ... }`) and Canonical JSON (`workflow-v1.schema.json`). HydraLattice remains a separate, standalone repository with zero cross-dependencies.

### Decision 1: Rootless Podman Worker Launcher Contract
- When `HALLMARK_WORKER_IMAGE` is set, the **Hallmark CLI (Rust)**:
  1. Verifies that the image reference pins an immutable `sha256:` digest.
  2. Verifies rootless Podman execution via `podman --remote=false info --format {{.Host.Security.Rootless}}` == `"true"`.
  3. Maps the host operator UID/GID to the worker user (`--userns=keep-id:uid=1654,gid=1654`), drops all Linux capabilities (`--cap-drop=all`), disables privilege escalation (`--security-opt=no-new-privileges`), and bind-mounts the canonical host manifest/input directories and `--state-dir` into the container.

### Decision 2: Ancestor-Scoped Gate B Release Admission with Bound Receipts
- `Runner.fs` (`SealRelease`) scopes verification strictly to the `Release` stage's transitive `Verify` ancestors so unrelated verification branches scheduled elsewhere in a workflow cannot block or satisfy release admission.
- When `gate-b-policy.json` is present, `SealRelease` invokes `Admission.evaluateBoundReleaseGate` to verify that every required `AssayKind` receipt is present, passing, and cryptographically bound to the release's `PlanDigest`, `SourceDigest`, `PolicyDigest`, and `TargetDigest`.

### Decision 3: Target-Aware `ExternalLedger` Recovery & Durable Checkpoints
- `Operations.Recovery.decideWithLedger` requires an explicit `ExternalLedgerReadback` observation before replaying any interrupted or failed `ExternalLedger` step:
  - `CommittedInLedger true` $\rightarrow$ `Reuse` (records completion from ledger readback with one dispatch),
  - `AbsentFromLedger` $\rightarrow$ `Execute`,
  - `Unobserved` / `ConflictingLedger` $\rightarrow$ `Refuse` (holds the run for reconciliation).
- `Storage.atomic` and `Storage.storeArtifact` flush file contents (`FileStream.Flush(true)`) and synchronize the parent directory (`RandomAccess.FlushToDisk`) on POSIX filesystems.

### Decision 4: Authoritative Self-Assay Gates
- **Hallmark Lifecycle Engine (F#)** (`Hallmark.slnx`): Verified by `FsAssay` (`1.0.4 verify` with required TRX test execution) $\rightarrow$ **`Pass` (`authoritative: true`)**.
- **Hallmark CLI & Core (Rust)** (`rust/Cargo.toml`): Verified by `RustAssay` (`check rust/src --check-deps rust/Cargo.toml`) $\rightarrow$ **`Pass` (`0` findings)**, plus `cargo test --workspace` and `cargo clippy --workspace --all-targets -- -D warnings`.
