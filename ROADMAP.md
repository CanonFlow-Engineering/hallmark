# Hallmark delivery roadmap

Measured results and the archived reference are in
[qualification](QUALIFICATION.md). Milestones record the promotion gates for the
**Hallmark CLI (Rust)** and **Hallmark Lifecycle Engine (F#)** ([Architecture](docs/ARCHITECTURE.md),
[CF-067](REQUIREMENTS.md#cf-067--unify-the-hallmark-cli-rust-and-hallmark-lifecycle-engine-f)).

| Formal name | Responsibility |
|---|---|
| **Hallmark CLI (Rust)** | Operator commands, pinned worker launch and isolation, independent bundle verification |
| **Hallmark Lifecycle Engine (F#)** | Planning, execution, the run journal, recovery decisions, and release admission |

| Milestone | Current delivery | Remaining acceptance |
|---|---|---|
| M0 typed planning | Rust migration manifest with frozen target and input identities | Port the six-stage graph, then qualify Dhall authoring, typed outputs and frozen workspace resolution |
| M1 local execution/artifacts | Installed migration CLI and bounded Docker calls | Port process execution, CAS and local secret handling. Qualify stable CLI and MCP contracts and hermetic adapters. |
| M2 install/configuration | One identified PostgreSQL fixture and a local run lock | Port Compose ownership, file/service drift and convergence. Qualify saved inputs, adoption, authenticated targets and fencing. |
| M3 migration | One reviewed Wallet V1-to-V2 upgrade, exact predecessor and ledger reconciliation | Published migration history, mixed versions, backup/restore drills and expand/contract policy |
| M4 release | Archived release and verification behavior | Port signed policy decisions and portable bundles, then qualify external issuers, corrections and multi-environment promotion. |
| M5 durable operations | Atomic local pending record and recovery after a lost result | Durable worker coordination, leases, fencing, cancellation, retention, metrics and saturation qualification |
| M6 ecosystem | Wallet database fixture on Rust. Wallet and Orders application runs and GSTFlow failure in the archive. | Native Assay gates, app packaging, browser/standalone/SaaS/mobile profiles, domain receipts, Workstream approval and optional VERA recall |
| M7 production | Experimental local preview | Authn/authz, tenancy, operational ownership, compatibility/support policy, SLO/RPO/RTO and reviewed Incubation promotion |

## Active sequence — 2026-11-22

1. **Delivered.** Archive the F# tree and ship the local Rust migration commands.
   Persist pending work before Flyway dispatch and recover the committed Wallet
   migration by reading PostgreSQL history.
2. Port the signed release record and bundle. Bind the decision to the exact plan,
   runtime evidence and configured RustAssay result. Verify from a clean host.
3. Port the workflow graph and the process, file, artifact and Compose
   operations needed for one full local delivery. Use the archived command
   contract and retained behavior cases to qualify the port.
4. Qualify Dhall/JSON parity and local MCP. Run the three-owner release with
   downstream notes and observe an independent operator.
5. Qualify the release inspection UI and the next unchanged application.

[Gantt](docs/delivery/gantt.html) · [Kanban](docs/delivery/kanban.html)
· [Active requirements](REQUIREMENTS.md#active-delivery-priorities).

## Broader qualification backlog

- Qualify PythonAssay with native Python tests and KotlinAssay with native
   Kotlin/JVM build and test evidence. Exercise a mixed workload release. Use
   published baselines, targeted overrides and scoped captures to reproduce
   selected tenant state in a sandbox.
- Exercise the shared gate with a GSTFlow packaged runtime and an ONDCFlow
   offline assessment. Reject receipt reuse across distinct EDIFlow rule packs.
   These cases can run alongside Python and Kotlin qualification after wallet
   and orders establish the gate contract.
   [Consumer acceptance cases](REQUIREMENTS.md#ecosystem-acceptance-scenarios).
- Use a GSTFlow browser artifact and standalone package as candidate
   delivery candidates and an ONDCFlow SDK package for registry consumption.
   Qualify hosted tenant activation on a server fixture and exercise one
   mobile test channel with device evidence. Extend to other Android and
   iOS profiles independently. Exercise compatible client and service
   upgrades as each profile is added.
   [Application delivery cases](REQUIREMENTS.md#application-delivery-acceptance-scenarios).
- Qualify a hermetic builder, an existing OCI store and VM and Kubernetes
   profiles individually. Add external trust, secrets and evidence, tenant
   rollout rings and authenticated remote MCP when their target policies and
   qualification are ready. Qualify Workstream approval with target authority
   and require authorized evidence corrections and current trust checks for
   signed release decisions.
- Tie distributed execution to a real workload with tested recovery, quotas
   and fencing. Schedule optional VERA recall, portal, update automation, cost
   estimates and traffic debugging as their consumers justify them.

## Engine and provider boundaries

The **Hallmark Lifecycle Engine (F#)** (`Hallmark.slnx`) owns planning, execution,
the run journal, recovery decisions, and Gate B release admission. The
**Hallmark CLI (Rust)** (`rust/Cargo.toml`) owns operator commands, pinned
rootless Podman worker launch and isolation, and independent bundle
verification. Apply `FsAssay` to `Hallmark.slnx` and `RustAssay` to Hallmark's
declared Rust core, with Cargo build and tests as separate evidence. The existing
Rust Wallet pilot remains the qualified migration path until the live Wallet
promotion drill (`HM-08`) passes.

External builders, migrators and gates use the pinned provider contract.
Workloads keep their native toolchains. Qualify any maintained Playground
helper against its existing fixtures and retain its original evidence under
CF-068.

Local work already authorized by the user proceeds without artificial approval
pauses. Remote publication or production promotion must satisfy its concrete
permissions and repository promotion rules. Preserve failing receipts and stop
unsupported capability claims; never repair another tool's history behind its API.
