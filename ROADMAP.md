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

## Next sequence — revised 2026-11-20

The first milestone is a clean-host reference distribution for a Dhall-authored
.NET upgrade. The installed CLI and local MCP expose the typed F# model.
Wallet and orders demonstrate upgrade, recovery, actual Assay and runtime
evidence, and an independently verifiable local Hallmark decision.
[Reference distribution](REQUIREMENTS.md#reference-distribution).
The [implementation sequence](PLAN.md#implementation-sequence) identifies the
code boundaries, deliverables and acceptance cases for each step.

1. Preserve the baseline and identify a supported registered FsAssay test
   producer for the implementation candidate. Keep the original xUnit and
   FsAssay receipts with their source and policy identities.
2. Build the shared authoring and execution path. Qualify Dhall and JSON input,
   plan identity, the installed CLI, local MCP and the registered gate contract
   on the same fixture. Ship the prerequisite manifest and starter workspace
   with the installed tools. Freeze workspace resolution, validate phase-specific
   dependencies and compare the CLI and MCP plans on a clean host.
3. Complete the wallet upgrade and recovery using version fixtures, then
   exercise the contracts with C# orders. Prove fresh and upgraded state
   equivalence for the eligible predecessor and selected target. Preserve
   compatible saved inputs and exercise restore, migration integrity and
   expand/contract. Qualify scoped MCP apply and resume. Gate the .NET scopes
   with FsAssay and CsAssay and observe the exact packaged runtimes. Verify
   the signed local policy decision in a separate environment.
4. Qualify PythonAssay with native Python tests and KotlinAssay with native
   Kotlin/JVM build and test evidence. Exercise a mixed workload release. Use
   published baselines, targeted overrides and scoped captures to reproduce
   selected tenant state in a sandbox.
5. Exercise the shared gate with a GSTFlow packaged runtime and an ONDCFlow
   offline assessment. Reject receipt reuse across distinct EDIFlow rule packs.
   These cases can run alongside Python and Kotlin qualification after wallet
   and orders establish the gate contract.
   [Consumer acceptance cases](REQUIREMENTS.md#ecosystem-acceptance-scenarios).
6. Use a GSTFlow browser artifact and standalone package as the next
   delivery candidates and an ONDCFlow SDK package for registry consumption.
   Qualify hosted tenant activation on a server fixture and exercise one
   mobile test channel with device evidence. Extend to other Android and
   iOS profiles independently. Exercise compatible client and service
   upgrades as each profile is added.
   [Application delivery cases](REQUIREMENTS.md#application-delivery-acceptance-scenarios).
7. Qualify a hermetic builder, an existing OCI store and VM and Kubernetes
   profiles individually. Add external trust, secrets and evidence, tenant
   rollout rings and authenticated remote MCP when their target policies and
   qualification are ready. Qualify Workstream approval with target authority
   and require authorized evidence corrections and current trust checks for
   signed release decisions.
8. Tie distributed execution to a real workload with tested recovery, quotas
   and fencing. Schedule optional VERA recall, portal, update automation, cost
   estimates and traffic debugging as their consumers justify them.

## F# and research-tool boundary

Keep lifecycle decisions, planning, admission, supported orchestration and
recovery in F#. Apply the current FsAssay Shape/authority contracts to changed
F# scope and retain separate compilation/behavior evidence. External builders,
migration engines, scanners and speech recognizers may retain their own language
behind a pinned, explicit provider boundary.

Dhall provides typed authoring for Hallmark package and workspace definitions.
The evaluator resolves the declared inputs into the workflow format, and F#
validates the graph and owns execution. The published JSON schema remains an
interchange contract for both CLI and MCP. Workloads keep their own .NET, Python
or Kotlin toolchains and the policy selects the appropriate Assay for each scope.

One-off Python transcription/reference-download scripts remain external research
tools. Before a prototype becomes maintained automation, move its reusable
rules, validation, orchestration and receipt handling to F#. The existing active
Playground prepare/build/probe/verify helpers are candidates for a behavior-
preserving F# port under CF-068. Preserve historical Python evidence copies and
their original receipts. No product Python dependency was added by this review.

Local work already authorized by the user proceeds without artificial approval
pauses. Remote publication or production promotion must satisfy its concrete
permissions and repository promotion rules. Preserve failing receipts and stop
unsupported capability claims; never repair another tool's history behind its API.
