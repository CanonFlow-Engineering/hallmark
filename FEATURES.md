# Hallmark feature selection

Reviewed 2026-11-16. Adopt means an original CanonFlow contract/design;
integrate means an adapter to a separately selected tool. Documentation review
does not qualify a product's runtime. No upstream source is copied here.

| Area and source | Useful capability | Selection | Delivery |
|---|---|---|---|
| [Dagger](https://docs.dagger.io/getting-started/introduction/) | Typed artifacts and a sandboxed incremental function DAG | Integrate a pinned build backend after a plain local-build adapter | M1 |
| [Dagger secrets](https://docs.dagger.io/config/secrets/) | Provider references and secret-aware interfaces | Adopt reference-only contracts; validate adapter delivery/redaction | M1–M2 |
| [Nix](https://nix.dev/manual/nix/2.34/introduction.html) | Dependency closure and immutable package identity | Adopt content-bound closure; keep Nix optional | M0–M2 |
| [OpenTofu workflow](https://opentofu.org/docs/intro/core-workflow/) | Review an identified plan before applying changes | Adopt stage separation; integrate infrastructure plans | M2 |
| [OpenTofu state locking](https://opentofu.org/docs/language/state/locking/) | Backend-dependent prevention of competing state writers | Require declared backend lock capability; add fencing where the target requires it | M2 |
| [Ansible check/diff](https://docs.ansible.com/projects/ansible/latest/playbook_guide/playbooks_checkmode.html) | Configuration modules and preview coverage | Integrate; record unsupported preview operations explicitly | M2 |
| [Flyway migrations](https://documentation.red-gate.com/flyway/flyway-concepts/migrations/versioned-migrations) | Ordered migration identity and applied-history checksums | Integrate a pinned migration engine; preserve its history | M3 |
| [Liquibase preconditions](https://docs.liquibase.com/secure/user-guide-5-1/what-are-preconditions) | Preconditions and distinct check failure/error handling | Adopt the design; select an adapter only after version/license review | M3 |
| [Temporal activities](https://docs.temporal.io/activity-definition) | Retry boundaries and idempotent external activities | Adopt explicit retry semantics; integrate durable orchestration only when required | M2–M5 |
| [Temporal versioning](https://docs.temporal.io/workflow-definition) | Code-compatible replay and worker versioning | Bind producer implementation to resume; no universal exactly-once claim | M0–M5 |
| [Argo CD](https://argo-cd.readthedocs.io/en/stable/) | Git desired-state reconciliation for Kubernetes | Integrate through a target adapter; avoid a second competing controller | M4 |
| [Argo Rollouts](https://argo-rollouts.readthedocs.io/en/stable/concepts/) | Canary/blue-green rollout with metric-based gates | Integrate progressive delivery after a basic deployment is qualified | M4 |
| [Sigstore](https://docs.sigstore.dev/cosign/verifying/verify/) | Verify artifact signatures against identities/keys | Integrate verification with an explicit trust policy | M1–M4 |
| [SLSA provenance](https://slsa.dev/spec/v1.2/provenance) | Relate outputs to source, inputs and producer | Adopt provenance references; evaluate standard attestation export | M1–M4 |
| [Octopus releases](https://octopus.com/docs/releases) | Snapshot deployment process and package references | Adopt an immutable release composition | M4 |
| [Octopus releases](https://octopus.com/docs/releases) | Tenant values can remain outside the release snapshot | Make mutable environment/tenant inputs explicit and separately identified | M2–M4 |
| [Harness approval context](https://developer.harness.io/docs/continuous-delivery/kb-articles/articles/harness-approval-notifications/) | Show resolved deployment target and artifact at decision time | Adopt concrete decision payloads; integrate Workstream without treating its ledger as execution authority | M4 |
| [JFrog release promotion](https://docs.jfrog.com/governance/docs/promote-a-release-bundle-v2-version) | Immutable bundles with attached promotion evidence | Adopt the pattern; do not make deprecated RLM a platform dependency | M4 |
| CanonFlow assurance and Assay producers | Evidence-bound admission with existing verdict contracts | Integrate existing producers; retain missing/failed evidence | M0–M4 |
| CanonFlow VERA | Versioned evidence recall and corrections | Optional discovery/recall consumer; it cannot authorize execution | M6 |

## Selection findings

Dagger's current documentation is marked 1.0-beta. An adapter must select and
test a specific compatible version; examples from another version are not an
API contract. JFrog's current RLM documentation states that feature development
ended on 2026-07-31, and that the deprecation removes stage-promotion support.
Its stated end-of-life date is 2028-01-31. Retain immutable composition and
evidence concepts without selecting that product path as a new dependency.

Liquibase's inspected [current license](https://github.com/liquibase/liquibase/blob/main/LICENSE.txt)
declares FSL-1.1-ALv2; the reviewed file has Git blob identity
`e7bda0e792778f03bbf1e1335962d5dd328e0a96`. Do not assume a current release has
the terms of an older release. This experiment copies no Liquibase code and
selects no runtime dependency on it.


## Essential features beyond a stage list

| Essential | Failure addressed | Priority |
|---|---|---|
| Exact input closure and producer/config/policy/target identities | Stale caches, mismatched resume, changed bytes under a label | M0 |
| Stable stage/resource identity and ownership | Renames losing history; two packages claiming the same resource | M0–M2 |
| Typed artifact/output references and secret references | Undeclared dependencies; accidental secret values in plans | M1 |
| Capability discovery and strict versioned wire format | Advertised operations that the installed adapter cannot accept | M1 |
| Time, resource, output and network budgets | Hung subprocesses, unbounded logs and uncontrolled side effects | M1 |
| Local/air-gapped operation with a portable evidence bundle | Hidden network dependencies and unverifiable handoff | M1–M4 |
| Cancellation and ambiguous-outcome reconciliation | Retrying an operation that may already have committed | M1–M3 |
| Lease/lock ownership and target fencing | Concurrent writers and a stale worker acting after lease expiry | M2 |
| Drift and import/adoption rules | Confusing externally managed resources with owned resources | M2 |
| Environment/namespace/tenant scoping | Applying a correct plan to the wrong target | M2 |
| Migration compatibility, backup and recovery evidence | Treating an application rollback as a database rollback | M3 |
| Expand/contract sequencing and mixed-version compatibility | A rolling application update breaking the current schema/users | M3–M4 |
| Release pinning, policy gates and health observations | Promoting a moving artifact or treating unknown health as success | M4 |
| Authentication, authorization and audit boundaries | A local label or approval record being mistaken for a credential | M4–M5 |
| SBOM, vulnerability/license evidence and trust policy | Unsourced dependency or signature claims | M4 |
| Garbage collection and evidence retention policy | Removing artifacts still required for recovery or audit | M5 |
| Metrics, traces and operational SLOs | A technically completed run with unacceptable service behavior | M4–M5 |

## Deliberate deferrals

Do not build a replacement container runtime, Kubernetes controller, universal
SQL migration executor, package registry or general durable workflow service
for the first consumer. Do not claim that a checksum authenticates a producer,
that a signature establishes correctness, or that one database transaction
establishes atomicity across the whole lifecycle. Features must earn their
implementation cost through an actual consumer and failure test.

## Current implementation evidence

The Delivery column above is the original selection sequence. Actual local
delivery now includes the F# graph, strict wire/schema, installed CLI, process and
Compose adapters, named/versioned local secrets, CAS, safe resume refusal, live
drift/convergence, Flyway recipe, immutable local releases, offline bundles and
local pointers. QUALIFICATION.md separates these executed results from proposed Dagger,
Nix, OpenTofu, Ansible, Argo, Sigstore and enterprise integrations. No reviewed
product name is advertised as an implemented adapter merely because it appears
in this selection.
