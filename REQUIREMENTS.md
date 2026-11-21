# Hallmark product requirements

Reviewed requirements draft, 2026-11-20. The requirements draw on the owner's
product direction, a review of CanonFlow consumer projects and 50 public
product and specification profiles.
[Counterpart selections](COMPETITORS.md) · [Source register](competitive-sources.json) · [Consumer requirements](#requirements-from-ecosystem-consumers).

Draft date: 2026-11-20. Intended readers: Hallmark product owners, implementers
and qualification reviewers. Product direction: one validated model spanning
Build, Install, Configure, Migrate, Verify and Release, with explicit operational
state, compatibility and recovery.

**Dhall authoring and one typed lifecycle model are the core of Hallmark.**
Teams use Hallmark's
Dhall schema to compose packages and declare dependencies, lifecycle stages,
transitions and gates. Hallmark evaluates that declaration into its versioned
workflow format. The lifecycle model validates the result and owns planning,
admission and recovery. Rust is the maintained engine direction under CF-067.
CLI and Model Context Protocol (MCP) clients use the same model. JSON remains
a supported authoring and interchange format.

The 2026-11-20 recording informs the product direction. Timestamped notes
identify supported intent where the transcript contains unclear wording.

## Product contract

Hallmark is the delivery application for teams that need to install an app,
upgrade its live state and prove what the release did. The installed CLI and
local MCP server expose the same lifecycle decisions. A developer authors
the workspace, an operator applies and recovers a plan, and a reviewer can
inspect the release evidence from a separate environment.

| Record | Role in the delivery workflow |
|---|---|
| Workspace and packages | Declare versioned components, input schemas, typed dependency outputs and delivery profiles in Dhall or the supported JSON format. |
| Resolution lock | Pin direct and transitive inputs, artifacts and provider choices for the selected profile. |
| Plan | Bind the lock, observed target, resolved input snapshot, operations and required gates. A review or configured automation policy selects the plan by identity. |
| Run and evidence | Record each attempted effect and its observations. Inspect or recover the run using its target and input identities. |
| Release | Identify the exact composition selected for delivery and its packaged runtime evidence. |
| Hallmark stamp | Attest to the release policy decision for that composition, plan, target and observation time. A later effect uses current target admission policy. |

### Reference distribution

The complete reference distribution targets a trusted .NET workload on the local
Linux/Compose profile. It uses the existing process, file and Compose
adapters and the pinned Flyway/PostgreSQL recipe. Qualify wallet first and
orders as the unchanged second consumer. The distribution identifies the
product SDK separately from each consumer's native toolchain.

| Included capability | What the first distribution must demonstrate |
|---|---|
| Install and start | The packaged CLI, local stdio MCP, versioned schemas, offline help and diagnostics, a starter workspace and a prerequisite manifest work together on a clean reference host. CF-036, CF-069–CF-070, CF-072, CF-086. |
| Resolve and review | The operator sees saved input changes, selected package bytes, target and planned effects. CLI and MCP agree on a frozen plan and explain stale or incompatible inputs. CF-001–CF-006, CF-020, CF-023, CF-049, CF-079, CF-082–CF-084. |
| Apply and recover | The selected plan uses the owned target, approved sources and scoped credentials. Exercise forward equivalence, safe backfill, restore, drift inspection and recovery after a lost response. CF-007–CF-011, CF-013, CF-029, CF-038–CF-040, CF-046–CF-047, CF-066, CF-071. |
| Verify and release | Required Assay and behavior evidence covers the exact .NET scopes. The selected package runs through its consumer entry point. Verify the resulting signed release policy decision with configured trust in a separate environment. CF-028, CF-053, CF-065, CF-067, CF-073–CF-075, CF-078. |

After applying, an operator can inspect what is installed and which checks
remain open. An interrupted run exposes the last recorded external state and
the supported recovery path. The release bundle carries the evidence needed
for its declared verification scope and identifies any required evidence
that the verifier still needs to resolve.

### Selected design choices

| Source | Decision for Hallmark |
|---|---|
| Dhall and Nix | Use typed declarations and a frozen dependency closure. Hallmark ties saved input evolution and stage-specific outputs to that contract. |
| OpenTofu, Flyway and Atlas | Apply a reviewed plan to its observed target. Keep the migration engine's ledger authoritative and exercise the upgrade on a populated fixture. |
| Temporal | Keep reconcile, retry and uncertain outcomes explicit in the local runner. Qualify distributed workers and their durable recovery when that target is added. |
| Assay, Tekton Chains and Sigstore | Bind each required producer's original result to the release decision. Ship explicit-key verification first and use the provider boundary for additional issuers and receipt sources. |
| Octopus Deploy and Argo Rollouts | Promote an identified composition and measure its activation scope. Apply the same checks to SaaS tenants and progressive rollout profiles as they are qualified. |
| Dagger and ORAS | Add qualified build and registry adapters through the existing contracts when their profiles need them. |

The source choices also inform the provider, evidence and profile backlog.
[Counterpart selections](COMPETITORS.md).

## Feature overview

| Area | High-level features | Requirements |
|---|---|---|
| Safe application evolution | Installed-state planning, current/transition state, fresh/upgrade equivalence, version bounds, expand/contract, backfill and downgrade checks | CF-001–CF-012 |
| Tenant reproduction | Owned setup data, runtime-created packages, captured installation/configuration, isolated replay and drift comparison | CF-013–CF-018 |
| Package consumption | Repository discovery, shared planner for CLI/MCP and optional TUI, published QA versions and branch snapshots | CF-019–CF-020, CF-029–CF-032 |
| Portable resource intent | Typed validation, native-script escape paths, shared developer/CI definitions, replicas, VM/Kubernetes profiles and resource composition | CF-021–CF-026 |
| Delivery trust | Existing Git/CI integration, signatures, approved stores, package/publish phases and immutable composition | CF-027–CF-031, CF-051–CF-055 |
| Variants and customization | Architecture/distro/build-feature matrices, runtime flags and versioned tenant overlays | CF-033–CF-035 |
| Day-two operations | Offline help, rolling updates, health gates, recovery, ownership, fencing and installed-state history | CF-036–CF-040 |
| Developer/role workflows | Multi-package workspaces, targeted overlays, affected-package plans, generated changelogs and role-specific evidence | CF-041–CF-045 |
| Evidence and recovery | History/schema checks, restore drills, reliable build caching, provider conformance, leased secrets and exact-scope verdicts | CF-046–CF-053 |
| Platform operations | Expiring previews, retention, telemetry, update proposals, reusable templates, runbooks, format upgrades, optional estimates/debugging, fault qualification and protected state | CF-056–CF-066 |
| Engine implementation boundary | Rust functional core, explicit effect adapters and a qualified F# reference | CF-067–CF-068 |
| CLI and MCP | Installed CLI for people and CI, local MCP for agents, shared plans and evidence, policy-bound effects | CF-069–CF-071 |
| Typed authoring | Typed Dhall declarations and reproducible imports feeding the validated lifecycle model | CF-072 |
| Workloads and gates | Native .NET, Python and Kotlin/JVM projects with scoped FsAssay, CsAssay, PythonAssay and KotlinAssay gates | CF-073–CF-074 |
| Consumer qualification | Runtime behavior of exact packages, bounded domain receipts, evidence corrections and signed release decisions | CF-075–CF-078 |
| Application delivery profiles | Server, SaaS, browser, standalone, mobile and SDK releases with target-specific evidence and compatible upgrades | CF-079–CF-081 |
| Reproducible upgrades | Evolution of saved inputs, typed dependency outputs, frozen workspace resolution and published migration lineage | CF-082–CF-085 |
| Reference distribution | Installable tools, a working starter workspace, prerequisite checks and release verification on a clean host | CF-086 |
| Release operations UI | Release and environment detail, attention queue, Kanban, downstream handoffs, run timeline, Gantt, Mermaid diagrams and operational metrics | CF-087–CF-094 |

## How to read the requirements

**R** identifies intent recovered from the recording. **B** identifies an
existing Hallmark obligation retained from the baseline. **I** identifies an
inferred product decision added to make the requirement executable or testable.
A requirement can have more than one origin. An inferred acceptance condition
does not become a verbatim speaker statement.

**C** identifies a feature selected from a named competitor/counterpart's public
documentation or source. Inline **Adopt** means implement an original Hallmark
contract; **Integrate** means use a separately selected provider; **Defer** means
retain the option without adding it to the first implementation slice. A source
link establishes the reviewed design reference, not an executed parity result.

**P0** identifies a release obligation for the named profile. **P1** identifies
broader product work. **P2** is a later extension. The active delivery
priorities select the scope and order for the current pilot and public MVP.
These priorities do not imply implementation or production readiness.
Each requirement has a distinct acceptance condition. The execution status of
new acceptance cases is pending until separately implemented and exercised.

The frozen baseline is commit `b14b48a6d3b03f69701c373777840e5bd09c4d3f`,
Hallmark `0.2.0-local.1`. Preserve [PLAN.md](PLAN.md),
[FEATURES.md](FEATURES.md), [ROADMAP.md](ROADMAP.md),
[QUALIFICATION.md](QUALIFICATION.md). Their existing results retain their
original scope. This document adds requirements; it does not revise historical
verdicts or announce new adapters.

## Vocabulary

| Term | Meaning in this document |
|---|---|
| Package | A versioned unit with declared inputs, dependencies, outputs and managed resources; not necessarily an OS package or container. |
| Current state | The managed state declared for a package version, used for fresh installation and comparison. |
| Transition | An identified operation/path from an observed supported version/state to another. |
| Managed-state equivalence | Equality of explicitly declared relevant observations after normalization; not byte equality of unrelated data, timestamps or secrets. |
| Forward check | A check comparing the declared current state with the result of supported transitions before publication. |
| Compatibility | A declared and tested relation between package, schema, configuration and application versions; a version number alone is insufficient. |
| Expand/contract | Introduce a compatible schema, move applications/data, then remove or tighten the old contract after its consumers have moved. |
| Convergence | Reconcile declared desired resources under their ownership and effect contracts. |
| Qualification | Executed acceptance evidence for a named scope, producer, inputs and target. |
| Gate | A policy requirement backed by a named producer and evidence for a declared scope. Each required gate must be satisfied before its dependent effect may begin. |
| Delivery profile | The artifact kind, target, distribution channel, supported operations and required evidence for delivering a component. |

## Installation, versions and compatibility

### CF-001 — Retain the common lifecycle model

**P0 · B.** Hallmark must retain one validated dependency model for Build,
Install, Configure, Migrate, Verify and Release. Stage dependencies must permit
schema preparation before an application deployment and schema contraction
after its compatibility gates. The Dhall authoring contract in CF-072 and the
JSON workflow format must both map to this validated lifecycle model.

**Acceptance:** one consumer graph expresses both sequences without an
untracked external orchestration script; dependency cycles and undeclared
outputs are rejected before effects. Preserve the baseline's separate desired
state, imperative work and observation semantics.

**Counterparts — Adopt:** One lifecycle intent with explicit provider/resource dependencies. [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-002 — Plan against installed state

**P0 · R/I · REC-01, REC-03.** The planner must distinguish an empty target,
an installed supported version and an unsupported or unknown target state.
It must produce the applicable installation/transition path before execution.
At admission, check the target observation that determines the path and
bind execution to the selected plan and effective policy. A changed
baseline requires a new applicable plan before dependent effects.

**Acceptance:** the same target-version request yields a fresh plan on an empty
fixture and an upgrade plan on an older fixture. An unsupported or unknown
baseline returns a diagnostic before mutation; a target label alone is not
accepted as observed installation evidence.
Change the migration baseline after planning and confirm that the reviewed
plan is refused before it applies the old transition.

**Counterparts — Adopt / Integrate:** Installed-state-aware planning and explicit brownfield import. [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/).

### CF-003 — Define both current state and transitions

**P0 · R/I · REC-04.** A package version must identify its managed current state
and its supported version transitions. Fresh installation must not require
replaying every obsolete migration solely to reconstruct that current state.

**Acceptance:** version 2 can create its five-column table directly on an empty
database and add the missing fifth column to a version-1 four-column database.
Both paths retain the package/transition identities and produce equivalent
declared schema observations while preserving pre-existing rows.

**Counterparts — Adopt:** A current-state declaration plus identified baseline/transition paths. [pgroll](https://github.com/xataio/pgroll/blob/448b29ba2215da4619d2eb8ee543957c6acfa95a/README.md).

### CF-004 — Gate publication on forward checks

**P0 · R/I · REC-04.** A publishable package must pass forward checks for every
predecessor declared in its supported transition matrix. Checks must compare
fresh target installation with upgrade-path results over declared observations.
Bind each comparison to the predecessor identity, selected artifact variant
and target analysis profile. When no eligible predecessor was checked, a
required upgrade check remains unresolved. A release that declares only fresh
installation needs evidence for that path. Record static analysis and executed
transition evidence with the scope each establishes.

**Acceptance:** removing an ALTER operation causes the upgraded schema to differ
from a fresh target schema and blocks publication. Skipping a required
predecessor case or using a stale package digest also blocks publication. The
receipt names paths, fixtures, observations and excluded comparisons; it does
not claim that every possible database state or future version was proved safe.

**Counterparts — Adopt:** Disposable-database verification, extended by Hallmark's fresh/upgrade equivalence gate. [Atlas](https://atlasgo.io/versioned/lint).

### CF-005 — Resolve package dependency ranges to exact identities

**P0 · R/B/I · REC-02.** Applications and database/schema packages must be
independently versionable. Dependencies must express supported version ranges,
then resolve to exact immutable package/content identities for execution.

**Acceptance:** a backend declaring schema `>=2.4.0 <3.0.0` accepts an eligible
2.x package and rejects 3.0 before mutation. The saved plan identifies the exact
selected version and bytes. Reusing the plan cannot silently select a newer
package published under the same range.

**Counterparts — Adopt / Integrate:** Dependency constraints resolved to pinned package/input identities. [Helm](https://helm.sh/docs/topics/charts/), [Nix](https://nix.dev/manual/nix/2.34/command-ref/new-cli/nix3-flake.html).

### CF-006 — Make versioning policy explicit

**P0 · R/I · REC-02.** Package owners must identify the versioning policy that
classifies compatible and breaking changes. The platform must record declared
classification, required compatibility evidence and unresolved changes.

**Acceptance:** a policy fixture distinguishes an additive nullable column from
an incompatible constraint/drop and rejects a release whose declared version
increment violates that policy. An unclassified raw SQL change requires an
explicit classification and evidence; it is not automatically treated as safe.
The policy is versioned. Semantic versioning is a default inference, not a
universal theorem about SQL compatibility.

**Counterparts — Adopt:** Explicit release classification and separate migration-risk evidence. [Changesets](https://github.com/changesets/changesets/blob/9a035ec36045aeb6141dd0bbac9ab9e3c69ac0bc/README.md), [Atlas](https://atlasgo.io/versioned/lint).

### CF-007 — Check the affected application/schema combinations

**P0 · R/I · REC-02.** Upgrade planning must account for applications still
running against a shared schema, including mixed application versions during
rollout and separately versioned schema packages.

**Acceptance:** a schema change that violates an installed dependent's declared
compatibility blocks the transition and names that dependent. A compatible
minor schema change and its unchanged application remain deployable. Declared
compatibility must be exercised with both old and new application versions in
the qualification fixture.

**Counterparts — Adopt / Integrate:** Compatibility across coexisting schema/application versions. [pgroll](https://github.com/xataio/pgroll/blob/448b29ba2215da4619d2eb8ee543957c6acfa95a/README.md).

### CF-008 — Express and enforce expand/contract sequencing

**P0 · R/I · REC-02.** A non-nullable-column transition must be expressible as
nullable addition, backfill, compatible application deployment and final
constraint enforcement, with explicit preconditions between phases.

**Acceptance:** an old application continues working during expansion; existing
rows are backfilled; a new application passes its behavioral probes; enforcing
the constraint is refused while NULL rows or incompatible live consumers remain.
After the gates pass, the final schema rejects NULL writes.

**Counterparts — Adopt; defer Liquibase runtime:** Expand/contract gates and state preconditions; exact provider license/version remains a separate selection. [pgroll](https://github.com/xataio/pgroll/blob/448b29ba2215da4619d2eb8ee543957c6acfa95a/README.md), [Liquibase](https://docs.liquibase.com/secure/user-guide-5-1/what-are-preconditions).

### CF-009 — Observe rollout completion before contraction

**P0 · R/I · REC-02.** A contraction gate must identify the relevant application
instances/consumers and verify their compatible versions before removing or
tightening the old schema contract.

**Acceptance:** an intentionally retained old replica prevents contraction.
Missing or unreachable required observations yield unknown/inconclusive and
prevent a success decision. The receipt records observation time and scope.
The first fixture may use two local instances; fleet support is a later target.

**Counterparts — Adopt / Integrate:** Observe remaining old consumers before schema completion and rollout progression. [pgroll](https://github.com/xataio/pgroll/blob/448b29ba2215da4619d2eb8ee543957c6acfa95a/README.md), [Argo Rollouts](https://argoproj.github.io/argo-rollouts/features/analysis/).

### CF-010 — Recover backfills without corrupting data

**P0 · I · REC-02.** Backfill work must declare batch/progress identity,
repetition semantics and completion checks. A failure must leave an identifiable
state from which recovery can be decided without blind repetition.

**Acceptance:** interrupt a backfill between batches, resume/reconcile it under
the same identities, and prove through data queries that no rows were omitted
or unintentionally updated twice. A changed backfill definition cannot reuse
the prior checkpoint. Batch sizes and time budgets are configuration, not fixed
product constants inferred from the recording.

**Counterparts — Adopt:** Progress-aware backfill and explicit idempotent effect boundaries. [pgroll](https://github.com/xataio/pgroll/blob/448b29ba2215da4619d2eb8ee543957c6acfa95a/README.md), [Temporal](https://docs.temporal.io/activity-definition).

### CF-011 — Distinguish application downgrade from database reversal

**P0 · R/B/I · REC-01, REC-02.** A request for an older application version must
check compatibility with the current schema/data before activation. Database
reversal, forward repair and backup restore must remain distinct operations.

**Acceptance:** a compatible application downgrade succeeds against the expanded
schema; an incompatible downgrade refuses with its failing precondition. A
channel-pointer change or application deployment cannot silently run destructive
reverse SQL. Data loss and unsupported reversals remain explicit outcomes.

**Counterparts — Adopt:** Separate revert/verify paths and the ability to identify prior releases. [Sqitch](https://sqitch.org/docs/manual/sqitch/), [Octopus Deploy](https://octopus.com/docs/releases).

### CF-012 — Provide an explicit baseline for existing resources

**P1 · I · REC-03.** Adopting an existing installation must bind its observed
schema/resources, target identity, version and ownership to a baseline before
Hallmark performs a transition.

**Acceptance:** an unowned existing table/service is not overwritten by a fresh
install request. A separately recorded adoption can establish the baseline;
subsequent drift from that baseline is reported before the upgrade proceeds.

**Counterparts — Integrate:** Explicit import of existing resources and retained provider resource identities. [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/).

## Tenant setup, runtime packages and mirrors

### CF-013 — Manage setup data separately from transactional data

**P0 · R/I · REC-05.** Packages must be able to declare versioned seed/setup
data and its supported insert/update/removal semantics. Ordinary business
transactions must remain outside desired-state reconciliation unless an owner
explicitly includes a particular record class.

**Acceptance:** an initial tenant receives seed records exactly once; a later
package updates an owned lookup record; repeating reconciliation creates no
duplicates and does not rewrite unrelated customer transactions. Conflict with
a manually changed owned record follows a declared conflict policy and appears
in the plan. This is not arbitrary database replication.

**Counterparts — Adopt:** Explicit setup-data operations; owned-record conflict policy is a Hallmark addition. [Sqitch](https://sqitch.org/docs/manual/sqitch/).

### CF-014 — Version runtime-created configuration packages

**P1 · R/I · REC-05.** Applications must be able to export runtime-created
configuration, such as an EDI specification or partner connection definition,
as an identified package with schema, tenant scope, dependencies and provenance.

**Acceptance:** create a partner definition after initial deployment, capture
version 1, modify it to version 2, and recreate version 1 in an isolated target
without changing the static application's release. The export excludes secret
values and rejects an invalid or unsupported configuration schema.

**Counterparts — Adopt:** Identified runtime exports with a schema-validated boundary. [CUE](https://cuelang.org/docs/introduction/).

### CF-015 — Capture a tenant installation manifest

**P1 · R/B/I · REC-05.** A capture must identify the observed application and
package versions, configuration, explicitly selected setup data, runtime
packages and their dependency closure for a named tenant/environment.

**Acceptance:** a fixture capture describes all four categories, contains
content identities and source scope, and names missing or inconsistent inputs.
A new capture records changed setup data without rewriting the previous one.
Cross-system consistency must be established by an adapter mechanism or marked
unavailable; independent reads cannot silently claim an atomic snapshot.

**Counterparts — Adopt / Integrate:** Scoped capture and identified restoration inputs; cross-system atomicity requires separate evidence. [Velero](https://velero.io/docs/v1.19/restore-reference/).

### CF-016 — Recreate a captured environment in a sandbox

**P1 · R/I · REC-05.** An environment mirror must consume a capture and an
explicit remapping document to reproduce selected tenant state in an isolated
target. The operator must be able to edit target-specific mappings before
planning and inspect the resulting changes.

**Acceptance:** recreate the captured application, configuration, setup records
and runtime-created partner definition; verify the same declared behavior with
sandbox endpoints. Missing captures or unsupported producers are reported.
Neither a file copy nor a release-bundle signature by itself establishes a
working recreated environment.

When replay selects a different supported architecture/distribution variant,
it must preserve the logical package/configuration selection and report the
artifact/platform differences. Such a replay can demonstrate selected behavior;
it cannot claim exact reproduction of an architecture-specific fault.

**Counterparts — Adopt / Integrate:** Filtered replay and explicit target/namespace remapping. [Velero](https://velero.io/docs/v1.19/restore-reference/).

### CF-017 — Isolate mirrors from production connections and secrets

**P1 · I · REC-05.** Mirror replay must require target-scoped credentials and
connection mapping. Captured connection definitions must separate endpoint
metadata from secret references. The sandbox policy must block unapproved
production destinations and apply the selected data-minimization rules.

**Acceptance:** replay fails before effects when a required partner endpoint
still points to production; a sandbox credential resolves successfully; source
secret values do not occur in the capture, plan or logs. A fixture containing
selected sensitive setup fields exercises the declared redaction policy. These
isolation rules are product inferences, not a claim that the speaker described
every control.

**Counterparts — Adopt / Integrate:** Separate target secret delivery from captured definitions and restore mapping. [OpenBao](https://openbao.org/docs/secrets/databases/), [Velero](https://velero.io/docs/v1.19/restore-reference/).

### CF-018 — Preserve capture ownership and explain differences

**P1 · R/B/I · REC-05.** A captured resource must retain stable identity,
ownership, source version and comparison rules. Users must be able to compare
a capture with a declared package or target and distinguish drift from an
unmanaged business-data change.

**Acceptance:** changing an owned partner definition is reported with its
resource identity; adding an unrelated invoice does not cause a desired-state
repair. Unknown resource classes cannot be treated as successfully captured.

**Counterparts — Adopt / Integrate:** Explain managed-resource changes and preserve explicit import/ownership boundaries. [Ansible](https://docs.ansible.com/projects/ansible/latest/playbooks_checkmode.html), [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/).

## Package discovery and operator interfaces

### CF-019 — Discover packages through repository adapters

**P1 · R/I · REC-06.** Users must be able to register a supported repository,
list/search its packages and versions, inspect dependencies and select a package
for planning. Repository credentials must be references resolved by the shell.

**Acceptance:** a local test repository exposes two versions and a dependent
package; the catalog selects the requested version and produces a resolved plan.
An unavailable repository, incompatible package or untrusted content returns
an actionable diagnostic. Nexus and Artifactory are candidate adapters from the
recording, not existing integrations or a requirement to build a registry.

**Counterparts — Integrate:** Use existing package and OCI repository mechanisms. [Helm](https://helm.sh/docs/topics/charts/), [ORAS](https://oras.land/docs/).

### CF-020 — Share one planner across CLI, MCP and TUI

**P0 for CLI and MCP. P1 for TUI. · R/B/I · REC-06 and owner follow-up.** The
CLI, MCP server and optional TUI must use the same package resolution,
validation, plan identity, admission and execution contracts. Interactive
selection must expose dependencies, intended changes, current versions and
progress. Automation must receive structured results and diagnostics.

**Acceptance criteria**

With identical inputs and observed target state, CLI and MCP return the same
plan digest, dependency diagnostics and admission decision. The CLI reports a
failed operation with a non-success exit and a structured result. An optional
TUI must meet the same parity check as part of its qualification.

**Counterparts — Adopt / Integrate:** Shared execution APIs behind interactive, CLI and task views. [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/), [Backstage](https://backstage.io/docs/features/software-templates/).

## Typed components and environment consistency

### CF-021 — Validate structured resource definitions before execution

**P1 · R/B/I · REC-07.** Supported typed resource definitions must validate
their fields, references, dependency outputs and provider versions before
execution. Database definitions must expose the column/resource identities
needed by their advertised validation scope.

**Acceptance:** an invalid supported column reference or mismatched output type
is rejected before database effects. The provider reports which guarantees it
can establish statically and which require runtime checks. Validation of a
structured definition does not establish the correctness of arbitrary SQL.

**Counterparts — Adopt / Integrate:** Validate configuration constraints and typed resource-output references. [CUE](https://cuelang.org/docs/introduction/), [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-022 — Retain explicit raw-provider escape paths

**P1 · R/B/I · REC-07.** Users must be able to package existing SQL scripts,
service definitions and external tools through declared adapter/process
boundaries without rewriting application source. Raw operations must declare
effects, inputs, ownership and recovery constraints.

Reusable plugins for builds, packaging, deployment, migrations, target
observation and release publishing must use the provider identity and
execution contract in CF-074. Each plugin declares the stages, typed inputs
and outputs, effects and recovery behavior it supports. Hallmark owns
planning, policy admission, recovery decisions and release signing. Qualify
each plugin on its declared target profile before advertising support.

**Acceptance:** an identified SQL-script recipe and a raw service definition can
participate in the same lifecycle graph; their unsupported static checks appear
as missing evidence. An opaque imperative script cannot acquire convergent or
transaction-safe semantics merely by being wrapped in a package. Planning
refuses an unsupported plugin stage or target profile before dispatch.

**Counterparts — Integrate:** Native scripts and resource modules with explicit unsupported-preview limits. [Sqitch](https://sqitch.org/docs/manual/sqitch/), [Ansible](https://docs.ansible.com/projects/ansible/latest/playbooks_checkmode.html).

### CF-023 — Reuse package definitions across developer, CI and target environments

**P0 · R/B/I · REC-08.** The same package source and dependency model must be
usable for a developer sandbox, CI qualification and a deployment target.
Environment-specific values must be explicit inputs rather than separate
untracked installation procedures.

**Acceptance:** from a clean checkout, one documented developer command resolves
and installs the consumer and its dependencies. CI and a second target use the
same identified package definitions with different declared configurations.
Receipts show platform/adapter differences; equal syntax is not a guarantee
of equal operating-system or production behavior.

**Counterparts — Adopt / Integrate:** Stable workload intent and declared environment-specific development connections. [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/), [DevSpace](https://www.devspace.sh/docs/configuration/dev/).

### CF-024 — Model placement and replicas explicitly

**P1 · R/I · REC-03, REC-09.** Packages must be able to describe component
placement, target selection and replica requirements through capable adapters.
Target topology must be an explicit input to planning.

**Acceptance:** a multi-component fixture plans its database and two application
instances on declared targets and reports unavailable placement capabilities
before effects. A later multi-node qualification must exercise cross-node
networking and recovery. Docker Compose support alone cannot claim distributed
multi-host orchestration or Kubernetes parity.

**Counterparts — Adopt / Integrate:** Compose resource/placement intent while exposing actual target capabilities. [Crossplane](https://docs.crossplane.io/latest/composition/compositions/).

### CF-025 — Support VM and Kubernetes target profiles through adapters

**P1 · R/B/I · REC-10.** The lifecycle model must be able to select both
VM/bare-host and Kubernetes target profiles without making Kubernetes a
prerequisite. Profiles must expose their actual capabilities and semantic
differences, including service management, networking, storage and ownership.

**Acceptance:** separately qualified VM/service-manager and Kubernetes adapters
deploy an identified application composition and execute the same business
probes. A profile that cannot satisfy an operation refuses before mutation.
Kubernetes-managed objects retain their controller's authority; a common model
does not authorize two controllers to race over one resource.

**Counterparts — Adopt / Integrate:** Keep VM delivery available and delegate Kubernetes reconciliation to a selected controller. [Argo CD](https://argo-cd.readthedocs.io/en/stable/user-guide/sync-waves/).

### CF-026 — Compose infrastructure and application resources

**P1 · R/I · REC-08, REC-10.** A composition must be able to link application,
database, proxy/load-balancer and supporting-service packages through declared
outputs rather than copied endpoint values and separate untracked runbooks.

**Acceptance:** a fixture wires a database and proxy to two application replicas,
checks references before effects and records ownership per resource. Provider
selection must distinguish implemented adapters from recipes and future targets.
PostgreSQL, systemd and nginx are reference candidates; Redis and search-service
integration are additional candidates, not newly delivered functionality.

**Counterparts — Adopt / Integrate:** Composed service/resource dependencies and versioned package configuration. [Crossplane](https://docs.crossplane.io/latest/composition/compositions/), [Helm](https://helm.sh/docs/topics/charts/).

## Source workflows, trust and audit

### CF-027 — Work with existing Git and CI practices

**P1 · R/B/I · REC-11.** Hallmark must accept identified package source from
existing CI systems and branching strategies without imposing Gitflow,
trunk-based development or a proprietary hosted pipeline. Published artifacts
must remain bound to exact source and build identities.

**Acceptance:** a branch build and a tagged build can publish distinct immutable
test versions through the same package contract. A later branch movement does
not change an existing release or authorize checkpoint reuse. Offline bundles
retain their resolved identities without depending on a live Git server.

**Counterparts — Integrate:** Reuse existing CI and source workflows with explicit affected-task inputs. [GitLab](https://docs.gitlab.com/ci/review_apps/), [Nx](https://nx.dev/docs/features/ci-features/affected).

### CF-028 — Verify publisher identity and retain an audit trail

**P0 · R/B/I · REC-11.** Published packages/releases must bind content, producer
and signature to an explicit verifier trust policy. Installation and promotion
records must identify the selected package, target, policy and actual outcome.

**Acceptance:** replacing package bytes, supplying the wrong verification key
or omitting required provenance prevents admission. An audit query can connect
source/build, publication and installed version. A valid signature establishes
the claimed signer under that policy; it does not prove correctness or that a
publisher account was uncompromised. Existing local ECDSA support remains a
baseline capability with its documented external-trust limitations.

**Counterparts — Integrate:** Artifact/identity-bound signature verification and signed execution attestations. [Sigstore Cosign](https://docs.sigstore.dev/cosign/verifying/verify/), [Tekton Chains](https://tekton.dev/docs/chains/).

### CF-029 — Restrict target package sources by policy

**P0 · R/I · REC-12.** A deployment target must be able to admit packages only
from its configured stores/publishers and resolved content identities. Operator
selection and dependency resolution must apply the same source policy.

**Acceptance:** a production-profile fixture accepts a trusted test-store
package and rejects an otherwise valid package from an unapproved store,
including a transitive dependency fetched from that store. Changing the source
policy changes plan/admission identity. The fixture is not a production rollout.

**Counterparts — Adopt / Integrate:** Enforce allowed sources and tenant-scoped references at the actual execution boundary. [Open Policy Agent](https://www.openpolicyagent.org/docs), [Flux](https://fluxcd.io/flux/installation/configuration/multitenancy/).

### CF-030 — Offer a composable build, package and publish flow

**P1 · R/B/I · REC-12.** The platform must expose independently observable
build, distribution/package creation and publication phases and a composition
that runs them in dependency order. Existing CI may invoke that composition
without reproducing the dependency graph in a second pipeline language.

**Acceptance:** a parent with two build dependencies is built in valid order,
packaged with exact output identities and published once. A failed required
build/test/forward check prevents publication. A partial publication outcome is
retained for reconciliation; a retry cannot silently replace immutable content.
A successful build is not automatically a qualified release.

**Counterparts — Integrate:** Compose identified builds and consume retained run/artifact attestations. [Dagger](https://docs.dagger.io/reference/glossary/), [Tekton Chains](https://tekton.dev/docs/chains/).

### CF-031 — Provide branch/topic channels with immutable snapshots

**P1 · R/I · REC-12.** Teams must be able to map their existing feature,
bug-fix or other branch conventions to isolated package channels/topic lanes.
A moving channel must resolve to an immutable snapshot when planned or consumed.

**Acceptance:** two branches with the same logical component publish distinct
snapshots without overwriting each other or the stable channel. A frozen plan
continues to consume its original snapshot after a channel advances. Promotion
records who/what selected the same identified artifact instead of rebuilding
an artifact under its old name. Exact lane naming is configuration.

**Counterparts — Adopt:** Branch-specific consumption without replacing immutable package snapshots. [GitLab](https://docs.gitlab.com/ci/review_apps/).

### CF-032 — Let QA install and identify a published version

**P1 · R/I · REC-13.** A tester must be able to select an immutable published
application composition and install it in a sandbox using declared runtime
prerequisites. The package-consumer path must not require the application's
source checkout or a fresh application build.

**Acceptance:** a clean tester environment installs a chosen snapshot from the
test store, runs its probes and exports a redacted bug-context record containing
release/package digests, target profile, configuration identity and relevant
capture references. A developer can resolve those same identities for
reproduction after the development channel has advanced. Source-build workflows
remain available separately.

**Counterparts — Adopt:** Version-selected review/testing environments and repeatable release consumption. [GitLab](https://docs.gitlab.com/ci/review_apps/), [Octopus Deploy](https://octopus.com/docs/releases).

## Platform variants and tenant configuration

### CF-033 — Resolve and validate a platform/variant matrix

**P1 · R/I · REC-14.** Packages must declare supported architecture,
operating-system/distribution, target-profile and build-feature dimensions.
Resolution must select a compatible variant using observed target facts and
bind that selection into the build/package/plan identities.
Bind target-dependent install analysis and its evidence to the effective
target and provider profile, which can differ for identical artifact bytes.

**Acceptance:** a fixture matrix distinguishes x86-64/ARM64 and two distribution
profiles, generates identified variants and refuses an incompatible artifact
before installation. An unregistered or unobserved target is not accepted solely
because the operator supplied a matching string. Cross-compilation and emulation
must be explicit builder capabilities, not evidence that a binary ran natively.
Exercise two target plans that share an artifact and require different actions.
An analysis record for one target fails admission on the other.

**Counterparts — Adopt; defer image builder:** Explicit target/action identity; optionally build identified VM images with a separately selected provider. [Bazel](https://bazel.build/remote/caching), [Packer](https://developer.hashicorp.com/packer/docs/intro).

### CF-034 — Distinguish build features from runtime feature configuration

**P1 · R/I · REC-14, REC-15.** A feature dimension must declare whether it changes
build output, package dependencies, installation or runtime configuration. The
platform must reject combinations that violate declared compatibility rules.

**Acceptance:** a compile-time feature changes artifact identity; a runtime flag
changes configuration identity and triggers its declared reconciliation/rollout
behavior. Mutually incompatible flags fail validation. The qualification matrix
identifies tested combinations and retains untested combinations as missing
evidence instead of claiming exhaustive coverage.

**Counterparts — Adopt / Integrate:** Environment-scoped runtime feature evaluation, distinct from build-feature identity. [Unleash](https://docs.getunleash.io/concepts/environments), [OpenFeature](https://openfeature.dev/docs/reference/concepts/provider/).

### CF-035 — Version tenant customization and white-label configuration

**P1 · R/I · REC-15.** A tenant must be able to select a validated, versioned
configuration/branding overlay over shared package definitions. Its storage
location may be a supported file or database adapter, with explicit provenance
and secret references.

**Acceptance:** two tenant fixtures use the same application bytes with distinct
configuration identities; changing one overlay leaves the other tenant's state
unchanged. Unknown keys, incompatible overlay versions and unauthorized
cross-tenant references fail before application. Capture/mirror replay retains
the selected overlay version while applying sandbox-specific connection mapping.

**Counterparts — Adopt / Integrate:** Reusable overlays and separately resolved tenant values. [Kustomize](https://kubernetes.io/docs/tasks/manage-kubernetes-objects/kustomization/), [Octopus Deploy](https://octopus.com/docs/releases).

## Qualification and day-two operations

### CF-036 — Ship discoverable, version-matched help and examples

**P0 for the reference distribution. P1 for added profiles. · B/I.** The
installed product must expose commands, schemas, adapter capabilities,
diagnostics and limitations in version-matched help. Examples must use
a named supported product and provider version and distinguish a rendered
plan from admitted and executed effects.

**Acceptance:** in an offline fixture, inspect help and capabilities and follow
the packaged fresh-install and upgrade examples. An added capture profile
includes its qualified mirror example. Unsupported operations report their
capability gap. Execute the examples from the installed product.

**Counterparts — Adopt:** Discoverable help and a published schema/reference matched to supported implementations. [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-037 — Qualify rolling updates, health gates and abort behavior

**P1 · R/B/I · REC-17.** Replica rollout must declare ordering, readiness/health
checks, allowed disruption, compatibility gates and abort/recovery behavior.
An unhealthy or unobserved instance must not count as successfully updated.

**Acceptance:** update a two-instance fixture with one intentionally unhealthy
new instance; halt progression and preserve the prior compatible instance.
Exercise a successful rolling update and a failed update with explicit recovery
observations. Application recovery must respect CF-011 and CF-009; it must not
promise transactional rollback of databases, files and remote services together.
An availability claim requires observations against a stated test envelope.

**Counterparts — Adopt / Integrate:** Measured progressive deployment and explicit failure/inconclusive gates. [Argo Rollouts](https://argoproj.github.io/argo-rollouts/features/analysis/), [Harness](https://developer.harness.io/continuous-delivery/use-continuous-delivery/verify-deployments/verify-deployments-with-the-verify-step.md), [Spinnaker](https://spinnaker.io/docs/concepts/).

### CF-038 — Retain honest recovery and replay boundaries

**P0 · R/B/I · REC-17.** Failed, interrupted and partially completed runs must
retain stage identities, observed outputs and effect/repetition semantics.
Resume must refuse changed inputs and ambiguous imperative retries until the
adapter can reconcile the external outcome or obtain the required explicit
recovery input.

**Acceptance:** inject failure before and after a migration's external commit,
lose a response and restart the runner. Completed work is reused only under
unchanged identities; an uncertain commit is not blindly repeated or reported
as cancelled. Cancellation, timeouts and a stopped service produce their actual
status. The original failure receipt remains available after recovery.

**Counterparts — Adopt:** Version-compatible replay and honest state/concurrency boundaries. [Temporal](https://docs.temporal.io/activity-definition), [OpenTofu](https://opentofu.org/docs/language/state/locking/).

### CF-039 — Preserve ownership and concurrency boundaries

**P0 for local exclusion; P1 for distributed execution · B/I.** Each managed
resource must have an explicit owner and target identity. Execution must reject
competing owners/writers. Distributed workers require enforceable lease/fencing
contracts before claiming exclusive target control.

**Acceptance:** two local contenders cannot mutate the same owned resource
concurrently. A distributed qualification must expire a lease, admit a new
worker and demonstrate that the stale worker cannot commit further effects.
A local file lock alone does not satisfy that distributed case. Teardown must
not delete an unowned resource or discard required data/evidence implicitly.

**Counterparts — Adopt / Integrate:** Cooperating state locks and scoped execution identities; target fencing is an additional Hallmark obligation. [OpenTofu](https://opentofu.org/docs/language/state/locking/), [Flux](https://fluxcd.io/flux/installation/configuration/multitenancy/).

### CF-040 — Explain installed state and operational history

**P0 for local inventory and history. P1 for fleet and tenant views. · R/B/I ·
REC-17.** Operators must be able to inspect the actual package, configuration,
schema and target of the reference application. Link those observations to
the plan, run, drift and recovery records. Fleet views include the observed
rollout state for each selected scope.

**Acceptance:** after a partial rollout, the inventory names both installed
application versions and the last observed schema/configuration identity.
Deliberate drift appears as drift; unreachable targets appear as unknown.
Success of a planner, CLI command or signature check cannot replace runtime
health or application-behavior evidence.
For the local profile, inspect the installed state after recoverable failure
and follow its recorded run and recovery result from either interface.

**Counterparts — Adopt:** Link version/environment inventory to the actual deployment record. [Spinnaker](https://spinnaker.io/docs/concepts/), [GitLab](https://docs.gitlab.com/ci/review_apps/).

## Developer workspaces and change sets

### CF-041 — Represent components and packages independently

**P1 · R/I · REC-19.** A workspace must support multiple components and multiple
packages per component. The dependency graph must use stable package identities
rather than assuming one repository, project or component equals one package.

**Acceptance:** a monorepo fixture contains three components, one of which owns
separate application, schema and configuration packages. Workspace discovery
reports their dependency edges and can select a component without installing
unrelated roots. Repository layout and branching strategy remain team choices.

**Counterparts — Adopt:** Separate workspace/component layout from package dependency structure. [Nx](https://nx.dev/docs/features/ci-features/affected), [Helm](https://helm.sh/docs/topics/charts/).

### CF-042 — Support a package-based targeted development loop

**P1 · R/I · REC-19, REC-20.** Developers must be able to install a known
published system baseline, then build and replace selected changed components
in their sandbox while retaining identified published dependencies.

**Acceptance:** install the full fixture from published packages without building
application source, change one component and build/install its affected package
set. Record unchanged dependency digests, changed source/outputs and executed
compatibility/behavior checks. Removing a local override restores the selected
published baseline. Development-only source/mount overrides must be explicit
and cannot leak into a production release unnoticed.

**Counterparts — Adopt / Integrate:** Selective development overrides, file synchronization and explicit rebuild fallback. [Tilt](https://docs.tilt.dev/live_update_reference.html), [DevSpace](https://www.devspace.sh/docs/configuration/dev/).

### CF-043 — Explain the affected package and version set

**P1 · R/I · REC-20.** A change-set view must identify changed packages,
affected dependents and required version/compatibility checks using source,
declared-input and dependency identities. Git diff may contribute evidence but
must not be the sole basis for claiming safe reuse.

**Acceptance:** an isolated component edit excludes unrelated packages from the
requested change set, while a shared contract change includes its affected
consumers. A changed toolchain/configuration/input invalidates incompatible
reuse. The report explains every included package and unresolved dependency.
Prebuilt dependency reuse is distinct from a qualified hermetic build cache.

**Counterparts — Adopt:** Affected work follows declared inputs/outputs; action reuse has its own correctness contract. [Nx](https://nx.dev/docs/features/ci-features/affected), [Bazel](https://bazel.build/remote/caching).

### CF-044 — Generate release notes from explicit change sets

**P1 · R/I · REC-21.** A package change set must record affected packages,
intended version/compatibility classification, migration implications and a
human-readable summary. A release cut must collect the admitted change sets
into a version-bound changelog rather than relying on a manually updated wiki.

**Acceptance:** two independent package change sets produce the expected version
plan and release notes, including an identified breaking change. Re-running a
release cut does not duplicate entries. Missing classifications block the
affected release until resolved; automated suggestions do not silently assert
that a SQL or API change is compatible.

**Counterparts — Adopt:** Contributor-authored change sets drive dependent version plans and generated release notes. [Changesets](https://github.com/changesets/changesets/blob/9a035ec36045aeb6141dd0bbac9ab9e3c69ac0bc/README.md).

### CF-045 — Explain the lifecycle to each participating role

**P1 · R/I · REC-18, REC-21.** The same identified delivery record must support
a developer's change/dependency view, QA's install/reproduction view, an
operator's rollout/recovery view and a product/business review of delivered
changes and observed acceptance results.

**Acceptance:** one fixture release exposes all four views with consistent
version and evidence identities. A product owner can identify what changed and
which business cases passed without reading infrastructure scripts. Unsupported
or failed checks remain visible in every summary. This does not require a new
portal before the CLI/structured-report workflow is usable.

**Counterparts — Adopt / Integrate:** Consistent task/result links and stakeholder review environments. [Backstage](https://backstage.io/docs/features/software-templates/), [GitLab](https://docs.gitlab.com/ci/review_apps/).

## Additional requirements selected from counterparts

These extend the recording-derived backlog. They are proposed Hallmark
requirements, with new execution evidence still pending.

### CF-046 — Validate migration history and schema drift independently

**P0 · B/C/I.** A migration adapter must check applied-history identity and live
schema drift as distinct observations. The selected engine owns its migration
ledger; Hallmark must not rewrite that ledger to make a check pass.

**Acceptance:** an edited applied migration fails history validation; an
out-of-band column change is detected by schema inspection even when the
migration ledger is unchanged. Missing checksums for an unsupported migration
type remain missing evidence. A repair is a separately identified operation
with before/after receipts, not an automatic side effect of validation.

**Counterparts — Adopt / Integrate:** Keep applied-history validation, schema inspection and post-deploy verification separately identified. [Flyway](https://documentation.red-gate.com/flyway/reference/commands/validate), [Atlas](https://atlasgo.io/versioned/lint), [Sqitch](https://sqitch.org/docs/manual/sqitch/).

### CF-047 — Require exercised recovery for destructive data changes

**P0 for destructive migration admission · B/C/I.** A destructive migration
policy must identify a backup/restore or forward-repair strategy and the scope
of its tested recovery evidence before admission.

**Acceptance:** restore a populated fixture to an isolated target and verify
schema, selected business invariants and expected data; retain backup identity,
keys/references, elapsed recovery time and the accepted data-loss boundary.
An incomplete backup/restore cannot satisfy a complete-recovery gate. RPO/RTO
targets must be configured and measured; a successful snapshot command is not
a restore drill. Non-destructive local changes need no invented backup ceremony.

**Counterparts — Adopt / Integrate:** Qualify actual restoration and identify destructive-change/backup preconditions. [Velero](https://velero.io/docs/v1.19/restore-reference/), [Bytebase](https://docs.bytebase.com/sql-review/review-rules).

### CF-048 — Establish cache correctness before advertising reusable builds

**P1 · B/C/I.** A cache-capable builder must bind its action result to source,
declared inputs, toolchain, platform, environment policy and dependency outputs.
Output-byte storage and a reusable action result must remain different concepts.

**Acceptance:** an identical qualified action can reuse verified output bytes;
changing any relevant input invalidates reuse. An undeclared network read or
ambient input is either prohibited or makes the action non-hermetic/non-cacheable
under policy. Deployments, migrations and live observations cannot inherit
build-cache semantics. A provider incapable of these guarantees stays an
identified local-process recipe.

**Counterparts — Adopt / Integrate:** Input-bound action reuse and locked dependency closure; never infer cacheability for effects. [Bazel](https://bazel.build/remote/caching), [Dagger](https://docs.dagger.io/reference/glossary/), [Nix](https://nix.dev/manual/nix/2.34/command-ref/new-cli/nix3-flake.html).

### CF-049 — Define a versioned provider capability and conformance contract

**P0 for providers in the reference distribution. P1 for added adapters. ·
B/C/I.** Each provider must identify its executable or API version, input
and output schemas, privileges, preview support, effect classes, ownership,
cancellation and recovery capabilities before plan admission.

**Acceptance:** capability negotiation rejects an unsupported operation/version
before effects. Provider conformance fixtures cover unknown fields, changed
outputs, missing preview, timeout and lost responses. A provider upgrade cannot
silently reuse records produced under an incompatible implementation. An
existing external engine retains authority over its own state and controller.
Exercise a declared operation whose typed output differs between validation
and execution. Its conformance check fails with the mismatched contract.

**Counterparts — Adopt / Integrate:** Versioned automation APIs, workload schemas and validated configuration boundaries. [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/), [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/), [CUE](https://cuelang.org/docs/introduction/).

### CF-050 — Support leased external secrets and rotation-aware delivery

**P1 · B/C/I.** Secret providers must expose reference/version or lease identity,
permitted delivery paths, renewal/revocation behavior and rotation-triggered
reconciliation. A secret value must not become a plain plan/cache/log field.
Keep typed references and version identities in authoring, arguments and
records. Resolve material in the execution adapter into its declared consumer
environment, credential file, protected rendering or parameter binding as
supported by the provider. Record the actual re-render, reload or restart
required by that path before reporting the rotation effective.

**Acceptance:** obtain a test database credential through a provider, renew it,
rotate/revoke it and observe the configured consumer update/recovery behavior.
An expired or unrenewable lease stops dependent work according to policy.
Tests check plans, captures, process arguments and output, and artifact
metadata for leaked test secrets. Retain the local vault as a separate
provider with its narrower key-management contract.

**Counterparts — Integrate:** Leased credential providers and reference-only secret delivery. [OpenBao](https://openbao.org/docs/secrets/databases/), [Dagger](https://docs.dagger.io/reference/glossary/).

### CF-051 — Transport complete identified releases through existing registries

**P1 · B/C/I.** Artifact distribution must support a selected OCI/generic-store
adapter and an offline export containing the complete declared install closure,
attestations and required verification material.

**Acceptance:** transfer a release to an isolated fixture, disable registry
network access and verify/install all included declared packages. A missing
transitive artifact, changed digest or untrusted signer blocks admission. An
offline install claim does not imply an offline source build unless toolchains
and build inputs are separately included and exercised. Repository mirroring,
environment capture and traffic mirroring remain different operations.

**Counterparts — Integrate:** Generic OCI artifact distribution and portable verification material. [ORAS](https://oras.land/docs/), [Sigstore Cosign](https://docs.sigstore.dev/cosign/verifying/verify/).

### CF-052 — Attach artifact-bound provenance, SBOM and security evidence

**P1 · B/C/I.** A release must be able to reference standard provenance and an
SBOM, together with required vulnerability/license checks identified by exact
artifact, producer, policy, coverage and advisory-database time/version.

**Acceptance:** substitute a different artifact or stale required scan and the
admission decision refuses reuse. Unknown component/license/coverage remains
visible. A critical fixture finding blocks under the configured policy; a scoped
exception records owner, reason and expiry. Do not claim complete detection,
SLSA certification or security from a signature or empty findings alone.

**Counterparts — Integrate:** Artifact inventory, scoped scan results and standard producer/source provenance. [Syft](https://github.com/anchore/syft), [Trivy](https://trivy.dev/docs/latest/scanner/vulnerability/), [SLSA](https://slsa.dev/spec/v1.2/provenance), [Tekton Chains](https://tekton.dev/docs/chains/).

### CF-053 — Bind evidence admission to exact scope and real verdicts

**P0 · B/C/I.** Admission must identify required evidence, producer, source,
configuration, policy and target scope. It must preserve Pass, Fail,
Inconclusive and ToolFailure as distinct outcomes and treat unsupported or
unavailable required evidence as unresolved.

**Acceptance criteria**

The gate reports each required evidence item and its scope. A stale or foreign
receipt or unsupported test producer leaves the obligation unsatisfied. Zero
admitted rules or zero findings alone supply no evidence of behavior, purity or
release readiness. For F# changes, retain an executed FsAssay check on the
actual changed scope with the exact command, producer, configuration, source
identities, exit and raw receipt. Retain the independent build and behavior
results. CF-074 applies this contract to each registered producer.

**Counterparts — Adopt / Integrate:** Keep evaluator output, attested producer results and enforcement distinct. [Open Policy Agent](https://www.openpolicyagent.org/docs), [Tekton Chains](https://tekton.dev/docs/chains/).

### CF-054 — Apply target-scoped authorization and concrete decision gates

**P1 · B/C/I.** Multi-user execution must authenticate the caller and enforce
tenant/environment/action permissions at execution. Where a target policy
requires a human decision, the decision must bind the actual plan, artifacts,
resolved configuration, target, policy and validity period.

**Acceptance:** cross-tenant deployment is denied; changing an approved plan
invalidates its decision; an expired/revoked decision cannot authorize execution.
Non-production automation proceeds under its configured authority without an
unconditional manual approval step.

When importing a Workstream Handoff Pack, bind its digest and the verified
ledger checkpoint to the decision for the exact rollout. Enforce the target's
authentication and approval policy at execution. An altered pack, a changed
release or a decision from an actor without the required target authority must
leave the rollout blocked. A matching decision from an authorized approver can
satisfy the gate. [Workstream Handoff Pack](https://github.com/CanonFlow-Engineering/workstream/blob/75c66140e8ea461a0331c7cb3933bb208e96e0c4/docs/decision-audit-handoff.md).

**Counterparts — Adopt / Integrate:** Protected-environment decisions and tenant-specific execution identities. [GitLab](https://docs.gitlab.com/ci/review_apps/), [Flux](https://fluxcd.io/flux/installation/configuration/multitenancy/), [GitLab deployment approvals](https://docs.gitlab.com/ci/environments/deployment_approvals/).

### CF-055 — Promote the same composition through environments and tenant rings

**P1 · B/C/I.** Promotion must select an immutable application/package
composition and separately identify environment/tenant configuration and secrets.
Rollout rings must select explicit tenants/targets and progression criteria.
For SaaS releases with tenant-specific activation, record service deployment
and tenant activation as distinct observations. Verify the tenant's required
configuration and data state before activation.

**Acceptance:** move identical artifact digests through development, test and
two tenant rings; record different resolved configuration identities without
rebuilding the application. An unhealthy canary tenant prevents the next ring.
Changing mutable tenant values triggers a new identified configuration decision
even when the application release version is unchanged.
On a shared SaaS deployment, observe the new behavior in one tenant while
another tenant stays on the previous behavior. A failed readiness check
withholds activation for the affected scope.

**Counterparts — Adopt / Integrate:** Release composition and tenant rings with measured progression. [Octopus Deploy](https://octopus.com/docs/releases), [Argo Rollouts](https://argoproj.github.io/argo-rollouts/features/analysis/), [Octopus tenants](https://octopus.com/docs/tenants).

### CF-056 — Give preview environments owners, expiry and resource limits

**P1 · C/I.** Branch/QA previews must have an owner, isolated resource namespace,
release/configuration identity, expiry/renewal policy and declared resource
quotas. Cleanup must respect shared and persistent data ownership.

**Acceptance:** create two branch previews, close/expire one, and demonstrate
owned-resource cleanup without touching the other or a shared database. A
failed cleanup remains visible and retryable; it cannot be reported as reclaimed
capacity. Production endpoint mapping follows CF-017.

**Counterparts — Adopt / Integrate:** Temporary review environments with automatic-stop lifecycle metadata. [GitLab](https://docs.gitlab.com/ci/review_apps/).

### CF-057 — Retain artifacts and evidence by recovery reachability

**P1 · B/C/I.** Retention/garbage collection must protect artifacts and receipts
reachable from active releases, supported recovery points, installed versions
and configured retention holds. Users must be able to preview reclamation.

**Acceptance:** a dry run excludes an old artifact still needed by an installed
tenant or rollback point; an unreferenced expired fixture can be reclaimed.
Concurrent release creation cannot race a collector into deleting newly pinned
content. A pruned supplemental log does not silently invalidate a required
attestation or permit reuse of missing evidence.

**Counterparts — Adopt / Integrate:** Previewable retention; add Hallmark's active-release and recovery reachability protection. [Harbor](https://goharbor.io/docs/edge/working-with-projects/working-with-images/create-tag-retention-rules/).

### CF-058 — Export correlated telemetry and gate on fresh observations

**P1 · B/C/I.** Execution must expose structured stage/run events and optional
OpenTelemetry traces, metrics and logs with bounded labels and redaction.
Deployment gates must state observation windows, freshness and missing-data rules.

**Acceptance:** correlate a failed rollout across plan, stage, adapter and target
without exposing test secrets; export latency/error observations from a fixture.
No telemetry samples, NaN or stale health results cannot become a successful
gate by default. Operational SLO observations and immutable execution/evidence
records remain separately identifiable.

**Counterparts — Integrate:** Correlated operational signals and explicit deployment observation criteria. [OpenTelemetry](https://opentelemetry.io/docs/concepts/signals/), [Harness](https://developer.harness.io/continuous-delivery/use-continuous-delivery/verify-deployments/verify-deployments-with-the-verify-step.md).

### CF-059 — Propose dependency upgrades without silently promoting them

**P2 · C/I.** Optional update automation may propose new provider, toolchain,
base-image or package versions, group changes and refresh locks. It must retain
the prior pins and route the candidate through its actual required checks.

**Acceptance:** a fixture update changes the intended lock identity and produces
its compatibility/test plan; a failing required check leaves the admitted
release unchanged. Creating a proposal does not merge a branch, change a
production channel or execute a deployment without the configured authority.

**Counterparts — Integrate:** Update proposals remain conditional on current checks and configured authority. [Renovate](https://docs.renovatebot.com/key-concepts/automerge/).

### CF-060 — Provide reusable templates and an optional self-service catalog

**P2 · C/I.** Platform owners must be able to publish versioned application and
environment templates with validated inputs, owners and supported target profiles.
An optional portal must invoke the same planner and evidence APIs as the CLI.

**Acceptance:** a new consumer is instantiated from a pinned template, produces
a reviewable plan and exposes links to its actual results. An unsupported
template/provider combination refuses with the relevant capability. No mandatory
portal or application-language migration is introduced by template selection.

**Counterparts — Adopt / Integrate:** Validated reusable templates and portable workload intent. [Backstage](https://backstage.io/docs/features/software-templates/), [Score](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-061 — Reuse lifecycle contracts for bounded operational runbooks

**P1 · B/C/I.** Maintenance actions such as configuration rotation, backup,
drift repair and service recovery must use named inputs, owned targets, budgets,
effect classes and retained outcomes under the same operational contracts.

**Acceptance:** rerunning an observation is safe; retrying a non-repeatable
maintenance action with an uncertain result is refused until reconciled. A
backup and a service-restart fixture retain their real exits, timeouts and
target identities. Runbooks cannot obtain broader privileges than their caller
and provider policy permit.

**Counterparts — Adopt / Integrate:** Managed maintenance/runbook workflows use explicit resources and execution contracts. [Octopus Deploy](https://octopus.com/docs/releases), [Ansible](https://docs.ansible.com/projects/ansible/latest/playbooks_checkmode.html), [Octopus runbooks](https://octopus.com/docs/runbooks).

### CF-062 — Upgrade Hallmark state and protocols compatibly

**P1 · B/C/I.** Hallmark must version manifest, provider, checkpoint and event
formats and declare compatible reader/writer and recovery paths. Upgrading the
runner must not reinterpret an old effect record under incompatible semantics.

**Acceptance:** a supported old fixture loads through an identified migration
and preserves old receipts; an unsupported newer format refuses without
overwriting it. Worker/provider version changes are checked before resume.
An interrupted state-format migration is recoverable from retained originals.
From a mixed-version deployment, observe which runners speak a supported
protocol before dispatching work. Retain an authorized inspection and upgrade
path under the supported compatibility policy. Verify each required runner
in the upgrade scope before reporting completion. Distinguish refusal before
migration from failure after a committed state change.

**Counterparts — Adopt:** Version-compatible workflow/replay evolution applied to Hallmark's own durable formats. [Temporal](https://docs.temporal.io/activity-definition), [Temporal workflow versioning](https://docs.temporal.io/workflow-definition).

### CF-063 — Report optional cost and capacity estimates honestly

**P2 · C/I.** Cloud-capable profiles may attach resource/cost estimates and
budget policy decisions to the resolved plan using an identified estimator,
price snapshot and explicit usage assumptions.

**Acceptance:** a fixture plan exceeding its configured estimate budget is
flagged or blocked according to that policy; missing pricing is unknown.
Changing placement updates the estimate. Forecasts are not actual invoices,
and cost-estimation support is not required for the local upgrade slice.

**Counterparts — Defer / Integrate:** Attach plan-time estimates and assumptions when a real cloud consumer needs them. [Infracost](https://www.infracost.io/docs/).

### CF-064 — Keep remote traffic debugging explicit and separate from capture

**P2 · C/I.** Optional local-to-cluster debug integration must identify the
selected traffic/resource scope, target identity, permissions, write behavior
and cleanup path. It must be disabled unless explicitly selected for that run.

**Acceptance:** a test intercept/mirror affects only selected sandbox requests,
uses sandbox credentials and is removed at session end or cancellation. Traffic
interception does not count as an environment/database snapshot, and live
production traffic cannot be selected through an implicit default.

**Counterparts — Defer / Integrate:** Opt-in local/remote traffic debugging, distinct from environment-state capture. [Telepresence](https://telepresence.io/), [mirrord](https://github.com/metalbear-co/mirrord/blob/c8ded0d15e27ac33c47838f7ff9d5b234e749bbe/README.md).

### CF-065 — Qualify realistic histories and retain comparison limits

**P0 · B/C/I.** Qualification must cover retained application behavior, fresh
installation, supported upgrades, repeated reconciliation, drift, faults and
recovery over identified sources and targets. Runtime comparison with another
tool must record equivalent inputs and material adaptations.

**Acceptance:** exercise the scenario matrix below on at least two unchanged
consumer applications, retain failing and unavailable outcomes and explain each
capability claim by its receipt. Generated command histories or targeted fault
injection must retain seeds/history when used. A rendered plan, passing unit
suite or successful documentation review cannot establish full feature parity.

**Counterparts — Adopt:** Separate preflight simulation, measured rollout analysis and actual comparative runtime evidence. [Atlas](https://atlasgo.io/versioned/lint), [Argo Rollouts](https://argoproj.github.io/argo-rollouts/features/analysis/).

### CF-066 — Protect state, captures and keys without concealing replay risk

**P0 for local keys and operational records. P1 for external custody and
tenant captures. · C/I.** Protect the local signing keys, credential material
and sensitive run state with the configured storage and access policy.
Captures and external key custody must support their selected encryption,
rotation and recovery contracts. Check integrity, confidentiality and
freshness according to the respective policy.

**Acceptance criteria**

Read the local protected fixture with its permitted key. A missing or wrong
key produces a recovery failure. A stale, correctly encrypted plan fails
the current identity and freshness checks. For profiles that add key
rotation or capture recovery, exercise the configured rotation and restore
and verify the outcome against the retained state identity.

**Counterparts — Adopt / Integrate:** Protect state/plan bytes and key recovery without mistaking encryption for anti-replay protection. [OpenTofu](https://opentofu.org/docs/language/state/locking/), [OpenTofu state/plan encryption](https://opentofu.org/docs/language/state/encryption/).

## Engine implementation and research-tool boundaries

### CF-067 — Build the maintained lifecycle engine in Rust

**P0 · B/I · Owner direction, 2026-11-21.** Build the maintained Hallmark
engine in Rust. The F# local preview and its retained receipts provide the
behavioral reference. Use a pure decision function with explicit state and
observations. Filesystem, process, network, clock and persistence are adapter
effects. Preserve existing supported workflow inputs and version shared
contracts explicitly when the Rust engine adds a capability.

**Acceptance:** qualify the Wallet recovery slice with the required RustAssay
result, independent Cargo build and behavior evidence, and an installed
execution that exercises recovery. Compare retained F# behavior where the
same contract applies. Record commands, producer and source identities,
scope, exits and raw receipts. Apply the FsAssay Shape and authority
contracts to any F# reference changes. Missing required evidence remains
unresolved under CF-053 and CF-074.

**Counterparts — Adopt:** preserve the explicit provider/API boundaries useful
in [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/) and
[Score](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-068 — Qualify reusable automation at the provider boundary

**P1 · B/I · Owner direction.** One-off research tools may use
Python where its libraries provide an effective extraction/transcription path.
Implement lifecycle decisions in the Rust engine when a workflow becomes
maintained Hallmark automation. Use pinned Python, Bash and PowerShell hooks
and native external tools for scoped effects through the declared provider
contract.

**Acceptance:** preserve existing behavior before porting the active
`examples/playground/prepare.py`, `build.py`, `probe.py` and `verify.py` helpers
when they are selected for supported automation. Exercise the replacement with
the same consumer fixtures, relevant invocation/output/exit contracts and fault
cases. Retain scoped Assay, build and behavior evidence for the replacement.
Archived comparison recipes retain their original receipts. A future
audio-ingestion feature must bind source bytes,
model/provider version, parameters, timestamps, uncertainty and actual failures
to its transcript receipt. The current disposable transcription and source-page
snapshot scripts remain outside the product checkout and create no new Python
runtime dependency in Hallmark.

**Counterparts — Adopt / Integrate:** use replaceable automation/provider
boundaries as in [Pulumi](https://www.pulumi.com/docs/iac/concepts/automation-api/)
and versioned producer results as in [Tekton Chains](https://tekton.dev/docs/chains/).
Port maintained Hallmark behavior; retain identified external engines.

## CLI, MCP, typed authoring and workload gates

### CF-069 — Provide a stable CLI for operators and CI

**P0 · B/C · Owner direction.** The installed Hallmark CLI must expose package
validation and planning, execution and recovery, run and gate inspection, and
release verification. It must provide offline help, a capability inventory and
versioned machine-readable results with stable fields, diagnostic codes and
documented exit behavior. Capability discovery must identify supported manifest
formats, operations and registered gate providers. Results must identify the
plan or run and carry the outcome and available receipt references.
Stable diagnostic codes identify the affected scope and link to version-matched
offline remediation. Document when reconciliation is required before retry.
Non-interactive use reports missing inputs before effects. Optional structured
progress has its own stream and reports the terminal outcome when the process
can finish. After a lost process or connection, run inspection reports the
last recorded observation and any unresolved outcome.

**Acceptance criteria**

Install the packed CLI in a clean environment. Use its help and capabilities to
validate, plan and run a fixture without interactive input under the configured
policy. Check that a rejected plan and a failed required gate return documented
diagnostics and non-success exits. Existing supported command inputs and exit
meanings pass compatibility checks.
Exercise missing input and an interrupted operation. Verify that CLI and MCP
report the same domain diagnosis and recovery requirement. Progress output
preserves the parseability of the final machine-readable result.

### CF-070 — Expose planning and evidence through MCP

**P0 · C · Owner direction.** Hallmark must ship a local MCP server over
standard input and output. It must advertise a supported protocol revision and
named tools with versioned input and structured-result schemas. Its initial
tools must cover capabilities, manifest validation, planning, run status,
gate decisions and authorized receipt lookup. Results must include the same
plan identity, diagnostic codes and domain outcomes used by the CLI. Default
tools read the configured workspace and records and prepare plans without
executing workload stages. The stdio stream carries MCP messages.

**Acceptance criteria**

An independent MCP client initializes the installed server, discovers tools
and calls planning and status tools for the CLI qualification fixture. Compare
the plan digest, diagnostics and gate and receipt references for the same frozen
inputs or retained run. An invalid request returns structured diagnostics and
leaves the fixture's managed state unchanged. Startup and logging preserve the
stdio protocol stream.

Integrate the standard tool discovery, schemas and stdio transport.
[MCP tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools).
[MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports).

### CF-071 — Enforce execution scope through both CLI and MCP

**P0 for local execution. P1 for remote MCP. · C/I · Owner direction.** An
operator may explicitly enable named MCP effect operations such as apply and
resume for an allowed workspace and target. The server must bind each request
to an authorized caller, an identified plan or run, the target, and current
policy. Enabled tools must run through Hallmark's shared effect, authorization
and admission contracts, including any required decision under CF-054. Tool
inputs may reference registered operations and trusted workflow definitions
within that scope. Credentials resolve inside the execution boundary. Before
effects, associate the caller's operation reference with the run. Use that
reference to recover the recorded result after a lost response under CF-038.
Remote MCP must qualify authenticated Streamable HTTP and enforce tenant,
target and operation permissions for each request.

**Acceptance criteria**

An authorized local MCP call applies an admitted plan to a disposable target
and returns the recorded run. Attempt the same action with an unconfigured
scope, a changed plan digest or an unsatisfied required gate and observe refusal
before the protected effect. Reconnect after a lost response and recover the
original run by operation reference without repeating its effects. For remote
qualification, a caller from another tenant cannot invoke the effect or read
its receipts.

Integrate the MCP transport and authorization contracts and apply Hallmark's
policy at execution. [MCP tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools).
[MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports).
[MCP authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).

### CF-072 — Use Dhall to author workflows for the typed lifecycle model

**P0 · C/I · Owner direction.** Hallmark must ship a versioned Dhall schema
and reusable package and workspace definitions for typed parameters, package
and stage dependencies, output references, version transitions and named
gates. A pinned Dhall evaluator resolves and type-checks the declaration and
normalizes it to the versioned Hallmark workflow format. The same strict
decoder and domain validation must serve Dhall and direct JSON inputs.
The engine owns the lifecycle invariants, planning and gate decisions.

Workflow input identity must include the resolved declaration. The frozen plan
must also bind the authoring schema, evaluator version and content identities
of the source and transitive imports. Provide offline resolution for the pinned
local import closure. Validate the allowed import sources before resolution.
Require explicit policy and integrity pins for any enabled remote imports.
Use declared secret references for credentials and restrict ambient environment
and out-of-workspace reads at the evaluation boundary.

**Acceptance criteria**

Author the upgrade fixture in Dhall with a reusable package definition, a typed
dependency output and Verify gates. Resolve its imports offline. The equivalent
JSON declaration yields the same canonical workflow and plan decisions with
the same resolved inputs. CLI and MCP return the same digest for the same
frozen inputs. Authoring type errors, invalid graph invariants and missing
or changed pinned imports produce distinct diagnostics before effects. A
frozen plan cannot reuse evidence after its bound source or evaluator changes.

Use typed package and workspace declarations and integrate
Dhall's import, type-checking and normalization semantics for Hallmark's
schema.
[Dhall semantics](https://github.com/dhall-lang/dhall-lang/blob/master/standard/README.md).
[Dhall imports](https://github.com/dhall-lang/dhall-lang/blob/master/standard/imports.md).

### CF-073 — Deliver .NET, Python and Kotlin workloads in one lifecycle

**P0 for .NET. P1 for Python and Kotlin/JVM. · C/I · Owner direction.** Hallmark
must support workload profiles for .NET F# and C#, Python, and Kotlin/JVM. Each
profile must preserve the consumer's native build and test tooling, pinned
runtime, dependency inputs and package format. The shared workflow must
be able to combine packages from these profiles and use their typed outputs,
tests and Assay gates as dependencies. Reuse a consumer's native project or
package tooling through the registered adapter. Capability discovery must
report the qualified profile, scope and toolchain combination.

**Acceptance criteria**

Qualify the existing F# wallet first and use the C# orders fixture as a second
.NET consumer. Then qualify a native Python fixture and a Kotlin/JVM fixture
with their declared build, test and package inputs. A mixed workload release
must reference the same independently tested artifacts used by its component
plans and support the declared install, smoke-check and release path. An
unsupported runner or target profile reports the missing capability before its
dependent effect.

Use replaceable provider interfaces and a common workload contract.
[Pulumi Automation API](https://www.pulumi.com/docs/iac/concepts/automation-api/).
[Score workload specification](https://docs.score.dev/docs/score-specification/score-spec-reference/).

### CF-074 — Enforce native Assay gates at lifecycle stages

**P0 for the gate contract, Hallmark's Rust release and the .NET reference
profiles. P1 for Python and Kotlin/JVM. · B/I · Owner direction.** The Verify
stage must support multiple named gates for explicitly declared projects or
source scopes. Policy selects the applicable registered producer, version,
profile and required evidence for each gate and names the dependent effects,
such as publication, promotion or a protected migration. Dhall and JSON
workflows can reference registered gates. CLI and MCP must show each gate's
status, scope, selecting policy and reason for admission or refusal.

Hallmark's own Rust code must run the pinned RustAssay check in CI. Publishing
a Hallmark release that includes Rust code requires a complete passing check
on its declared functional-core scope and separate compiler and behavior
evidence. Record the actual source coverage and effect adapter boundaries.

RustAssay is opt-in for deployed workloads, including Rust applications.
Teams with authority over the target policy can enable advisory reporting or
require it before a named effect. An unconfigured workload can run without
RustAssay installed when its other requirements are satisfied. The effective
target policy takes precedence over a workflow's gate references.

Use verifiers as the first plugin kind. Register a trusted executable and pin
its provider ID, version, executable digest and invocation and receipt
formats in the plan. Declare supported runners, input scope and output
schema. Invoke the pinned tool as a bounded subprocess using its supported
arguments. The registration describes how to interpret its native output
and exit codes. Plugins supply evidence for Hallmark's policy decision.

| Workload | Native Assay evidence to qualify | Independent evidence |
|---|---|---|
| Rust | RustAssay `check --json` on the declared functional core. Preserve its native lexical verdict and scope limits. [RustAssay contract](https://github.com/CanonFlow-Assay/RustAssay/blob/main/docs/CONTRACT.md). | Cargo build and tests on the pinned toolchain. Qualify the packaged executable on the claimed runner. |
| .NET F# | FsAssay on the actual project or declared source scope. Use its supported `verify` route for registered test producers. Retain its scope and authority fields. [FsAssay verification](https://github.com/CanonFlow-Assay/FSharpAssay/blob/main/docs/refresh/usage.md). | Actual project build and behavior suite. Record producer or test-runner gaps as missing evidence. |
| .NET C# | CsAssay `verify` or `assess` using the supported SDK and required test producer for the consumer. [CsAssay commands](https://github.com/CanonFlow-Assay/CSharpAssay#commands). | Compiler and the consumer's behavior and architecture tests where required. |
| Python | PythonAssay `check --json` for the declared supported lexical rule and source scope. [PythonAssay contract](https://github.com/CanonFlow-Assay/PythonAssay/blob/main/docs/CONTRACT.md). | Run the project's own behavior tests and retain their results as a separate gate input. |
| Kotlin/JVM | KotlinAssay `verify` for the declared supported syntax profile, scoped compilation and configured behavior commands. [KotlinAssay contract](https://github.com/CanonFlow-Assay/KotlinAssay/blob/main/CONTRACT.md). | Retain actual consumer build, tests and applicable runtime checks for the selected JVM target. |

Hallmark must retain the original receipt and exit with the exact invocation
and the source membership and content, producer, toolchain, policy and artifact
identities relevant to the claim. It may run the pinned producer or admit an
authorized CI receipt with equivalent identity, integrity and freshness checks.
Record the producer's native result and the gate decision separately. Interpret
Pass, Fail, Inconclusive and ToolFailure using each producer's contract. Every
required gate must satisfy its own obligation before a dependent effect. Report
optional checks and unsupported or missing required evidence with their actual
scope and outcome.

**Acceptance criteria**

Run each supported gate on an actual fixture scope and retain its raw receipt.
A Hallmark Rust release with a finding or missing required RustAssay evidence
holds publication. A Rust workload with no gate configured completes delivery
on a host without RustAssay. Enable advisory reporting and observe that a
finding remains visible while delivery can proceed. Require the same gate in
target policy and observe that the finding holds the dependent effect.
Replacing the pinned verifier invalidates the reviewed plan.

A valid mixed-language release passes with evidence from all required producers
and behavior suites. Then observe a failed required test, an unavailable
producer and a receipt from different source bytes in separate cases. Each
blocks the dependent operation and explains the exact unsatisfied gate through
CLI and MCP. Omitting a gate reference from a candidate cannot remove a
requirement of the effective policy. Preserve the historical FsAssay xUnit v2
Inconclusive receipt and qualify supported evidence on a separately identified
candidate.

Adopt separate policy decisions, enforcement and source-bound producer records.
[Open Policy Agent](https://www.openpolicyagent.org/docs).
[Tekton Chains](https://tekton.dev/docs/chains/).

## Requirements from ecosystem consumers

GSTFlow exposes a failure that source tests missed. ONDCFlow and EDIFlow
make the scope of a domain result explicit. VERA's correction model informs
how Hallmark reuses evidence. Workstream informs the approval gate in
CF-054. The cases below use synthetic inputs or approved redacted fixtures.

### CF-075 — Qualify the packaged artifact on each claimed runtime

**P0 for the reference .NET profile. P1 for added runtime profiles. · I ·
GSTFlow and EDIFlow.** Verify must require evidence that the packaged
candidate ran through its normal consumer entry point on the claimed
profile. Bind each observation to the digest of the artifact selected for
Release, runtime and platform, entry point, test inputs and any relevant
rule-pack identity. Exercise a claimed offline profile with networking
disabled. When multiple variants claim equivalent domain behavior, exercise
the same fixtures and compare declared result fields under the same rule pack.
For artifacts transformed by a distributor, bind the delivered variant
and its runtime observations to the submitted build under CF-080.

**Acceptance criteria**

Start the packaged wallet and orders release candidates on the reference
profile. Bind the application probes and their outcomes to the respective
artifact digests. A startup failure leaves the required gate unsatisfied.

Retain the GSTFlow NativeAOT case where the core tests and build passed but
the installed executable failed. A required startup or behavioral failure
blocks the affected profile. For two variants that claim the same rules,
compare their verdicts and findings on identical fixtures, including a known
failure and an unknown or unsupported input. Report a variant with missing
runtime evidence as unqualified.
[GSTFlow](https://github.com/CanonFlowFoundation/GSTFlow/blob/9c783773a1e19e32577d9e1b2a7a809ec54daf6d/README.md) · [EDIFlow](https://github.com/CanonFlowFoundation/EDIFlow/blob/72f007b657fee9f9248173d871232cbf4661de08/README.md) · [Retained NativeAOT result](QUALIFICATION.md#retained-application-evidence).

### CF-076 — Admit domain receipts within their stated scope and authority

**P1 · I · GSTFlow, ONDCFlow and EDIFlow.** A registered domain gate must
declare the claim and observation authority it requires. Pin the producer
identity and the profile and rule-pack identities where applicable. Retain
the original receipt and verdict under CF-053, including the canonical
`.cff` receipt when the producer emits one. Bind the assessment to the exact input or
projection digest, declared coverage and evaluation context, including
the instant where it affects the result. The target policy must evaluate the
source and independence of observations when required by that claim.

A CanonFlow Format (CFF) payload digest check reports whether the payload
matches its declared digest in the stated representation. Record that check
with its scope. Required domain verdicts and issuer trust must be supported
by the corresponding producer evidence under the effective target policy.

**Acceptance criteria**

Check a GSTFlow-style CFF wrapper with a matching payload digest and no
trusted domain receipt. Report verified payload integrity and keep the
required domain gate unresolved. Change the payload and observe the
integrity check fail.

Admit an ONDCFlow receipt for a matching redacted quote under a policy that
trusts the producer for quote continuity. A changed checkpoint produces Fail.
A clean order-formation trace without the required independent observations
remains Inconclusive and leaves the broader gate unsatisfied. Reject receipt
reuse for a different profile, such as using one EDI partner's rule pack to
cover another partner.
[GSTFlow CFF prototype](https://github.com/CanonFlowFoundation/GSTFlow/blob/9c783773a1e19e32577d9e1b2a7a809ec54daf6d/playground/src/ZipUploader.tsx#L18-L47) · [ONDCFlow profile](https://github.com/CanonFlowFoundation/ONDCFlow/blob/157eeabca7d9d48a26fe2c7f193b19a0a960f3ba/README.md) · [Redacted quote contract](https://github.com/CanonFlowFoundation/ONDCFlow/blob/157eeabca7d9d48a26fe2c7f193b19a0a960f3ba/docs/REDACTED-QUOTE-EVIDENCE-ADAPTER-V1.md) · [EDIFlow profiles](https://github.com/CanonFlowFoundation/EDIFlow/blob/72f007b657fee9f9248173d871232cbf4661de08/README.md).

### CF-077 — Resolve corrected evidence before reuse

**P1 · I · VERA.** Hallmark must record authorized evidence corrections
and withdrawals with the affected receipt digest, actor, reason, effective
time and replacement digest when present. Preserve the original receipt and
decision under CF-057 and CF-066. Before a dependent effect, resolve the
current status of required evidence from its authorized source. When that
status cannot be established within the policy's freshness requirements,
leave the gate unresolved.

An optional VERA integration can help authorized readers find evidence
references. Admission uses the authoritative evidence records.

**Acceptance criteria**

Admit a valid receipt, correct it and inspect the original decision as of its
evaluation time. A later gate refuses reuse of the superseded receipt.
Replaying the old receipt or a cached reference from before the correction
leaves the correction in effect. An unavailable current status leaves the
gate unresolved. A replacement goes through current admission policy.
Reading or correcting another tenant's receipt requires authority for that
scope.
[VERA boundary](FEATURES.md).

### CF-078 — Attest to a release policy decision

**P0 for a scoped local release decision. P1 for external issuers and
evidence exchange. · I.** The reference profile requires a signed Hallmark
statement after its required gates are satisfied. Other delivery profiles
can require that statement for their own declared milestones. Use an issuer
trusted for the scope. Bind the statement to the immutable release
composition, plan, resolved configuration, target, effective policy and
supporting receipt digests. Include applicable approval decisions, evaluation
time and validity limits. Record the actual milestone covered.

Package the statement, exact artifacts and attested evidence in one
versioned release bundle that a recipient can verify without the original
workspace or artifact store. Identify any supplemental run records and
history that travel with the bundle. The portable verifier checks artifact
and evidence integrity and the issuer against configured trust. It reports
the recorded decision, signature coverage and verification scope. Any new
protected effect evaluates current caller authority, destination scope and
policy. When that policy requires a current disposition for reused
evidence, resolve it under CF-077 before admitting the effect.

**Acceptance criteria**

Verify a reference release from its portable bundle in a separate
environment with the original artifact paths unavailable and an explicit
trusted key. Alter an artifact and an attested receipt in separate copies.
Both fail verification. A key for a different issuer also fails verification.
Reject reuse for a new effect when its configured policy, input or target
identity differs, or when required evidence is stale.
Retrieve the historical signed decision by identity.

For additional issuers and evidence sources, exercise an applicable
withdrawal and record why a new gate refuses the superseded receipt.
Apply the publisher trust and evidence rules in CF-028, CF-052 and CF-053
to their supported scopes.

## Application delivery profiles

### CF-079 — Compose application delivery profiles in one plan

**P0 for the local server profile contract. P1 for additional profiles. ·
I · Owner direction.** A component must declare its delivery profile and
versioned provider capabilities under CF-049. It uses its native build and
packaging tools. Dhall and JSON must express the same profile choices and
dependencies. The plan identifies each component's selected artifact or
declared build output, channel and target. CLI and MCP report the supported
operations and evidence required for each component. Workflows may omit
inapplicable stages while retaining policy-required gates. A missing
capability for a required stage blocks its dependent operation.

| Profile | Release candidate | Main acceptance evidence |
|---|---|---|
| Self-hosted server | API, worker or service package on a declared host or supported container target | Installed version, readiness and required configuration and migration results |
| SaaS | Service composition with tenant, region or ring configuration | Service deployment and observed activation for the selected scope under CF-055 |
| Web client | Versioned browser assets or an offline-capable web app | Fresh browser session, supported cached-client behavior and active asset version where observable |
| Standalone desktop or CLI | OS-specific installer or portable executable | Fresh install or extraction, launch, supported upgrade and local state recovery where applicable |
| Mobile | Signed Android or iOS build for an identified distribution channel | Channel release state where applicable and an observed install, upgrade and launch on the claimed device scope |
| Library or SDK | Versioned registry package | A clean consumer resolves the published version and exercises the declared package entry point |

**Acceptance criteria**

Resolve and run the reference API and database through their selected
profile. The installed tools report the same provider and evidence requirements.

Validate one plan with an API, its database, a browser client and a mobile
client. Release a compatible client update using the identified services
already deployed. A missing capability for one required profile appears in
both CLI and MCP before the dependent effect. Qualify each profile and
channel with evidence from its actual package and target.

### CF-080 — Record distributor and installed state independently

**P1 · I.** For an external store or managed distribution channel, bind the
submitted artifact digest and publisher identity to the provider's app,
build, version and channel identifiers. Record the provider's observed
submission, review and availability states with their scope and observation
time. Leave a required milestone unsatisfied while it is pending or rejected.
When the distributor re-signs or transforms the upload, link its
delivered build identity to the submission and retain the observed device
variant, signing identity and delivered digest where exposed.

An installation claim requires a device or authorized fleet observation
for the claimed scope. A release attestation under CF-078 must identify the
milestone and evidence it covers. Reconcile an uncertain submission against
provider state under CF-038. Report the recovery actions the channel
supports and the device or authorized fleet scope observed.

**Acceptance criteria**

Submit a signed build to one test channel and record the provider build
identity. An accepted upload and a rejected build both leave the availability
gate unsatisfied. An available build leaves device state unknown until an
install probe confirms the delivered version. Discard a provider response in a
test and reconcile the existing submission before retry. Where a staged
rollout can be halted, verify the halt and retain the observation of devices
already on the update.
[App Store submission states](https://developer.apple.com/help/app-store-connect/reference/app-information/app-and-submission-statuses/) · [Android App Bundles](https://developer.android.com/guide/app-bundle) · [Google Play staged rollouts](https://support.google.com/googleplay/android-developer/answer/6346149).

### CF-081 — Qualify independent upgrades of clients and services

**P1 · I.** For clients that communicate with a separately released service,
declare the supported client, API and protocol version combinations. Use
that support window to gate breaking changes and schema contraction under
CF-005–CF-009. The test matrix must cover clients that can still be active
after a release, including cached browser builds and offline device clients
when supported. Where clients own durable local data, declare supported data
transitions and recovery under CF-004, CF-011 and CF-047.

**Acceptance criteria**

Run current and still-supported older clients against the candidate API.
Keep a cached browser build or offline device client in the fixture and
verify its supported path after the service changes. A removal that breaks
a supported client blocks the dependent release. For a standalone upgrade,
start with saved user data, interrupt the upgrade and exercise the declared
resume or restore path. Verify the resulting data and launch the resulting
version. [Service worker lifecycle](https://w3c.github.io/ServiceWorker/#service-worker-registration).

## Reproducible workspace and migration requirements

The first wallet and orders slice qualifies saved inputs, phase-specific
outputs and a frozen resolution for the local server profile. Published
migration lineage extends that contract to multiple release channels.

### CF-082 — Evolve saved inputs across application versions

**P0 · C/I.** A package must declare a versioned input schema with stable
identities for its saved input groups. Compare the target schema with actual
stored answers and their provenance before effects. Reuse a compatible value
and retain an operator override when a default changes. Resolve added or
incompatible fields through declared defaults, validated transformations or
explicit input according to field policy. Retain a removed value as inactive
within its retention policy until a declared purge removes it. Resolve
secret references under CF-050.

Bind the validated input snapshot to the run before its first dependent
effect. Resume with that snapshot. Admit a new plan for dependent work
that uses a different snapshot.

**Acceptance criteria**

Upgrade a populated V1 input group to V2 and a supported later version.
Preserve the operator override, identify a new required field and reject
an incompatible value before its dependent operation. Inspect a removed
field as inactive. Interrupt a run after accepting inputs and resume with
the same snapshot after the default source changes. The plan explains each
input decision without revealing secret material.

### CF-083 — Bind typed dependency outputs to their lifecycle stage

**P0 · C/I.** Declare dependency output schemas with their production stage
and version. A Build stage can consume declared build inputs and artifacts.
Later stages can also consume typed outputs from admitted earlier effects
under their provider contracts. Before consumption, validate the output's type,
producer identity, target and provenance. Plan the dependency before its
value exists and resolve the value from the producing stage.

Re-evaluate declared consumers affected by a changed dependency output.
Use the recovery and effect rules in CF-038 for dependent work. Preserve
unrelated operations that remain valid against the same inputs.

**Acceptance criteria**

Resolve a build artifact and a database binding through typed edges. Reject
a Build step that reads the binding produced after installation. Changing
the bound address affects the dependent configuration and appears in the
plan explanation. Unchanged artifacts and unrelated stages retain their
identities.

### CF-084 — Freeze workspace resolution before execution

**P0 for one local profile · C/I.** Generate a versioned resolution lock from
the workspace and target profile. Pin the selected packages, direct and
transitive edges, versions, artifact variants, source or artifact digests,
repository origin and inputs that determine resolution. Bind the lock to
the plan. Record the actual output digest when a locked source is built.
Support an explicit re-resolution operation that reports changed pins.

Replay must consume the recorded selection and validate it against current
trust and policy. A missing pin or incompatible target leaves its dependent
operation unresolved.

**Acceptance criteria**

Resolve a fixture with a transitive dependency, move its source channel and
plan again using the existing lock and staged inputs. The same inputs produce
the same selection through CLI and MCP. A missing or altered pin and an
incompatible target are refused. Explicit re-resolution identifies the new
selection and produces a new plan identity.

### CF-085 — Preserve published migration lineage across branches

**P1 · C/I.** Record the published release and migration step identities
that establish each supported predecessor path. Preserve shipped identities,
contents and required ordering when changes enter a new branch or channel.
Express a correction as a new migration with its own identity. Use CF-046
to validate the selected engine's applied ledger before execution.

**Acceptance criteria**

Merge release branches that share a published migration and have different
later changes. Verify the declared upgrade paths without repeating their
shared effect. An edited or reordered shipped step blocks the affected path
and names the predecessor. Qualify a corrective migration on the supported
predecessors.

## Reference distribution acceptance

### CF-086 — Ship a complete reference distribution

**P0 · I · Owner direction.** Deliver an installable CLI and local MCP
server, version-matched schemas, offline help and diagnostics, and a
versioned starter workspace. Include a prerequisite manifest identifying the
compatible runtime, Dhall evaluator, local adapters, fixture sources,
consumer toolchains and required Assay producers. The installed preflight
reports what is ready and names any missing prerequisite before the first
dependent effect. The reference workspace defines the permitted target,
initial policy and gate requirements. Its setup guide identifies how to
configure trust, credentials and local MCP access for that workspace.

Provide an executable path from setup to planning, applying, inspecting
and recovering a reference application. Include independent verification
of its signed release decision. The distribution's release checklist
requires evidence from the complete wallet journey and the orders consumer
under the shared contracts.

**Acceptance criteria**

Install the candidate on a clean supported host using the distribution,
declared prerequisites and identified fixture inputs. Run the included
workflow through the installed interfaces. Remove a declared prerequisite
and observe a diagnostic before effects. Complete a wallet upgrade with
data preservation and recover a run after an interrupted effect. Verify
the release proof with an explicit trust configuration in a separate
environment. Repeat the local delivery contract with orders.
Exercise a run with an unsatisfied required gate and observe that the scoped
release decision remains unresolved.

## Web interface and release coordination

Organize the web interface around Overview, Releases and Environments. A
release page is the main workspace for a coordinator, operator or reviewer.
It brings together the plan, runs, evidence and handoffs for a selected
release and target. The board, timeline and diagrams link back to that page.

### CF-087 — Inspect a release and its observed target state

**P1 · I · Owner direction.** The release page must show the target,
candidate and observed installed versions, current stage, last confirmed
observation, blocker, responsible owner and next available action. The
environment view must show the desired and observed state with observation
times. Represent drift, stale observations and unknown state explicitly.
When a connection drops, keep the last observation visible with its age
and indicate that live updates are unavailable.

Use the plan, run, release and observation identities produced by the shared
lifecycle under CF-020, CF-038 and CF-040. Support direct links to a release
and target, a run or a stage attempt. Reopening a link preserves its selected
scope. Apply scope permissions to lists, links and counts.

**Acceptance:** open an interrupted migration through a copied run link.
Identify its last confirmed state and the next reconciliation action.
Compare the browser result with the CLI and MCP results for the same run.
Disconnect updates and observe the last confirmed observation and its age.
Advance past its freshness window and observe the stale status. A caller
without target access cannot retrieve the release through a direct link.
[Argo CD desired and live state comparison](https://argo-cd.readthedocs.io/en/stable/user-guide/diffing/).

### CF-088 — Review changes, evidence and protected actions

**P1 · I.** Before a protected browser action, show the frozen plan, the target,
installed-to-candidate diff and the planned operations. Identify changed
artifacts and configuration, migration and compatibility obligations,
affected consumers and gates blocking the operation. Present the required
approval, evidence scope and reason for any refusal. Let a reviewer inspect
an attempt's retained logs, original gate receipt and signed release decision
within their permissions.

Show whether each gate is required or advisory and which policy selected it.
Keep Pass, Fail, Inconclusive, ToolFailure and missing evidence legible.
Show the scope and time of a historical signed decision and the current
admission result for a pending action. When browser actions are enabled, bind
apply, approval, hold, resume and promotion to the exact plan or run, target,
caller and current policy under CF-054, CF-071, CF-074 and CF-078. An action
uses the shared lifecycle and returns the resulting decision or run.

Provide an offline inspection view for a saved release bundle using the
portable verifier in CF-078. Show the checked artifact and evidence scope,
signer verification, and the recorded decision. Mark supplemental run
records and history. Show current admission when its inputs are available
and report it as unknown otherwise.

**Acceptance:** open a plan for review, change its target baseline or withdraw
a required receipt, and attempt the old action. The browser explains the
refusal before effects and links to the affected evidence. An authorized
operator can inspect the new plan and make a fresh decision. Open a valid
bundle and an altered copy offline. Identify which evidence the signature
covers and why the altered copy fails verification.
[GitLab deployment approvals](https://docs.gitlab.com/ci/environments/deployment_approvals/).

### CF-089 — Prioritize releases on a dashboard and Kanban board

**P1 · I · Owner direction.** The dashboard must surface releases needing
attention, active runs and environments with drift or unknown state.
Prioritize items needing evidence, approval or reconciliation. Show the
reason, responsible owner and time waiting. Each count and queue item links
to its scoped records.

Offer a list and a Kanban board over the same release-and-target records.
Cards show version, current stage, blocker, owner and next available action.
Derive lane placement from recorded lifecycle state and show the specific
reason for waiting, failure or an unresolved outcome on the card. Filter by
workspace, environment, owner and status. Keep filters in a shareable view.
When supported, assignment and priority changes update coordination records.
Lifecycle transitions use the explicit authorized actions in CF-088.

**Acceptance:** show the same release in staging and production with
different gate outcomes. The two cards report their own status and action.
Filter to a team's pending work and open the blocking receipt. Reassign a
card and inspect the coordination record. Check that applying a plan still
requires its explicit action. The list and board agree on visible records
and status.

### CF-090 — Coordinate downstream handoffs and release notes

**P1 · I · Owner direction.** Show downstream impact from the declared
dependency graph. Each affected team sees the originating change and
version, what it consumes, the action it owes and the evidence needed to
proceed. Let the coordinator find waiting handoffs and let each team inspect
the upstream note from its own view. Common changes reaching a consumer
through multiple paths display once with the dependency paths available.

Keep note revisions, acknowledgments and approvals as identifiable records.
Show corrections and withdrawals on affected downstream views and surface
pending actions that require reevaluation under CF-077. Use only notes and
evidence the viewer is authorized to read.

**Acceptance:** in the PostgreSQL, Kafka and OpenSearch fixture, an
OpenSearch owner sees the relevant upstream change and who owes the next
compatibility result. Withdraw that result and confirm that the team's view
shows the correction and the pending cutover decision reflects current
evidence. The historical decision remains inspectable by identity.

### CF-091 — Show execution history and planned schedules

**P1 for the execution timeline. P2 for Gantt. · I · Owner direction.**
Show the observed timeline for a run with stage attempts, waits,
approvals, retries and recovery observations. Link each event to its
record and show timestamp and time zone. Mark gaps and unresolved outcomes.

A Gantt view must show the planned schedule for a selected release across
teams, dependencies and maintenance windows. Show recorded execution
alongside the plan when available. Label forecasts and their inputs.
Display unscheduled work and missing durations explicitly. Show a critical
path when the dependency and timing inputs support it. A schedule preview
shows downstream impact before any authorized schedule change is saved.

**Acceptance:** interrupt an effect and confirm the timeline shows the last
known event with the outcome unresolved. In a scheduled copy of the fixture,
preview a two-hour PostgreSQL delay and identify the Kafka and OpenSearch
windows it affects. The reviewed schedule retains its identity during preview.
[Mermaid Gantt syntax](https://mermaid.js.org/syntax/gantt.html).

### CF-092 — Generate diagrams on demand from identified records

**P1 for dependency diagrams. P2 for additional diagram views. · I · Owner
direction.** Generate diagrams from the selected plan, run or release.
Show its identity, scope and observation time. Provide a structured list or
table and let users copy Mermaid source generated from the same data. Use
escaped labels and a trusted renderer configuration. Render the selected
view when requested and allow users to focus on affected components.

| Diagram | Question it answers |
|---|---|
| Dependency and impact graph | What blocks this step, and which consumers are affected? |
| Sequence of handoffs | What did each team do, and which reply or acknowledgment is due? |
| Recovery state | What was observed, and which recovery transitions remain available? |
| Evidence lineage | Which sources, artifacts and receipts support this release decision? |

**Acceptance:** request a dependency diagram for an identified run with a
blocked component. Follow its upstream edge to the gate. The diagram and
text view agree on the blocker. Regenerate Mermaid from the same selected
records and obtain the same graph. Inspect a different plan and see its
own edges and identity.
[GitHub Actions visualization graph](https://docs.github.com/en/actions/how-tos/monitor-workflows/use-the-visualization-graph)
· [Mermaid sequence diagrams](https://mermaid.js.org/syntax/sequenceDiagram.html)
· [Mermaid state diagrams](https://mermaid.js.org/syntax/stateDiagram.html).

### CF-093 — Make operational charts traceable to their runs

**P2 · I.** Start with charts for wait time by gate or owner, failed and
retried attempts by stage, and elapsed time from interruption to a known
state. Identify the time range, population, unit and any missing data.
Show whether counts are per release, run or attempt. Selecting a chart
value opens the underlying records under the same scope and filters.

**Acceptance:** chart a fixture with one retried stage and one run with a
missing recovery observation. Reconcile each value to its records. The
chart counts the retry at the attempt level and exposes the missing
observation when reporting recovery time.

### CF-094 — Make operational views accessible and responsive

**P1 · I.** Core views must support keyboard navigation, visible focus,
clear control labels and status text alongside color. Provide a list or
table for boards, charts and graphs and a control that performs each
supported drag action without dragging. Preserve focus across live
updates and expose loading, empty, stale and error states. On narrow
screens, prioritize release status, blocker, next action and evidence.

**Acceptance:** using only the keyboard, find a blocked release, open its
gate receipt and return to the originating card or row. Repeat with a
screen reader and with the chart replaced by its table. On a narrow
screen, inspect the same gate and target with equivalent status and
available actions.
[W3C complex images](https://www.w3.org/WAI/tutorials/images/complex/)
· [WCAG dragging movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html)
· [Mermaid accessibility](https://mermaid.js.org/config/accessibility.html).

### First web delivery slice

1. Start with a read-only release page, the attention queue, the observed
   timeline and plan and evidence inspection on one qualified Wallet run.
2. Use the three-owner fixture to qualify the board, downstream notes and
   the dependency diagram. Enable review and recovery actions when they
   meet the same policy and target checks as CLI and MCP.
3. Add Gantt when scheduled windows and durations are available. Use real
   run histories to qualify operational charts and additional diagrams.

## Active delivery priorities

The first delivery is an installed Rust CLI that upgrades the existing
Wallet on an owned local PostgreSQL target. Interrupt the run after the
migration commits, recover the recorded run against the migration ledger,
and verify the resulting signed bundle independently.

| Priority | Release scope | Requirements exercised |
|---|---|---|
| Pilot | Frozen JSON plan, one local target, durable recovery, exact migration readback, scoped producer evidence and portable signed proof. | CF-001–CF-005, CF-028, CF-038–CF-040, CF-046, CF-053, CF-067, CF-069, CF-074–CF-075, CF-078. |
| Public MVP | Add Dhall/JSON plan parity and local MCP, then run the PostgreSQL, Kafka and OpenSearch handoff with downstream notes and an independent operator. | CF-020, CF-043–CF-045, CF-069–CF-072, CF-083–CF-084, CF-086, CF-090. |
| After the pilot | Qualify the second unchanged workload and a release inspection page. Use observed handoff and recovery problems to choose the next provider or UI view. | CF-073, CF-079–CF-081, CF-087–CF-089, CF-091–CF-094. |

The remaining requirements stay in the following qualification backlog.
Pilot coverage is limited to the acceptance cases exercised by its fixture.
The [Gantt chart](docs/delivery/gantt.html) and [Kanban board](docs/delivery/kanban.html)
show the task dependencies and current status.

## Broader qualification sequence

| Sequence | Deliverable | Exit evidence |
|---|---|---|
| 0. Preserve authority and baseline | Retain existing behavior, source identities and failure receipts. Identify a supported FsAssay test producer for the implementation candidate. | Separate compiler, behavioral and actual FsAssay results with any unresolved evidence. |
| 1. Typed authoring and local interfaces | Dhall and JSON into the shared model, installed CLI, local MCP, registered gates, typed dependency outputs and workspace resolution. CF-069–CF-074, CF-083–CF-084, CF-086. | Install the tools on a clean reference host. One authored workflow produces the same frozen plan and diagnostics through both interfaces. Refuse a wrong-stage dependency and an altered pin. |
| 2. .NET reference release | Use wallet and then orders to qualify the upgrade, recovery, exact packaged runtime and local signed policy decision. CF-002–CF-011, CF-046–CF-047, CF-073–CF-075, CF-078, CF-082, CF-086. | Fresh and upgraded state agree for the declared predecessor and target. Preserve saved inputs across the transition and retry. Retain the required Assay, runtime and restore evidence and verify the signed decision in a separate environment. |
| 3. Mixed workloads and reproduction | Qualify Python and Kotlin/JVM in CF-073–CF-074. Add published migration lineage under CF-085, targeted development and tenant reproduction. | A mixed release uses separate native gate receipts. Preserve shared migration history across branches. Reproduce an identified deployment and selected tenant state in a sandbox. |
| 4. Consumer qualification | GSTFlow and ONDCFlow exercise CF-075–CF-076 against the registered gate contract. EDIFlow provides a second rule-pack case. | Verify the exact packaged runtime and exercise bounded domain receipt admission. Qualify only the exercised profiles. |
| 5. Application delivery profiles | CF-079–CF-081. Use the wallet and orders service baseline to extend qualification to browser, standalone, SaaS, a registry package and one managed mobile test channel. | Profile-specific evidence, mixed-version compatibility, tenant activation, package consumption and a store-to-device test. |
| 6. Portable providers | CF-021–CF-026, CF-033–CF-035, CF-048–CF-051 and selected provider integrations under CF-068. | Exact provider versions and conformance cases. Separately observed VM and Kubernetes behavior. |
| 7. Enterprise operation | CF-052–CF-058, CF-061–CF-062, CF-066, CF-077–CF-078, remote CF-071 and distributed CF-039. | Tenant-scoped execution and remote MCP, rollout rings, external trust and secrets, recovery, evidence corrections, signed decisions and Workstream approval qualification. |
| 8. Optional extensions | CF-059–CF-060, CF-063–CF-064 and optional VERA recall under CF-077. | A real consumer justifies update automation, portal, estimates or traffic debugging. Evidence recall respects the same access policy and current dispositions. |

The [delivery roadmap](ROADMAP.md) records current milestone status and the
order for qualifying the proposed capabilities.

### First-slice acceptance scenarios

These are planned cases, not execution receipts. Start with small V1/V2/V3
fixtures that expose adjacent and supported skip-version paths. Then exercise
the unchanged wallet and orders consumers under their preserved source/framework
contracts. The existing GSTFlow native failure remains a negative qualification
case; do not modify it merely to obtain a passing delivery result.

| Case | Required observation | Main requirements |
|---|---|---|
| AC-01 fresh target | V3 creates the intended five-column schema and owned seed records; business probes pass | CF-002–CF-004, CF-013 |
| AC-02 sequential upgrade | Populated V1 → V2 → V3 preserves data and reaches equivalent declared V3 observations | CF-003–CF-004 |
| AC-03 supported skip | V1 → V3 resolves required intermediate work; unsupported predecessors refuse before effects | CF-002, CF-004 |
| AC-04 broken transition | Remove the fifth-column transition operation; forward check differs and publication is blocked | CF-004 |
| AC-05 incompatible dependent | Resolve an out-of-range schema or retain an old incompatible application instance; activation/contraction is blocked | CF-005–CF-009 |
| AC-06 interrupted backfill | Restart between batches and verify completion/data invariants without blind repetition | CF-010, CF-038 |
| AC-07 lost commit response | Commit externally, lose its response and recover from retained evidence; do not repeat uncertain imperative work | CF-038 |
| AC-08 edited history and drift | Separately alter an applied migration and a live schema object; report each through the correct observation | CF-046 |
| AC-09 data recovery | Restore populated data to an isolated target; observe accepted RPO/RTO and business invariants | CF-047 |
| AC-10 compatible/incompatible downgrade | Activate an older compatible application; reject an incompatible one without implicit database reversal | CF-011 |
| AC-11 identity and source policy | Reject changed plan/output/secret identity, wrong signer and an unapproved transitive store | CF-028–CF-029, CF-038 |
| AC-12 missing/failed evidence | Fail a required behavior case and supply an unsupported/stale producer receipt; preserve actual outcomes and block admission | CF-053, CF-065 |
| AC-13 targeted reproduction, next slice | Install published baseline, replace one component, export bug context and recreate selected tenant state with sandbox connections | CF-014–CF-017, CF-032, CF-042 |
| AC-14 typed authoring | Resolve a Dhall declaration offline and compare its canonical workflow and decisions with JSON. Reject an invalid type, invalid graph and changed import in separate cases. | CF-001, CF-072 |
| AC-15 CLI and MCP parity | An installed CLI and an independent MCP client return the same frozen plan digest, diagnostic codes and gate results for the same fixture. | CF-020, CF-069–CF-070 |
| AC-16 MCP execution scope | Apply an admitted plan in an enabled disposable context. Confirm scope, identity and gate refusals before effects and inspect the recorded run after a lost response. | CF-038, CF-054, CF-071 |
| AC-17 .NET gates | Execute FsAssay and CsAssay on their declared .NET scopes and retain independent builds and tests. A failed or missing required gate blocks the dependent release. | CF-053, CF-073–CF-074 |
| AC-18 mixed workload gates, next slice | Compose .NET, Python and Kotlin/JVM artifacts and resolve their independent native gate receipts. A missing or stale required receipt blocks promotion and names the affected scope. | CF-073–CF-074 |

### Ecosystem acceptance scenarios

Run these planned cases after wallet and orders establish the shared gate
contract. The GSTFlow failure in [retained application evidence](QUALIFICATION.md#retained-application-evidence)
provides a retained negative case for AC-19.

| Case | Required observation | Main requirements |
|---|---|---|
| AC-19 packaged behavior | Execute the exact release candidate on each claimed profile. A build with a failed native startup blocks that profile. Variants claiming equivalent behavior agree on a common synthetic corpus. | CF-033, CF-065, CF-075 |
| AC-20 domain scope | A redacted ONDCFlow quote check can pass within its scope and fail on a changed checkpoint. A clean trace with missing independent observations stays Inconclusive. A different partner or rule pack cannot reuse that receipt. | CF-053, CF-076 |
| AC-21 evidence correction | Supersede a receipt used in an earlier decision. Historical inspection retains that decision and repeat admission rejects both the old receipt and a cached reference to it. | CF-053, CF-077 |
| AC-22 handoff and authority | Verify the Workstream pack and an authorized approval for the exact rollout. Alter the pack, swap the release and remove actor authority in separate cases. Each blocks execution. | CF-054 |
| AC-23 release-policy attestation | Verify a valid signed statement and attempt reuse with a different artifact, target or withdrawn receipt. Report each refusal through the shared gate result. | CF-028, CF-053, CF-077–CF-078 |

### Application delivery acceptance scenarios

| Case | Required observation | Main requirements |
|---|---|---|
| AC-24 composed delivery | Resolve one plan with an API, database, web client and mobile client. Publish a compatible client update against the identified existing API. Report missing required capabilities through both interfaces. | CF-001, CF-020, CF-049, CF-079 |
| AC-25 SaaS activation | Deploy one service composition, activate it for the ready tenant and observe the previous behavior in the other tenant. A failed tenant readiness check withholds activation. | CF-035, CF-055, CF-079 |
| AC-26 mobile distribution | Record a test-channel submission, reconcile a lost response, observe availability and verify the delivered build and upgrade on a test device. An available build without an install observation leaves installed state unknown. | CF-038, CF-075, CF-078, CF-080 |
| AC-27 mixed client versions | Verify a supported cached browser build or an offline device client on return after an API rollout. Removal of a contract used by a supported client blocks the rollout. | CF-005–CF-009, CF-081 |
| AC-28 standalone upgrade | Launch the exact installer output or portable package. Upgrade from a supported prior version with saved user data, interrupt at a declared boundary, then verify recovery, data and the installed version. | CF-004, CF-011, CF-047, CF-075, CF-081 |
| AC-29 registry consumption | Publish one immutable SDK version to the declared test registry. A clean consumer resolves and exercises that version. An unexpected package digest or wrong signer fails admission under the configured source policy. | CF-028–CF-029, CF-051, CF-079 |

### Workspace and migration acceptance scenarios

| Case | Required observation | Main requirements |
|---|---|---|
| AC-30 saved input evolution | Retain an operator override after a default changes. Report missing and incompatible fields before effects. Resume with the accepted input snapshot. | CF-038, CF-082 |
| AC-31 typed dependency output | Resolve an artifact and an installed service binding through typed edges. Refuse the runtime binding during Build. Changing an output affects its declared consumer. | CF-005, CF-049, CF-083 |
| AC-32 frozen workspace | Advance a dependency channel after locking. Replay the same closure through CLI and MCP, detect a changed pin and explain an explicit re-resolution. | CF-031, CF-072, CF-084 |
| AC-33 eligible predecessor | Use the published predecessor variant and target analysis profile. Missing eligible coverage leaves a required upgrade unsatisfied. Demonstrate fresh-only qualification independently. | CF-004, CF-033 |
| AC-34 published migration lineage | Merge paths from two published branches, preserve their shared effect and reject edits to a shipped step. Exercise a forward correction. | CF-046, CF-085 |
| AC-35 automation diagnostics | With missing input, refuse before effects and return the same stable code through CLI and MCP. After interrupted execution, report whether reconciliation is required and preserve valid machine-readable output. | CF-038, CF-069–CF-071 |
| AC-36 platform upgrade | Observe a mixed-version fleet, refuse incompatible dispatch, and report completion only after the required runners are verified. Retain observed state after a partially applied migration. | CF-038, CF-062 |

### Reference distribution scenarios

| Case | Required observation | Main requirements |
|---|---|---|
| AC-37 clean-host workflow | Install the distributed tools and load the supplied reference workspace with declared prerequisites. CLI and MCP return the same frozen plan and diagnostics. Remove one prerequisite and observe refusal before its dependent effect. | CF-036, CF-049, CF-069–CF-072, CF-086 |
| AC-38 complete reference upgrade | Use the installed tools to upgrade populated wallet data, recover after interruption and inspect the actual installed state. Refuse a reviewed plan whose target baseline has changed. Collect the required native and packaged-runtime evidence and seal the scoped release decision. Repeat with orders. | CF-002, CF-038–CF-040, CF-065, CF-074–CF-075, CF-078, CF-082, CF-086 |
| AC-39 independent release verification | Use a separate environment and configured trust to verify the portable release proof. Reject altered bytes and an untrusted issuer. With changed target policy, retain the recorded historical decision and refuse use for a new effect until current admission is satisfied. | CF-028, CF-053, CF-071, CF-078, CF-086 |

### Baseline capability versus new work

| Area | Retained local baseline | Additional requirement still to implement/qualify |
|---|---|---|
| Planning | Validated six-kind graph and identity-bound resume | Frozen package resolution, typed dependency outputs, target-matched predecessor checks and forward-equivalence publication gate |
| Authoring | Versioned JSON workflow schema | Versioned Dhall package and workspace schema feeding the shared model, with pinned imports and offline evaluation |
| Interfaces | Installed CLI with help, capabilities and JSON records | CLI result compatibility and gate discovery, local MCP client qualification and policy-bound execution |
| Database | Exercised Flyway/PostgreSQL recipe and repeated no-op migration | Edited-history rejection, schema drift, mixed-version compatibility, backfill faults and restore drills |
| Delivery targets | Local process, files and owned Compose reconciliation | VM/service-manager and Kubernetes providers with independently qualified capabilities |
| Reproduction | Artifact/release bundles and separately staged consumer fixtures | Versioned saved inputs, tenant captures, owned setup/runtime configuration and isolated environment mirrors |
| Builds | Identified process builds and byte-deduplicating CAS | Qualified hermetic/action caching and provider input closure |
| Trust and secrets | Local versioned vault, explicit-key ECDSA and portable release verification | External leases/identity/KMS, standard provenance/SBOM, source policy and multi-user authority |
| Workloads and evidence | .NET wallet and orders deployments, historical 52-test Hallmark baseline and separate FsAssay Inconclusive receipt | Scoped, policy-bound FsAssay and CsAssay gates, then separately qualified PythonAssay and KotlinAssay gates in a mixed workload release |
| Consumer qualification | Retained GSTFlow NativeAOT runtime failure | Packaged runtime checks, domain receipt admission, evidence corrections and signed release policy decisions |
| Application delivery profiles | Local service deployment, packaged CLI and portable signed release | Browser delivery, tenant activation, standalone upgrades, mobile test channels and cross-version client compatibility |
| Language/tooling | F# reference core/runtime/CLI and Python Playground helpers | Rust engine with scoped hooks and retained reference evidence |

### Assumptions and useful owner inputs

- **Chosen product and language.** Dhall is the typed authoring interface and
  Rust is the maintained engine direction. JSON is a supported input and interchange
  format. CUE and Score are design references and possible integrations.
- **First users/consumer:** assume platform engineers operating self-hosted Linux
  and PostgreSQL, with wallet/orders as qualification consumers. An explicitly
  different first customer workload can change provider ordering.
- **Supported history:** use V1/V2/V3 to establish the first fixture matrix. Do
  not infer an unlimited promise to upgrade every historical release directly.
  Product support/retirement policy still needs an owner decision.
- **Version policy:** begin with an explicitly named SemVer policy. PVP or other
  formats require their own defined parser/compatibility policy; do not treat
  the recording's unclear example version letters as a selected format.
- **Mirror data:** default to explicitly selected owned setup/configuration and
  sandbox endpoints. Which additional tenant data may be captured, its masking
  rules and retention need a real consumer/data-owner policy.
- **Scale and service targets:** tenant/node counts, deployment topology,
  availability/disruption budgets, RPO/RTO, offline requirements and identity
  provider are useful inputs before production qualification. They do not block
  this local requirements draft or authorize production changes.
- **Automation.** Rust owns lifecycle decisions. Use scoped hooks and native
  tools through the versioned provider contract.

### Review status

The initial review retained the full recording, source hashes, 56 downloaded
primary-page snapshots and six pinned representative source-file inspections
outside the product checkout. The follow-up adds MCP and Dhall specifications
and references to the four Assay producer contracts. Every requirement has an
acceptance condition. The status table above identifies baseline behavior and
the capabilities that still require implementation and qualification. Use
CF-053, CF-065, CF-067 and CF-074 to evaluate implementation evidence.

## Recording source register

The source is the user-provided recording, SHA-256
`288f8525145080cc1afa2abaefe23df49cbb30a0bf41bc3f3e44ad8796461009`.
The `audio-requirements/` directory of the local research archive holds the
source manifest, full timestamped automatic transcript, decoder parameters,
failed and partial attempts, and completed-pass receipt.
The final pass consumed all 43:32.709 with voice-activity filtering and emitted
612 segments; its last emitted speech segment ends at 43:21. A quality heuristic
flags 213 segments for low average token probability or higher-temperature
fallback. Those flags are not word-error measurements. The source register
therefore records topic-level interpretations, uncertain passages and explicit
inferences rather than claiming a verbatim transcript. Technical terms were
normalized only where context supported them.

| Source | Recording offset | Recovered topic and interpretation boundary |
|---|---|---|
| REC-01 | 00:00–00:45 | Older versions, installation overview, upgrade semantics. Opening wording is incomplete; no universal downgrade guarantee is inferred. |
| REC-02 | 00:45–05:12 | Compatible/breaking schema changes; nullable/backfill/application/non-nullable sequence; application/schema packages and version bounds. Some example version letters/names are unclear. |
| REC-03 | 05:12–06:17 | Placement and the distinction between fresh installation and incremental/day-two upgrades. |
| REC-04 | 06:17–08:29 | Four-column to five-column example; fresh versus transitioned state; forward checks before publication and new-tenant installation. |
| REC-05 | 08:29–11:41 | Seed/setup data, runtime-created packages such as EDI/partner definitions, capture and environment mirror into a sandbox; DDL and limited managed DML. |
| REC-06 | 11:41–13:05 | CLI/TUI, package/repository selection, dependency planning and progress; Nexus/Artifactory named as examples. |
| REC-07 | 13:05–14:18 | Typed configuration/database definitions and raw SQL/systemd escape paths. |
| REC-08 | 14:18–16:44 | Package/component source layout, developer sandbox and target consistency, nginx and supporting resources in one model. |
| REC-09 | 16:44–18:16 | Multi-VM sandbox/topology and environment parity; repeated Compose wording is unclear and does not establish multi-host Compose support. |
| REC-10 | 18:16–20:05 | VM/bare-host and Kubernetes profiles; resource management and explicit component versioning, including SemVer/PVP terminology. |
| REC-11 | 20:05–21:09 | Existing Git/CI branching practices, signing, publisher identity and centralized audit. |
| REC-12 | 21:09–23:52 | Allowed package stores; build/distribute/publish composition; topic/branch snapshots and development versions. |
| REC-13 | 23:52–25:01 | QA sandbox installation of a selected published version and version-linked issue reporting. |
| REC-14 | 25:01–28:58 | Introductory cost/comparison wording is unclear; the clear later discussion covers architecture/distro/feature variant matrices and target filtering. No pricing commitment is inferred. |
| REC-15 | 28:58–30:40 | Feature/customization/white-label inputs and cross-platform sandbox variants. Exact storage details are not sufficiently clear to select a database product. |
| REC-16 | 30:40–31:30 | Public/internal documentation discussion is unclear. It does not authorize access to private documentation or establish a new public API. |
| REC-17 | 31:30–33:32 | Multi-replica rolling updates, failures, recovery and known installed state. Broad rollback wording is narrowed by explicit database/effect limits. |
| REC-18 | 33:32–34:34 | Developer, QA, business/product and stakeholder impact; role-specific output is an inferred acceptance design. |
| REC-19 | 34:34–36:14 | Monorepo example with multiple components and multiple packages per component. A mandatory monorepo migration is not inferred. |
| REC-20 | 36:14–39:07 | Install published baseline, build changed components, change sets and sandbox overlays; some publication/version examples are unclear. |
| REC-21 | 39:07–41:35 | Automation instead of stale manually updated documentation; major/minor classification, collected change sets and release changelogs. |
| REC-22 | 41:35–43:32.709 | Documentation/sample discovery and closing discussion. The spoken URL is unclear and was not reconstructed or treated as a dependency. |
