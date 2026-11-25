# Hallmark platform plan

Product: Hallmark.

Hallmark provides a typed lifecycle for build, install, configuration,
migration, verification and release.

The active engine is Rust, with an installed Wallet recovery pilot. Track dependencies,
status and acceptance evidence in the [Gantt chart](docs/delivery/gantt.html)
and [Kanban board](docs/delivery/kanban.html). The
[delivery priorities](REQUIREMENTS.md#active-delivery-priorities) set the
release boundary for the work.

Home: CanonFlow-Engineering/hallmark, alongside but separate from Workstream.
Origin: the unpublished CanonFlowLabs experiment, retained as historical evidence.

The active consumer is the Wallet database fixture. The earlier local six-kind
workflow, Wallet API, Orders API and GSTFlow NativeAOT cases are reference
evidence for the broader ports. [Qualification](QUALIFICATION.md).

Promotion condition: a separate Incubation candidate demonstrates real-domain
use, an unchanged second consumer, deterministic tests, offline evidence,
failure recovery and the repository's required human promotion decision.

Stop rule: do not turn this experiment into an unqualified deployment system,
silently change CanonFlow's assurance kernel, execute production changes, or
claim support for an adapter that has not been implemented and exercised.

## Architecture direction

Keep a common typed lifecycle contract and explicit effect/evidence semantics.
Use replaceable adapters for established build, infrastructure, configuration,
migration and rollout engines. CanonFlow contributes assurance and admission;
Workstream contributes its existing local evidence/human-decision workflow.
Neither capability is relabelled as permission to execute an external action.

The assurance core keeps its existing pure contracts. The broader requested
lifecycle belongs to a platform layer: the user's request expands the platform
scope without requiring a migration engine inside the assurance kernel.

## Original work sequence

1. Inventory the authenticated GitHub organizations and current repositories.
   Inspect ownership and existing implementations before proposing new homes.
2. Build a source-backed comparison and feature-selection matrix. Separate
   ideas, reusable open-source components, commercial capabilities and measured
   behavior. Record qualification evidence for each claimed operation.
3. Define the essential platform contract: artifact/content identities,
   dependency graph, stage kinds, repetition semantics, target/config identity,
   secret references, admission, execution and evidence. Specify errors and
   refusal behavior before implementation.
4. Implement a local F# foundation that validates and deterministically plans a
   six-stage lifecycle without invoking external engines. Exercise acceptance
   and refusal cases. It is a planning contract, not a deployment claim.
5. Map implementation milestones for real build, configuration, migration and
   release adapters. Include resource ownership, locking, state recovery,
   observability, cancellation, signing, promotion and drift detection.
6. Record the selected organization/repository layout, open decisions,
   acceptance gates and actual validation evidence. Leave a concrete next
   implementation slice and preserve the research artifacts.

## Initial feature candidates

- Dagger and Nix: composable build definitions, isolated execution, dependency
  closure, source identity and cache correctness.
- OpenTofu and Ansible: state/ownership, plan/apply separation, resource
  reconciliation, module interfaces and configuration previews.
- Flyway and Liquibase: migration identity/checksums, preconditions, applied
  history, compatibility and honest rollback limits.
- Temporal: durable orchestration, idempotent activities, cancellation,
  version-compatible replay and ambiguous remote-outcome handling.
- Argo CD and Argo Rollouts: GitOps reconciliation, immutable release inputs,
  progressive rollout and measured health gates.
- Sigstore/in-toto/SLSA: artifact provenance and identity-bound verification.
- Harness, Octopus and JFrog: evaluate release governance, environment/tenant
  modelling, deployment safety, promotion and operational visibility through
  their public documentation. Commercial functionality is a design reference;
  availability and license terms need separate selection.

These are research candidates. The feature matrix will record what to adopt,
adapt, integrate, defer or reject after source review.

## First implementation acceptance

The foundation must represent Build, Install, Configure, Migrate, Verify and
Release as explicit stage kinds; validate stable stage IDs and required content
identities; reject missing dependencies and cycles; plan deterministically;
and refuse reuse of a checkpoint whose relevant identity has changed.

A preview receipt must not establish execution success. An imperative one-time
effect must not be silently treated as repeatable desired state. Tests must
include a valid six-stage graph and concrete negative cases. Production
execution was deferred at M0; remote adapters, approvals and cluster recovery remain later
milestones until their own evidence exists.

The local planner and runtime checks are recorded in
[qualification](QUALIFICATION.md). The roadmap identifies the remaining
profile checks.

## Executed continuation

The M0 work sequence above is retained as the original plan. The approved
continuation implemented a local runtime and installed CLI; exercised build,
configuration, Flyway migration, deployment, explicit fixture exercise, read-only
verification, release, drift repair and teardown/reinstall. The checks
retained runtime failures and unresolved outcomes.
The broader enterprise gates remain in ROADMAP.md. No production-ready or
full-parity claim replaces the observed qualification.

## Implementation sequence

The installed Rust CLI has recovered one Wallet database upgrade. Qualify the
signed release and clean-host handoff next. Use the active delivery plan to
select the scope for the full application and adapter ports.
[Reference distribution](REQUIREMENTS.md#reference-distribution) · [Roadmap](ROADMAP.md).

The local qualification slice includes versioned saved inputs, typed
dependency outputs and frozen workspace resolution. Use one
wallet profile to prove those contracts with actual upgrades. Carry the
qualified contracts into further delivery profiles.

### 1. Reproduce the baseline and identify the verifier route

Run the locked Rust workspace checks and the installed migration fixture.
Record commands, source, producer, toolchain and policy identities, exit codes
and raw receipts. For ports of prior behaviors, use the archived source,
pinned SDK and test cases. Keep the 52 native xUnit results and the historical
FsAssay outcomes with the archive. Qualify any required workload verifier on
its published test producer and scope.
[Qualification](QUALIFICATION.md) · [Contributor checks](AGENTS.md).

### 2. Extend the typed planning and provider contracts

Start with the local server profile. Use the archived planning and recovery
contracts as behavioral references for the Rust graph. Describe selected
package versions, observed
target state, provider capabilities and required gates. Keep planning and
admission decisions in the Rust core. Use adapters for target observations
and effects. Bind the
profile, provider version, resolved inputs and policy to the plan identity.
Define one versioned saved-input group and its update rules. Plan typed
build and installed dependency outputs for the fixture. Freeze direct and
transitive resolution with the profile and source or artifact identities.

Use the archived workflow schema and parser tests for the broader wire
compatibility port. The Rust pilot reads the migration manifest v1. Exercise fresh and
upgrade plans, explicit unsupported
targets and refusal of a checkpoint with changed inputs. A required operation
with an unavailable provider appears in the plan diagnostics before effects.
Reject a wrong-stage output and preserve the saved operator input in a
compatible schema update. Exercise CF-001–CF-005, CF-038, CF-049, CF-062,
CF-079 and CF-082–CF-084.

### 3. Connect Dhall, the installed CLI and local MCP

Pin a Dhall evaluator and define a package and workspace schema for the wallet
workflow. Pin the import closure, exercise offline evaluation and map the
result through the same workflow validation used by JSON. Share planning,
capability and run inspection between the Rust CLI and an MCP stdio host.

Assemble the reference distribution with the CLI, local MCP, prerequisite
manifest, starter workspace, schemas and matching help. Install it on a clean
reference host and exercise MCP with an independent client. The same wallet
inputs and observed state must produce the same plan digest and
diagnostics through both interfaces. Compare replay from the generated lock
after a repository channel advances. Reject invalid types, changed imports,
altered pins and invalid graphs. Add versioned diagnostic codes and offline
remediation to the shared result. Check non-interactive refusal with missing
input or a missing prerequisite. Enable MCP apply and resume after scoped
authorization and the execution cases in step 5 are exercised. Cover
AC-14–AC-16, AC-31, AC-32, AC-37 and the interface portion of AC-35.

### 4. Implement registered Verify gates

Register named producer contracts with the supported scope, invocation and
receipt format. Begin with FsAssay and CsAssay for the wallet and orders.
Evaluate the native verdict under the effective target policy and bind the
decision to the exact source, configuration, producer, artifacts and target
that support the claim. Retain compiler and behavioral results and any
required runtime observations.

Exercise real required gates through the shared command path. A failed test,
an unavailable producer and a receipt for changed bytes each block the
dependent operation and retain their own outcome. The candidate authoring
file cannot remove a gate of the effective policy. Once this contract works,
qualify PythonAssay with native Python tests and KotlinAssay with native JVM
build and test evidence, then compose a mixed release. Exercise AC-12, AC-17
and AC-18.

### 5. Qualify the reference upgrade and signed release

Use small versioned schema fixtures to exercise fresh install, sequential
and supported skip-version upgrades, compatibility refusal, interrupted
backfill and restore. Then run the unchanged wallet sources through the
installed tool with the pinned Flyway/PostgreSQL recipe. Exercise
configuration, verification, release and drift repair. Repeat the shared
consumer contract with the unchanged orders sources and native C# toolchain.

Compare fresh and upgraded state and business behavior, including preserved
data and operator overrides. Compare the eligible predecessor for the observed
target and report a missing required comparison as unresolved. Verify the
applied migration ledger and refuse an edited migration or premature
contraction. Interrupt a run after an external effect, reconcile the target
and resume with the accepted input snapshot. Verify absence of a duplicate
effect and check the recorded diagnostics and progress. Retain each consumer's
Assay, build, behavioral, runtime and recovery evidence for AC-01–AC-12,
AC-17, AC-30, AC-33 and AC-35. [Archived application recipes](QUALIFICATION.md#retained-application-evidence).

Run the exact release candidates and inspect their installed state with the
distributed tools. Exercise an authorized local MCP apply and resume on an
owned fixture and compare the effect policy with the CLI. Extend the
existing signed bundle path in
`runtime/Storage.fs` and `runtime/Bundles.fs` to carry the scoped local
release policy decision under CF-078. Verify it on a separate host or in an
isolated environment using configured trust. Exercise changed target state,
altered bytes and the wrong issuer. Cut the reference distribution after
wallet and orders meet AC-38 and AC-39 with their required gate evidence.


After the local upgrade passes, extend the same ledger checks to two
published branches with a shared migration. Qualify their merge and a forward
correction under AC-34 before claiming multi-channel migration support.

### 6. Qualify packaged browser, standalone and SDK releases

Exercise the GSTFlow browser artifact and a standalone build through their
consumer entry points from staged release packages. Keep the historical
NativeAOT startup failure as a negative case. Exercise an ONDCFlow SDK
package from a clean consumer using a declared test registry. Use the
existing provider boundary for each package type.

Record the exact artifact and supported runtime. Run common fixtures for
variants that claim equivalent behavior. Admit ONDCFlow `.cff` receipts for
their declared scope and preserve Inconclusive where independent observations
are missing. Exercise refusal of cross-profile reuse with EDIFlow. Cover AC-19,
AC-20, AC-28 and AC-29.

### 7. Qualify SaaS activation and independent client upgrades

Deploy one service composition across two tenant scopes with versioned
configuration. Record rollout readiness and activate the qualified scope.
Exercise one client update against the existing API and the candidate API
against current and still-supported older clients. Include a cached browser
build or returning offline client when the delivery profile supports it.

Observe a healthy release for the selected tenant and a failed readiness
check that withholds activation elsewhere. Block an API or data-contract
change that breaks a supported client. Reproduce the selected tenant's owned
setup and configuration in a sandbox. Cover AC-13, AC-25, AC-27 and the
composed-plan portion of AC-24.

### 8. Extend release trust and evidence reuse

Extend the local signed release path to additional issuers and evidence
sources. Check issuer authority and current policy at the destination.
Resolve authorized corrections before reuse while preserving the issued
statement and its historical decision. Admit a Workstream handoff under the
verified rollout and target approval contract. VERA recall can be added
independently as an evidence lookup.

Verify a valid statement, then exercise mismatched scope, an unauthorized
signer, a changed handoff and withdrawn evidence. Retain the original
records and block the dependent operation in each refusal case. Cover
AC-21–AC-23.

### 9. Add one managed mobile delivery path

Select one available test channel and an application with a runnable native
build. Bind the signed submission to the provider's app, build and channel
identities. Record the provider's review and availability state, then observe
the delivered install, upgrade and launch on a test device. Verify the build
linkage where the provider generates or signs the delivered package.

Exercise a lost submission response and reconcile it before retry. Confirm
one supported interruption or rollout recovery path and record the device
scope actually observed. Qualify additional Android and iOS channels
independently. Cover AC-26 and the mobile portions of AC-24 and AC-27.

### 10. Qualify remote providers and production operation

Add OCI distribution, VM and Kubernetes capabilities through individually
tested adapters. Qualify authenticated target access, remote MCP, external
trust and secret providers, and multi-user rollout policies on the supported
targets. Exercise durable workers, fencing, lost responses, resource
retention, restore and the required operational budgets.

Qualify staged upgrades of the control and execution components. Verify the
negotiated protocol for each required runner and record the migration stage
the platform reached on an interrupted upgrade. Exercise AC-36.

A production candidate needs measured recovery and operational evidence for
the advertised scope and the repository's documented promotion decision.
See the remaining milestones and qualifications in [ROADMAP.md](ROADMAP.md).

The first code change validates one local wallet fixture with saved inputs
and typed dependency outputs. The first interface checkpoint is the same
frozen plan from Dhall and JSON through the installed CLI and local MCP,
including a rejected input case. The first product milestone is a clean-host
reference distribution that completes the wallet and orders lifecycles and
exports their scoped signed release evidence.
