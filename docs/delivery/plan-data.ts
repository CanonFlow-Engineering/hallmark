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
}

// Days are estimates. A null duration has no assigned window.
const deliveryPlan: { readonly updated: string; readonly tasks: readonly DeliveryTask[] } = {
  updated: "21 Nov 2026",
  tasks: [
    {
      id: "HM-01", phase: "pilot", status: "complete", days: 1, after: [],
      title: "Set the engine and pilot scope",
      refs: "CF-067, CF-069, CF-078",
      detail: "Use Rust for the maintained engine and the F# preview as the behavioral reference.",
      acceptance: "The requirements and roadmap name one engine and one Wallet recovery release boundary.",
      evidence: "The active priorities, engine contract and contributor instructions now agree."
    },
    {
      id: "HM-02", phase: "pilot", status: "complete", days: 1, after: ["HM-01"],
      title: "Freeze the F# reference",
      refs: "CF-038, CF-053, CF-065, CF-067",
      detail: "Pin the committed reference, SDK and package inputs. Preserve the original distinct Assay outcomes.",
      acceptance: "Locked restore, Release build and retained tests execute on the pinned reference SDK.",
      evidence: "Commit 5c6adc4 rebuilt with SDK 10.0.301. All 52 native tests passed. Historical FsAssay results keep their original scope."
    },
    {
      id: "HM-03", phase: "pilot", status: "complete", days: 2, after: ["HM-02"],
      title: "Implement the Rust recovery decision",
      refs: "CF-038, CF-053, CF-067, CF-074",
      detail: "Use a pure function to decide what to do with a migration ledger observation. Keep the first change small enough for an owner walkthrough.",
      acceptance: "Exercise applied, definitively absent and unknown outcomes. Reject changed checksums and observations for another plan, target, operation or migration.",
      evidence: "Five behavior tests, Cargo build, formatting, Clippy and the pinned RustAssay check passed on the declared Rust core."
    },
    {
      id: "HM-04", phase: "pilot", status: "queued", days: 4, after: ["HM-03"],
      title: "Persist one local run and its pending work",
      refs: "CF-002, CF-005, CF-038–CF-040",
      detail: "Record the frozen plan, pending migration identity and recovery decision together. Recover that record before dispatching work after restart.",
      acceptance: "Restart between recording intent and observing the result. A duplicate observation creates no duplicate effect. Changed plan or target identity is refused."
    },
    {
      id: "HM-05", phase: "pilot", status: "queued", days: 4, after: ["HM-04"],
      title: "Reconcile the Wallet migration from PostgreSQL",
      refs: "CF-002–CF-005, CF-038, CF-046",
      detail: "Reuse the existing Wallet and Flyway recipe. Observe the expected migration ID and checksum through a readback adapter.",
      acceptance: "Lose the response after commit. Restart and confirm the ledger entry, preserved rows and a single migration effect. An uncertain result holds the run."
    },
    {
      id: "HM-06", phase: "pilot", status: "queued", days: 4, after: ["HM-05"],
      title: "Install the Rust CLI for the pilot",
      refs: "CF-001, CF-005, CF-039, CF-069, CF-075",
      detail: "Support the pilot's JSON plan, apply, status and resume through one installed CLI and an owned local target.",
      acceptance: "A separate clean consumer installs the binary, reviews a frozen plan and recovers a run using the same identity."
    },
    {
      id: "HM-07", phase: "pilot", status: "queued", days: 4, after: ["HM-06"],
      title: "Bind the signed proof to its gates",
      refs: "CF-028, CF-053, CF-074, CF-075, CF-078",
      detail: "Attach the exact runtime evidence and configured verifier receipts to a scoped release decision and portable bundle.",
      acceptance: "Required missing evidence holds release. A trusted verifier checks the original bundle and rejects an altered copy or a different issuer."
    },
    {
      id: "HM-08", phase: "pilot", status: "queued", days: 2, after: ["HM-07"],
      title: "Qualify the clean-host Wallet recovery",
      refs: "CF-038, CF-065, CF-075, CF-078",
      detail: "Run the interrupted upgrade from a clean install and record the observed target, recovery and independently verified proof.",
      acceptance: "An operator can identify the commit, recover without repeating it and verify the signed bundle with the original artifact store absent."
    },
    {
      id: "HM-09", phase: "mvp", status: "queued", days: 5, after: ["HM-08"],
      title: "Qualify Dhall and local MCP",
      refs: "CF-020, CF-069–CF-072, CF-083–CF-084, CF-086",
      detail: "Add typed authoring and a local MCP client over the same planner, evidence and admission decisions as the CLI.",
      acceptance: "Equivalent Dhall and JSON declarations produce the same frozen plan. An independent MCP client sees the same decisions and rejects a changed pin."
    },
    {
      id: "HM-10", phase: "mvp", status: "queued", days: 6, after: ["HM-09"],
      title: "Build the three-owner release fixture",
      refs: "CF-043–CF-045, CF-075, CF-077–CF-078, CF-090",
      detail: "Use PostgreSQL, Kafka and OpenSearch to exercise distinct owners, mixed-version evidence and downstream release notes.",
      acceptance: "Cutover stays held while compatibility evidence is unresolved. Downstream A receives the relevant note and a later correction."
    },
    {
      id: "HM-11", phase: "mvp", status: "queued", days: null, after: ["HM-10"],
      title: "Observe an independent operator",
      refs: "CF-045, CF-065, CF-086, CF-090",
      detail: "Run the fixture with an operator outside the project when one is available. Compare with the team's normal workflow.",
      acceptance: "Record how long it takes to identify the committed effect, find the blocker owner and choose a safe next action."
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
