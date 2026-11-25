# Local operation

Build and install the **Hallmark CLI (Rust)** and **Hallmark Lifecycle Engine (F#)**
using the [README](README.md). Use an owned PostgreSQL container on local
rootless Podman (or Docker) with the preceding versioned migrations already
recorded by Flyway, and have the pinned Flyway image available locally.

For a repeatable disposable target that exercises the full six-stage Wallet
release, interruption recovery, Gate B bound-receipt enforcement, and
independent bundle verification, run:

```bash
fixture=$(mktemp -d)
bash scripts/verify-wallet-engine.sh "$fixture/evidence" podman
```

## Running a Six-Stage Workflow

From the repository root, plan and freeze a workflow manifest through the
**Hallmark CLI (Rust)** and **Hallmark Lifecycle Engine (F#)**:

```bash
runner="$PWD/artifacts/rust/bin/hallmark"
state=$(mktemp -d "$PWD/artifacts/run-state.XXXXXX")
manifest=/path/to/workflow.json

"$runner" engine plan "$manifest" --state-dir "$state" > "$state/plan.json"
"$runner" engine freeze "$manifest" "$state/frozen.json" --state-dir "$state"
```

When the workflow includes a `postgres-migrate` stage, supply `FLYWAY_PASSWORD`
(or configure a `passwordSecret` via `hallmark engine secret-set`) and apply the
reviewed `planDigest`:

```bash
read -rs FLYWAY_PASSWORD
export FLYWAY_PASSWORD
digest=$(jq -er '.planDigest' "$state/frozen.json")
"$runner" engine apply "$manifest" --plan-digest "$digest" --state-dir "$state"
unset FLYWAY_PASSWORD
```

If the runner is interrupted after PostgreSQL commits the migration, inspect the
run journal and resume with the same manifest, target, and state directory:

```bash
"$runner" engine status --context wallet-release --state-dir "$state"
run_id=$("$runner" engine status --context wallet-release --state-dir "$state" | jq -er '.[0].RunId')
"$runner" engine resume "$manifest" --run "$run_id" --state-dir "$state"
```

The `postgres-migrate` adapter reads `public.flyway_schema_history`, verifies
the target identity and Flyway CRC32 checksum, and reconciles a committed
migration without dispatching Flyway again.

To export the public key, create a deterministic release bundle, and verify it
independently with the **Hallmark CLI (Rust)**:

```bash
"$runner" engine public-key --context wallet-release --state-dir "$state" > "$state/public-key.pem"
"$runner" engine bundle "$state/release.bundle.zip" --context wallet-release --run "$run_id" --public-key "$state/public-key.pem" --state-dir "$state"
"$runner" bundle-verify "$state/release.bundle.zip" --public-key "$state/public-key.pem"
```

See the [command reference](COMMANDS.md) for the complete command and exit contract.
