# Hallmark command reference

The **Hallmark CLI (Rust)** (`hallmark`) provides the operator entry point,
pinned rootless Podman worker launch (`HALLMARK_WORKER_IMAGE`), independent
bundle verification, and the standalone migration pilot commands. The
**Hallmark Lifecycle Engine (F#)** (`hallmark engine <COMMAND>`) owns six-stage
planning, execution, the run journal, target-aware recovery, convergence, and
Gate B release admission.

## Hallmark CLI (Rust) Commands

| Command | Behavior |
|---|---|
| `hallmark engine <COMMAND> [ARGS...]` | Delegate a lifecycle command to the **Hallmark Lifecycle Engine (F#)** (via the pinned rootless Podman worker image when `HALLMARK_WORKER_IMAGE=sha256:...` is set, or the local `.NET 10` worker assembly). |
| `hallmark mcp [--state-dir DIR] [--allow-effects]` | Run the local `stdio` Model Context Protocol (MCP) JSON-RPC 2.0 server over the shared planner, run journal, and Gate B reducer (`hallmark_capabilities`, `hallmark_plan`, `hallmark_status`, `hallmark_gate_b_evaluate`, `hallmark_bundle_verify`, `hallmark_apply`, `hallmark_resume`). |
| `hallmark bundle-verify BUNDLE --public-key FILE` | Independently verify a `.zip` release bundle's ECDSA P-256 signature, attested step evidence digest, and all artifact SHA-256 digests natively in Rust. |
| `hallmark release-verify RELEASE --public-key FILE` | Independently verify a signed `release.json` record against the trusted public key PEM natively in Rust. |
| `hallmark plan MANIFEST` | Run the standalone Rust migration pilot planner against a PostgreSQL container. |
| `hallmark apply MANIFEST --plan-digest SHA256 --state-dir DIR` | Apply a reviewed migration in the standalone Rust migration pilot. |
| `hallmark status --state-dir DIR` | Read the checksummed run in the standalone Rust migration pilot. |
| `hallmark resume MANIFEST --run ID --state-dir DIR` | Reconcile an interrupted run in the standalone Rust migration pilot. |
| `hallmark --help` / `--version` | Print the installed command surface or version. |

## Hallmark Lifecycle Engine (F#) Subcommands (`hallmark engine ...`)

| Subcommand | Behavior |
|---|---|
| `capabilities` / `schema` / `doctor` | Emit the JSON capability contract (`Build`, `Install`, `Configure`, `Migrate`, `Verify`, `Release`; operations `process`, `write-file`, `capture`, `compose-up`, `postgres-migrate`, `http`, `release`), workflow JSON Schema, or environment diagnostics. |
| `plan MANIFEST [--state-dir DIR]` | Validate the six-stage DAG, ancestor coverage, and source closure, and emit the preview plan and `planDigest`. |
| `freeze MANIFEST FILE [--state-dir DIR]` | Write the frozen plan, `planDigest`, `sourceDigest`, and input file digests to `FILE`. |
| `emit-json MANIFEST` | Parse a workflow and emit its canonical JSON definition (`Wire.toJson`). |
| `three-owner-check FIXTURE` | Evaluate a multi-owner release fixture (affected package graph, deduplicated dependency paths, VERA evidence corrections/withdrawals, version-bound release notes, and four role views). |
| `triage --context NAME [--fixture FILE] [--state-dir DIR]` | Emit an operator incident triage report identifying committed imperative effects, the responsible blocking owner team, and the safe `resume` command. |
| `apply MANIFEST [--plan-digest SHA256] [--state-dir DIR]` | Execute the six-stage workflow under the context lock and persist step outcomes in the `fsync`-backed run journal. |
| `resume MANIFEST --run ID [--state-dir DIR]` | Reconcile an interrupted or failed run using target-aware `ExternalLedger` readback (`postgres-migrate` and `release`) without repeating committed imperative effects. |
| `converge MANIFEST --run ID [--state-dir DIR]` | Reuse completed `Build`, `Migrate`, and `Release` steps while re-evaluating `DesiredState` and `Verify` stages. |
| `status --context NAME [--state-dir DIR]` | List checksummed run records for `NAME`. |
| `history --context NAME [--state-dir DIR]` | Verify and report the hash-chained `history.ndjson` event log. |
| `drift --context NAME --run ID [--state-dir DIR]` | Inspect recorded output files and owned Compose services for drift. |
| `secret-set NAME --context NAME [--state-dir DIR]` | Encrypt a secret from `stdin` using AES-256-GCM and bind its version to future plans. |
| `public-key --context NAME [--state-dir DIR]` | Export the context's ECDSA P-256 public key in PEM format. |
| `bundle FILE --context NAME --run ID --public-key FILE [--state-dir DIR]` | Create a deterministic, uncompressed (Stored) `.zip` release bundle containing `release.json`, `attested-steps.json`, `run.json`, `history.ndjson`, and content-addressed `artifacts/<sha256>`. |
| `bundle-verify FILE --public-key FILE` | Verify a `.zip` release bundle inside the F# engine or isolated worker container. |
| `release-verify FILE --public-key FILE` | Verify a `release.json` signature against the trusted public key PEM. |

## Exit Codes

- `0`: Command succeeded.
- `1`: A workflow step failed during `apply`, `resume`, or `converge` (for example, Gate B refused `release` signing due to a missing or mismatched bound receipt).
- `2`: Drift observed during `drift`.
- `3`: Action held or runtime/state evidence unavailable or conflicting (for example, `Flyway checksum conflicts with the frozen migration` or an unobserved external ledger).
- `64`: Invalid command-line invocation.
