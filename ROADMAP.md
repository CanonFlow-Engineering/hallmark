# Hallmark delivery roadmap

Measured current state is in QUALIFICATION.md. These milestones are
promotion gates, not promises inferred from a passing command.

| Milestone | Current delivery | Remaining acceptance |
|---|---|---|
| M0 typed planning | F# planner implemented and separately tested. Baseline FsAssay Inconclusive. | Dhall package/workspace authoring, typed dependency outputs, frozen workspace resolution and supported Assay authority |
| M1 local execution/artifacts | Strict wire/schema, installed CLI, process budgets, local vault, source identity, CAS and portable signed releases | Reference distribution and clean-host preflight, stable CLI and MCP contract, scoped effects, hermetic adapters and external secret providers |
| M2 install/configuration | Real Compose targets, local locks/ownership, file/service drift, convergence and safe resume refusal | Saved-input evolution, authenticated target binding, distributed fencing, adoption/ownership transfer and hostile race tests |
| M3 migration | Pinned Flyway/PostgreSQL recipe; actual ledger and repeated no-op migration | Target-specific predecessor checks, published migration history, incompatible mixed versions, backup/restore drills and explicit expand/contract policy |
| M4 release | Identified composition, immutable local signing, portable verification and local channel pointers | Scoped local Hallmark decision with independent verification, then external issuers, evidence corrections and multi-environment promotion |
| M5 durable operations | Local records, context exclusion, event integrity, cancellation and bounded documents | Durable workers, leases/fencing, lost-response recovery, concurrency/quotas, retention/GC, metrics/traces and saturation qualification |
| M6 ecosystem | Unmodified wallet and C# source staged/deployed; GSTFlow native failure retained | Native Assay gates, browser and standalone qualification, SaaS and mobile profiles, ONDCFlow and EDIFlow domain receipts, Workstream approval evidence and optional VERA recall |
| M7 production | Not qualified | Authn/authz, tenancy, operational ownership, compatibility/support policy, SLO/RPO/RTO and reviewed Incubation promotion |

## Active sequence — 2026-11-21

1. Preserve the F# reference and implement the Rust recovery decision. Keep
   native build, test and Assay results tied to their actual scopes.
2. Persist the local run and its pending work. Exercise the Wallet migration
   against PostgreSQL with a lost response after commit. Reconcile by ledger
   identity and verify a signed bundle from the installed Rust CLI.
3. Qualify Dhall/JSON parity and local MCP. Run the three-owner release with
   downstream notes and observe where an independent operator needs help.
4. Use that result to prioritize the release inspection UI and the next
   unchanged consumer. Add scheduling and analytics when the workload has
   real schedule and run data.

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

Rust owns lifecycle decisions. Apply RustAssay to Hallmark's declared Rust
core and use Cargo build and tests as separate evidence. Apply the current
FsAssay Shape and authority contracts to changed F# scope. Dhall and JSON feed
the shared workflow contract used by the CLI and MCP.

External builders, migrators and gates use the pinned provider contract.
Workloads keep their native toolchains. Qualify any maintained Playground
helper against its existing fixtures and retain its original evidence under
CF-068.

Local work already authorized by the user proceeds without artificial approval
pauses. Remote publication or production promotion must satisfy its concrete
permissions and repository promotion rules. Preserve failing receipts and stop
unsupported capability claims; never repair another tool's history behind its API.
