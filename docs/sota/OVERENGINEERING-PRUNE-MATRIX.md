# Hallmark Overengineering Audit & Requirement Prune Matrix (`CF-001`–`CF-094`)

## 1. Executive Pruning Principles

A foundational infrastructure engine fails when its specification outgrows its verification boundary. Reviewing all 94 requirements in [`REQUIREMENTS.md`](../../REQUIREMENTS.md) against [`FEATURES.md`](../../FEATURES.md) (*"Do not build a replacement container runtime, Kubernetes controller, universal SQL migration executor, package registry or general durable workflow service"*) reveals three distinct classes:

| Disposition | Count | Definition |
|---|---:|---|
| **CORE-V1 (Keep & Consolidate)** | **18** | Essential mathematical, state-machine, and cryptographic invariants that form the SOTA Hallmark v1.0 Rust engine (`HM-01` through `HM-09`, `HM-15`). |
| **DEFER-ADAPTER (Post-v1.0 Plugin)** | **52** | Valid enterprise lifecycle capabilities that belong in external opt-in provider recipes (OpenTofu, ORAS, OpenBao, multi-branch lineage) *after* the Core v1.0 reference distribution ships. |
| **PRUNE-EXCISE (Remove from Scope)** | **24** | Speculative scope creep, duplicate third-party platforms (App Store Connect, Infracost, Telepresence, Renovate, Backstage, interactive Kanban/Gantt engines) that dilute Hallmark's focus. |

---

## 2. Complete Requirement-by-Requirement Audit (`CF-001`–`CF-094`)

### Group A: CORE-V1 — Consolidated into the 4 SOTA Pillars (18 Requirements)

| Req ID | Title | Consolidated SOTA Pillar | Implementation Owner |
|---|---|---|---|
| **CF-001** | Retain the common lifecycle model | **Pillar 1 (`dag.rs`)** | `validate_workflow` (6 stage kinds, cycle check, release ancestor coverage) |
| **CF-002** | Plan against installed state | **Pillar 3 (`history.rs` + `plan.rs`)** | `inspect_history` + `DatabaseIdentity` (`system_identifier`, `database_oid`) |
| **CF-003** | Define both current state and transitions | **Pillar 3 (`history.rs`)** | `verify_forward_equivalence` (fresh baseline vs. transition sequence) |
| **CF-004** | Gate publication on forward checks | **Pillar 3 (`history.rs`)** | `ForwardEquivalenceOutcome::Equivalent` gate before release |
| **CF-005** | Resolve package dependency ranges to exact identities | **Pillar 1 (`dag.rs` + `plan.rs`)** | Exact `sha256:` digest pins in `WorkflowDraft` and `PlanInputs` |
| **CF-020** | Share one planner across CLI and MCP | **Pillar 1 (`dag.rs`)** | Shared `hallmark-core` decision library called by CLI and stdio MCP |
| **CF-028** | Verify publisher identity and retain audit trail | **Pillar 4 (`release.rs`)** | `verify_release_bundle` with `trusted_public_key_ids` |
| **CF-038** | Retain honest recovery and replay boundaries | **Pillar 3 (`history.rs` + `state.rs`)** | `decide_recovery_v2` (session-aware + catalog-drift-aware recovery) |
| **CF-039** | Preserve ownership and concurrency boundaries | **Shell (`state.rs` + `postgres.rs`)** | `Store::lock` (`run.lock` in `0700` dir) + PostgreSQL advisory/container checks |
| **CF-046** | Validate migration history and schema drift independently | **Pillar 3 (`history.rs`)** | `DatabaseObservation.observed_catalog_digest` vs. Flyway CRC32 ledger |
| **CF-053** | Bind evidence admission to exact scope and real verdicts | **Pillar 2 (`gates.rs`)** | `evaluate_gates` (4-state Gate B semilattice reducer) |
| **CF-067** | Build the maintained lifecycle engine in Rust | **All Pillars (`rust/src/`)** | Pure functional Rust core + thin CLI/Podman adapters |
| **CF-069** | Provide a stable CLI for operators and CI | **Shell (`rust/cli/`)** | Deterministic JSON stdout, exit codes `0`, `1`, `2`, `3`, `64` |
| **CF-072** | Dhall/JSON typed authoring into shared model | **Pillar 1 (`dag.rs`)** | Canonical `WorkflowDraft` JSON IR (Dhall lowers via `dhall-to-json`) |
| **CF-074** | Enforce native Assay gates at lifecycle stages | **Pillar 2 (`gates.rs`)** | `GateRequirement` + `GateReceipt` (`RustAssay`, `FsAssay`, `CsAssay`, `PythonAssay`) |
| **CF-077** | Resolve corrected evidence before reuse (VERA) | **Pillar 2 & 4 (`gates.rs`, `release.rs`)** | `VeraDisposition::SupersededBy` / `Withdrawn` blocks release reuse |
| **CF-078** | Attest to a release policy decision | **Pillar 4 (`release.rs`)** | `ReleaseStamp` + portable bundle verification (`AdmittedForTarget` vs `HistoricalStampValidButTargetRefused`) |
| **CF-083** | Bind typed dependency outputs to lifecycle stage | **Pillar 1 (`dag.rs`)** | `OutputBinding` + `StageKind::phase_rank` check (`ForbiddenStageOutputConsumption`) |

---

### Group B: PRUNE-EXCISE — Overengineering Removed from Hallmark Scope (24 Requirements)

> [!WARNING]
> **Architectural Rule**: None of the 24 items below may add types, dependencies, CLI flags, or schema fields to `hallmark-core` or `hallmark-cli`.

| Excised Req IDs | What It Proposed | Why It Is Overengineering & How It Is Replaced |
|---|---|---|
| **CF-059** | Built-in Renovate/Dependabot dependency update proposal bot | External Git bots (Renovate) already do this. Hallmark verifies locked inputs; it does not open dependency PRs. |
| **CF-060** | Backstage-style self-service template portal | A `examples/` directory with 2 copyable starter manifests (`rust-wallet`, `dotnet-orders`) replaces an entire portal subsystem. |
| **CF-063** | Infracost cloud billing & FinOps cost estimation | Billing forecasts have zero place inside a deterministic release-verification and migration engine. |
| **CF-064** | Telepresence / mirrord remote cluster traffic interception | Network packet tunneling for remote Kubernetes pods is an orthogonal developer debugger, not a lifecycle engine feature. |
| **CF-080**, **AC-26** | Apple App Store Connect & Google Play Store submission/review state machine and mobile device fleet probes | Hallmark targets self-hosted Linux, Podman, PostgreSQL, and server/CLI artifacts. Mobile app-store review workflows are a completely different product category. |
| **CF-081** (Browser/Mobile parts), **AC-27** | Service-worker browser cache lifecycle and offline mobile device data migration | Keep client-service API version range checks (`CF-005`/`ContractAssay`), drop browser service-worker and mobile device management. |
| **CF-089**, **CF-091** (Gantt), **CF-093**, **CF-094** (Drag-and-drop UI) | Interactive drag-and-drop Kanban boards, critical-path Gantt schedule forecasting, and multi-team task reassignment web app | Replace with a **single static, read-only HTML/Markdown Release Receipt report** (`hallmark verify-bundle --html report.html`, matching `CsAssay` and `FsAssay`). Use Forgejo Issues/Projects for Kanban tracking. |
| **CF-014**, **CF-015**, **CF-016**, **CF-017**, **CF-018** | Full Velero-style multi-tenant environment capture, data masking, and sandbox cloning engine | Overlaps with database logical backup/restore (`CF-047`) and application config overlays. Keep simple seed-data separation (`CF-013`), excise the multi-system Velero mirror engine. |
| **CF-024**, **CF-025**, **CF-026** | Multi-VM placement solver, Crossplane resource composition, and Kubernetes controller delegation | Hallmark v1.0 runs on **local Rootless Podman** and Linux processes. Do not build a cluster scheduler or Crossplane abstraction layer. |
| **CF-033**, **CF-034**, **CF-035** | Cross-distro/cross-architecture build matrix solver, Unleash feature-flag server integration, and white-label branding engine | Build artifacts identify their target triple (`x86_64-unknown-linux-gnu`) as a plain string attribute in `ReleaseStamp`; feature flags and white-label CSS belong to the application, not the deployment verifier. |

---

### Group C: DEFER-ADAPTER — Parked Until After Core v1.0 Ships (52 Requirements)

These requirements represent legitimate phase-2 capabilities (`M3`–`M7`) that should be implemented **only after** `HM-07`, `HM-08`, `HM-15`, and `HM-09` are merged and qualified:
* **Advanced Database Operations (`CF-006`–`CF-013`, `CF-047`, `CF-085`)**: Expand/contract multi-step backfill batches (`pgroll` pattern), restore drills, and multi-branch migration lineage merges.
* **MCP & Read-Only Inspection Views (`CF-070`, `CF-071`, `CF-087`, `CF-088`, `CF-090`, `CF-092`)**: Local stdio MCP server exposing the exact same `hallmark-core` functions as the CLI, plus static Mermaid dependency/recovery rendering from `run-v1.json`.
* **External Supply-Chain & Secret Providers (`CF-019`, `CF-021`–`CF-023`, `CF-027`, `CF-029`–`CF-032`, `CF-036`, `CF-037`, `CF-040`–`CF-045`, `CF-048`–`CF-052`, `CF-054`–`CF-058`, `CF-061`, `CF-062`, `CF-065`, `CF-066`, `CF-068`, `CF-073`, `CF-075`, `CF-076`, `CF-079`, `CF-082`, `CF-084`, `CF-086`)**: Workstream approval handoff pack verification (`CF-054`), `SecurityAssay` SBOM/advisory integration (`CF-052`), and second-consumer (`Orders` C# / `Vera` F#) lifecycle recipes.
