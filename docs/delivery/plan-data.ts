// The browser bundle is generated from the TypeScript sources with npm run build.
type PlanPhase = "pilot" | "mvp" | "later";
type TaskId = `HM-${number}`;
type TaskState = "complete" | "queued" | "active" | "blocked" | "later";

interface DeliveryTask {
  readonly id: TaskId;
  readonly phase: PlanPhase;
  readonly status: TaskState;
  readonly days: number | null;
  readonly after: readonly TaskId[];
  readonly title: string;
  readonly refs: string;
  readonly detail: string;
  readonly acceptance: string;
  readonly evidence?: string;
  readonly issue?: number;
}

// Days are estimates. A null duration has no assigned window.
const deliveryPlan: {
  readonly updated: string;
  readonly forgejo: string;
  readonly tasks: readonly DeliveryTask[];
} = {
  updated: "24 Nov 2026",
  forgejo: "http://localhost:3000/arun/hallmark",
  tasks: [
    {
      id: "HM-01", phase: "pilot", status: "complete", days: 1, after: [],
      title: "Lock the Hallmark CLI (Rust) and Lifecycle Engine (F#) split",
      refs: "CF-067, CF-069, CF-078",
      detail: "Give Hallmark CLI (Rust) operator commands, pinned worker launch and bundle verification, and Hallmark Lifecycle Engine (F#) planning, execution, run journal, recovery and release admission.",
      acceptance: "CF-067, docs/ARCHITECTURE.md and ROADMAP.md define the formal Hallmark CLI (Rust) and Hallmark Lifecycle Engine (F#) ownership boundary.",
      evidence: "Formal ownership table and decisions D0–D4 recorded in docs/ARCHITECTURE.md and CF-067."
    },
    {
      id: "HM-02", phase: "pilot", status: "complete", days: 1, after: ["HM-01"],
      title: "Qualify the .NET 10 Hallmark Lifecycle Engine (F#) with FsAssay",
      refs: "CF-038, CF-053, CF-065, CF-067",
      detail: "Restore the five F# projects on .NET 10 and emit schema-valid TRX test reports for FsAssay verification.",
      acceptance: "Locked restore, Release build and registered TRX test execution pass under FsAssay 1.0.4 verify.",
      evidence: "FsAssay 1.0.4 verify returned authoritative Pass (5/5 F# projects loaded, 16/16 files analyzed, 61/61 TRX tests passed)."
    },
    {
      id: "HM-03", phase: "pilot", status: "complete", days: 2, after: ["HM-02"],
      title: "Verify the Rust recovery and SOTA lifecycle core",
      refs: "CF-038, CF-053, CF-067, CF-074",
      detail: "Maintain the pure #![no_std] Rust kernel covering recovery, six-stage DAG invariants, Gate B semilattice and Bundle v2 checks.",
      acceptance: "Exercise applied, absent and unknown ledger outcomes, DAG release ancestor coverage and target fingerprint checks.",
      evidence: "14/14 Rust workspace tests, Clippy (-D warnings) and RustAssay Pass (5/5 core files, 0 findings) verified."
    },
    {
      id: "HM-04", phase: "pilot", status: "complete", days: 4, after: ["HM-03"],
      title: "Persist local runs and deterministic Bundle v2 archives",
      refs: "CF-002, CF-005, CF-038–CF-040",
      detail: "Save the frozen plan and pending operation under the state lock and normalize archive entry timestamps for deterministic bundles.",
      acceptance: "Repeated bundle creation produces identical byte-for-byte SHA-256 digests and target admission rejects cross-environment replay.",
      evidence: "Pinned archive timestamps to 1980-01-01T00:00:00Z, sorted artifact digests and verified target fingerprint admission."
    },
    {
      id: "HM-05", phase: "pilot", status: "complete", days: 4, after: ["HM-04"],
      title: "Enforce Migration Ledger v2 and catalog digest audit",
      refs: "CF-002–CF-005, CF-038, CF-046",
      detail: "Audit applied script ordering, checksum equality, ExpectedCatalogDigest match, live pg_catalog drift and active sessions.",
      acceptance: "Reject checksum drift, expected-vs-observed catalog mismatch, live post-migration catalog drift and active non-idle sessions.",
      evidence: "Admission.auditMigrationLedgerV2 verifies ExpectedCatalogDigest, live catalog digest and active session count."
    },
    {
      id: "HM-06", phase: "pilot", status: "complete", days: 4, after: ["HM-05"],
      title: "Wire Hallmark CLI (Rust) to the pinned rootless Podman F# worker",
      refs: "CF-001, CF-005, CF-039, CF-069, CF-075",
      detail: "Enforce sha256 worker image digests, rootless Podman checks, keep-id UID mapping and automatic host manifest/state bind mounts.",
      acceptance: "Hallmark CLI (Rust) plans host manifests through the pinned rootless Podman worker and passes clean-checkout delegation tests.",
      evidence: "Verified host-manifest planning via HALLMARK_WORKER_IMAGE in rootless Podman and clean-checkout Cargo delegation tests."
    },
    {
      id: "HM-07", phase: "pilot", status: "complete", days: 4, after: ["HM-06"],
      title: "Enforce ancestor-scoped Gate B bound-receipt admission in SealRelease",
      issue: 1,
      refs: "CF-028, CF-053, CF-074, CF-075, CF-078",
      detail: "Scope SealRelease to transitive Verify ancestors of Release and verify Assay receipts bound to plan, source, policy and target digests.",
      acceptance: "Unrelated Verify branches do not block Release, while missing, failed or mismatched bound receipts refuse release signing.",
      evidence: "Runner.fs evaluates Admission.evaluateBoundReleaseGate over transitive Verify ancestors before Storage.seal."
    },
    {
      id: "HM-15", phase: "pilot", status: "complete", days: 4, after: ["HM-07"],
      title: "Unify the six-stage Hallmark Lifecycle Engine (F#) and target-aware recovery",
      refs: "CF-001–CF-005, CF-028, CF-067, CF-083–CF-084",
      detail: "Execute the six-stage graph, ExternalLedger readback reconciliation, fsync checkpoint durability and hallmark { ... } CE DSL.",
      acceptance: "All 61 F# unit and runtime tests pass on multi-branch release, ExternalLedger single-dispatch recovery and bundle verification.",
      evidence: "Verified 61/61 F# tests in Hallmark.Tests via VSTest and TrxRunner.fs under FsAssay 1.0.4 verify."
    },
    {
      id: "HM-08", phase: "pilot", status: "complete", days: 2, after: ["HM-07", "HM-15"],
      title: "Qualify the clean-host Wallet recovery and release drill",
      refs: "CF-038, CF-065, CF-067, CF-075, CF-078",
      detail: "Run the six-stage Wallet upgrade through the first-class F# postgres-migrate adapter, refuse release on missing or mismatched receipts, recover an interrupted migration with one dispatch and verify the signed bundle.",
      acceptance: "An operator can launch the pinned worker container, recover without repeating a committed migration and verify the signed bundle from independent Rust and container consumers.",
      evidence: "scripts/verify-wallet-engine.sh passed on rootless Podman PostgreSQL 18.7 and Flyway 13.12.1 (1 Flyway dispatch, missing/mismatched Gate B receipts refused, bundle verified natively in Rust and in an isolated container)."
    },
    {
      id: "HM-09", phase: "mvp", status: "complete", days: 5, after: ["HM-08"],
      title: "Qualify F# workflow DSL to JSON and local stdio MCP",
      refs: "CF-020, CF-069–CF-072, CF-083–CF-084, CF-086",
      detail: "Expose pure F# hallmarkWorkflow { ... } workflow emission to canonical JSON and a local stdio MCP server over the shared planner and Gate B reducer.",
      acceptance: "Equivalent F# DSL and JSON declarations produce the same frozen plan digest, and an MCP client observes identical Gate B decisions.",
      evidence: "Wire.toJson + hallmarkWorkflow CE DSL and runtime/Mcp.fs (hallmark mcp) verified in RuntimeTests.fs under FsAssay 1.0.4 verify (64/64 TRX tests passed)."
    },
    {
      id: "HM-10", phase: "mvp", status: "complete", days: 6, after: ["HM-09"],
      title: "Build the three-owner release fixture",
      refs: "CF-043–CF-045, CF-075, CF-077–CF-078, CF-090",
      detail: "Exercise distinct component owners, mixed-version evidence and VERA supersession/withdrawal refusal across multi-service dependencies.",
      acceptance: "Cutover stays held while compatibility evidence is unresolved. Downstream consumers receive the relevant note and later correction.",
      evidence: "Admission.evaluateThreeOwnerCutover and examples/three-owner/fixture.json verified across CLI (three-owner-check), MCP (hallmark_three_owner_evaluate) and FsAssay 1.0.4 verify (66/66 TRX tests passed)."
    },
    {
      id: "HM-11", phase: "mvp", status: "complete", days: 1, after: ["HM-10"],
      title: "Observe an independent operator",
      refs: "CF-045, CF-065, CF-086, CF-090",
      detail: "Provide single-command CLI/MCP operator triage (`hallmark engine triage` / `hallmark_triage`) and run a cold-start operator observation on the mid-incident workspace.",
      acceptance: "Record how long it takes to identify the committed effect, find the blocker owner and choose a safe next action.",
      evidence: "scripts/verify-operator-drill.sh, hallmark engine triage, and cold-start isolated operator observation identified committed V2 migration, opensearch-projection-team blocker (VERA-CORR-2026-09), and safe resume action (67/67 TRX tests passed; external human operator study noted in QUALIFICATION.md)."
    },
    {
      id: "HM-12", phase: "later", status: "later", days: null, after: ["HM-11"],
      title: "Add the release inspection page",
      refs: "CF-087–CF-092, CF-094",
      detail: "Start with a read-only release page, attention queue, timeline and evidence inspection. Use the pilot to decide what the operator needs next.",
      acceptance: "The operator can follow a blocker to its original receipt with the same identity and status as CLI and MCP."
    },
    {
      id: "HM-13", phase: "later", status: "later", days: null, after: ["HM-11"],
      title: "Qualify a second unchanged consumer",
      refs: "CF-073–CF-076, CF-079–CF-081",
      detail: "Use Orders to test reuse of the shared contracts. Choose additional languages and targets from observed need.",
      acceptance: "An installed consumer exercises its own pinned runtime, upgrade path and configured gates."
    },
    {
      id: "HM-14", phase: "later", status: "later", days: null, after: ["HM-12"],
      title: "Add scheduling and operational charts",
      refs: "CF-091–CF-094",
      detail: "Qualify Gantt, additional Mermaid views and aggregate metrics against actual timing and run data.",
      acceptance: "Forecasts cite their inputs and every count drills down to the same scoped records."
    }
  ]
};
