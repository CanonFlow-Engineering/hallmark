#!/usr/bin/env bash
set -euo pipefail
umask 077

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
evidence=$(realpath -m -- "${1:?Supply a fresh evidence directory}")
engine=${2:-podman}
case "$engine" in
    podman|docker) ;;
    *) printf 'Choose podman or docker for the Wallet fixture.\n' >&2; exit 64 ;;
esac
mkdir -m 700 -- "$evidence"
cd -- "$repo"

postgres_image=postgres:18.7@sha256:2ea8630df176ad090ec92136dee850397c5af8781e4bcf2e4162593691c61bbc
flyway_image=flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657
runtime_cli=$(realpath -- "$(command -v "$engine")")
case "$engine" in
    podman)
        runtime=("$runtime_cli" --remote=false)
        postgres_image="docker.io/library/$postgres_image"
        flyway_image="docker.io/$flyway_image"
        ;;
    docker) runtime=("$runtime_cli" --host unix:///var/run/docker.sock) ;;
esac
db_name=fsplayground_hallmark
database=
worker_image_tag="hallmark-worker:hm08-$$"
runner_pid=
cleanup() {
    local result=$?
    if [[ -n $runner_pid ]]; then
        pkill -KILL -P "$runner_pid" 2>/dev/null || true
        kill -KILL "$runner_pid" 2>/dev/null || true
        wait "$runner_pid" 2>/dev/null || true
    fi
    touch -- "$evidence/return-response"
    if [[ -n $database ]]; then
        "${runtime[@]}" rm --force --volumes "$database" > "$evidence/database-cleanup.out" \
            2> "$evidence/database-cleanup.err" || true
    fi
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

record_command() {
    local name=$1
    shift
    printf '%q ' "$@" > "$evidence/$name.command"
    printf '\n' >> "$evidence/$name.command"
}

run_check() {
    local name=$1 expected=$2 result=0
    shift 2
    record_command "$name" "$@"
    "$@" > "$evidence/$name.out" 2> "$evidence/$name.err" || result=$?
    printf '%s\n' "$result" > "$evidence/$name.exit"
    if [[ $result != "$expected" ]]; then
        printf '%s exited %s, expected %s\n' "$name" "$result" "$expected" >&2
        cat -- "$evidence/$name.err" >&2
        return 1
    fi
}

git rev-parse HEAD 'HEAD^{tree}' > "$evidence/source.txt"
printf '%s\n' "$engine" > "$evidence/container-engine.txt"
if [[ $engine == podman ]]; then
    run_check rootless 0 "${runtime[@]}" info --format '{{.Host.Security.Rootless}}'
    grep -Fxq -- true "$evidence/rootless.out"
fi
run_check images 0 "${runtime[@]}" image inspect --format '{{.Id}} {{.Os}}/{{.Architecture}}' "$postgres_image" "$flyway_image"

# 1. Build F# Lifecycle Engine worker, package rootless Podman worker image, and build Rust Hallmark CLI
run_check dotnet_publish 0 dotnet publish cli/Hallmark.Cli.fsproj -c Release -o "$evidence/worker-build/worker" --nologo
chmod -R a+rX "$evidence/worker-build/worker"
cp -- Containerfile.worker "$evidence/worker-build/Containerfile.worker"
run_check worker_image_build 0 "${runtime[@]}" build -f "$evidence/worker-build/Containerfile.worker" -t "$worker_image_tag" "$evidence/worker-build"
worker_image_id=$("${runtime[@]}" image inspect --format '{{.Id}}' "$worker_image_tag")
worker_image="sha256:${worker_image_id#sha256:}"
printf '%s\n' "$worker_image" > "$evidence/worker-image-digest.txt"

cd -- "$repo/rust"
export CARGO_TARGET_DIR="$evidence/target"
run_check rust_build 0 cargo build --release --locked --offline --package hallmark-cli
cd -- "$repo"

mkdir -p -- "$evidence/bundle" "$evidence/consumer" "$evidence/adapters" "$evidence/independent-consumer"
mkdir -m 755 -- "$evidence/bootstrap"
install -m 755 -- "$CARGO_TARGET_DIR/release/hallmark" "$evidence/bundle/hallmark"
run_check pack 0 tar -czf "$evidence/hallmark-linux.tar.gz" -C "$evidence/bundle" hallmark
run_check install 0 tar -xzf "$evidence/hallmark-linux.tar.gz" -C "$evidence/consumer"
runner="$evidence/consumer/hallmark"
sha256sum -- "$evidence/hallmark-linux.tar.gz" "$runner" > "$evidence/distribution.sha256"
run_check version 0 "$runner" --version

# 2. Initialize disposable PostgreSQL 18.7 target with Wallet V1 schema and seed rows
install -m 644 -- examples/rust-wallet/migrations/V1__wallet.sql "$evidence/bootstrap/"
install -m 644 -- "$evidence/bootstrap/V1__wallet.sql" examples/rust-wallet/migrations/V2__index_wallet_operations.sql "$evidence/consumer/"
printf 'wallet-service-v0.2.0\n' > "$evidence/consumer/source.txt"
sha256sum -- "$evidence/consumer/"*.sql "$evidence/consumer/source.txt" > "$evidence/fixture-inputs.sha256"

export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
fixture_password=$POSTGRES_PASSWORD
export FLYWAY_PASSWORD="$fixture_password"
run_check database 0 "${runtime[@]}" run -d --pull never --network none --memory 512m \
    --env "POSTGRES_DB=$db_name" --env POSTGRES_USER=fixture --env POSTGRES_PASSWORD "$postgres_image"
read -r database < "$evidence/database.out"

ready=0
for ((attempt = 0; attempt < 100; attempt++)); do
    if "${runtime[@]}" exec "$database" pg_isready -h 127.0.0.1 -U fixture -d "$db_name" > /dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 0.2
done
[[ $ready == 1 ]]

run_check v1 0 "${runtime[@]}" run --rm --pull never --network "container:$database" \
    --cap-drop ALL --security-opt no-new-privileges \
    --mount "type=bind,source=$evidence/bootstrap,target=/flyway/sql,readonly" \
    --env FLYWAY_PASSWORD "$flyway_image" -outputType=json \
    "-url=jdbc:postgresql://127.0.0.1:5432/$db_name" -user=fixture migrate
unset POSTGRES_PASSWORD

psql=("${runtime[@]}" exec "$database" psql -XwqtA -v ON_ERROR_STOP=1 -U fixture -d "$db_name")
run_check seed 0 "${psql[@]}" -c "
BEGIN;
UPDATE wallets SET balance=85 WHERE id='a';
INSERT INTO operations VALUES('retained-debit','a',15,'accepted',85);
INSERT INTO outbox VALUES('retained-debit','debited');
COMMIT;"

wallet_rows="
SELECT json_build_object(
    'wallets', (SELECT json_agg(wallets ORDER BY id) FROM wallets),
    'operations', (SELECT json_agg(operations ORDER BY reference) FROM operations),
    'outbox', (SELECT json_agg(outbox ORDER BY reference) FROM outbox)
);"
run_check before 0 "${psql[@]}" -c "$wallet_rows"

# 3. Author the 6-stage Wallet release workflow manifest in the clean consumer directory
cd -- "$evidence/consumer"
jq -n --arg id "$database" --arg name "$db_name" --arg engine "$engine" '{
  schemaVersion: 1,
  name: "wallet-release",
  context: "wallet-release",
  inputs: ["source.txt", "V1__wallet.sql", "V2__index_wallet_operations.sql"],
  allowedExecutables: ["/bin/sh"],
  steps: [
    {
      id: "build",
      kind: "Build",
      effect: "Imperative",
      repeat: "ExecuteOnce",
      needs: [],
      operation: {
        type: "process",
        executable: "/bin/sh",
        arguments: ["-eu", "-c", "mkdir -p build && tr \"[:lower:]\" \"[:upper:]\" < source.txt > build/wallet-service.txt"]
      },
      outputs: ["build/wallet-service.txt"],
      retrySafety: "manual"
    },
    {
      id: "artifact",
      kind: "Build",
      effect: "Observation",
      repeat: "Reconcile",
      needs: ["build"],
      operation: {
        type: "capture",
        path: "build/wallet-service.txt"
      },
      outputs: [],
      retrySafety: "reconcile"
    },
    {
      id: "install",
      kind: "Install",
      effect: "DesiredState",
      repeat: "Reconcile",
      needs: ["artifact"],
      operation: {
        type: "write-file",
        path: "installed.txt",
        content: "{{artifact.digest}}\n"
      },
      outputs: [],
      retrySafety: "reconcile"
    },
    {
      id: "configure",
      kind: "Configure",
      effect: "DesiredState",
      repeat: "Reconcile",
      needs: ["install"],
      operation: {
        type: "write-file",
        path: "service.json",
        content: ("{\"artifact\":\"{{artifact.digest}}\",\"database\":\"" + $name + "\"}\n")
      },
      outputs: [],
      retrySafety: "reconcile"
    },
    {
      id: "migrate",
      kind: "Migrate",
      effect: "Imperative",
      repeat: "ExecuteOnce",
      needs: ["configure"],
      operation: {
        type: "postgres-migrate",
        containerEngine: $engine,
        containerId: $id,
        database: $name,
        user: "fixture",
        migrations: ["V1__wallet.sql", "V2__index_wallet_operations.sql"]
      },
      outputs: ["wallet-ledger.json"],
      retrySafety: "external-ledger"
    },
    {
      id: "verify",
      kind: "Verify",
      effect: "Observation",
      repeat: "Reconcile",
      needs: ["migrate"],
      operation: {
        type: "process",
        executable: "/bin/sh",
        arguments: ["-eu", "-c", "test -s wallet-ledger.json && test \"$(cat build/wallet-service.txt)\" = \"WALLET-SERVICE-V0.2.0\""]
      },
      outputs: [],
      retrySafety: "reconcile"
    },
    {
      id: "release",
      kind: "Release",
      effect: "Imperative",
      repeat: "ExecuteOnce",
      needs: ["verify"],
      operation: {
        type: "release",
        version: "0.2.0"
      },
      outputs: [],
      retrySafety: "external-ledger"
    }
  ]
}' > wallet-workflow.json

# 4. Exercise rootless Podman worker launch contract (HALLMARK_WORKER_IMAGE) on host manifest (CLI and live interactive MCP)
mkdir -m 700 -- "$evidence/podman-plan-state" "$evidence/consumer/state"
mkdir -m 755 -- "$evidence/freeze-out"
run_check podman_worker_plan 0 env HALLMARK_WORKER_IMAGE="$worker_image" \
    "$runner" engine plan "$evidence/consumer/wallet-workflow.json" --state-dir "$evidence/podman-plan-state"
jq -e '.evidenceStage == "Preview" and (.stages | length == 7)' "$evidence/podman_worker_plan.out" > /dev/null

run_check podman_worker_freeze 0 bash -c 'umask 022 && HALLMARK_WORKER_IMAGE="$1" "$2" engine freeze "$3" "$4" --state-dir "$5"' _ \
    "$worker_image" "$runner" "$evidence/consumer/wallet-workflow.json" "$evidence/freeze-out/frozen.json" "$evidence/podman-plan-state"
[[ $(stat -c '%a' "$evidence/freeze-out") == 755 ]]
while IFS= read -r entry; do
    mode=$(stat -c '%a' "$entry")
    if [[ -d $entry ]]; then
        [[ $mode == 700 ]]
    else
        [[ $mode == 600 ]]
    fi
done < <(find "$evidence/podman-plan-state")

run_check podman_worker_mcp 0 python3 - "$worker_image" "$runner" "$evidence/podman-plan-state" "$evidence/consumer/wallet-workflow.json" <<'PY'
import json, os, subprocess, sys
worker_image, runner, state_dir, manifest = sys.argv[1:5]
env = os.environ.copy()
env["HALLMARK_WORKER_IMAGE"] = worker_image
proc = subprocess.Popen(
    [runner, "mcp", "--state-dir", state_dir],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True,
    bufsize=1,
    env=env,
)
init_req = {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "wallet-drill", "version": "0.2.0"}}}
proc.stdin.write(json.dumps(init_req) + "\n")
proc.stdin.flush()
init_line = proc.stdout.readline()
assert init_line, "Missing initialize response while stdin open"
proc.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
plan_req = {"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": "hallmark_plan", "arguments": {"manifest": manifest}}}
proc.stdin.write(json.dumps(plan_req) + "\n")
proc.stdin.flush()
plan_line = proc.stdout.readline()
assert plan_line, "Missing hallmark_plan response while stdin open"
proc.stdin.close()
proc.wait(timeout=30)
print(init_line.strip())
print(plan_line.strip())
PY
jq -es --slurpfile cli "$evidence/podman_worker_plan.out" --slurpfile frozen "$evidence/freeze-out/frozen.json" '
  [.[] | select(.id == 2)] | .[0].result |
  .isError == false and
  .structuredContent.planDigest == $cli[0].planDigest and
  .structuredContent.planDigest == $frozen[0].planDigest and
  .structuredContent.sourceDigest == $frozen[0].sourceDigest and
  .structuredContent.policyDigest == $frozen[0].policyDigest and
  .structuredContent.targetDigest == $frozen[0].targetDigest
' "$evidence/podman_worker_mcp.out" > /dev/null

# 5. Intercept Flyway migration response to simulate process kill after PostgreSQL commit
ln -s -- "$runtime_cli" "$evidence/adapters/real-runtime"
cat > "$evidence/adapters/$engine" <<'WRAPPER'
#!/usr/bin/bash
set -euo pipefail
adapters=$(cd -- "$(dirname -- "$0")" && pwd)
proof=$(dirname -- "$adapters")
case "${1-}" in
    --host) action=${3-} ;;
    --remote=false) action=${2-} ;;
    *) action=${1-} ;;
esac
if [[ $action == run && ${*: -1} == migrate ]]; then
    printf 'dispatch\n' >> "$proof/dispatches.txt"
    "$adapters/real-runtime" "$@" > "$proof/flyway-response.json"
    if [[ -e "$proof/interrupt" ]]; then
        cp -- "$proof/flyway-response.json" "$proof/flyway-before-interruption.json"
        touch -- "$proof/committed"
        while [[ ! -e "$proof/return-response" ]]; do
            sleep 0.1
        done
    fi
    cat -- "$proof/flyway-response.json"
else
    exec "$adapters/real-runtime" "$@"
fi
WRAPPER
chmod 755 -- "$evidence/adapters/$engine"
export PATH="$evidence/adapters:$PATH"
export HALLMARK_WORKER_DLL="$evidence/worker-build/worker/Hallmark.Cli.dll"

head_commit=$(git -C "$repo" rev-parse HEAD)
head_tree=$(git -C "$repo" rev-parse 'HEAD^{tree}')
policy_compact=$(jq -c '
  .enabledProfiles |= sort |
  .approvedBlockingRules |= sort |
  .advisoryRules |= sort |
  .experimentalRules |= sort |
  .prototypeRules |= sort |
  .dummyRules |= sort |
  .deprecatedRules |= sort |
  .removedRules |= sort |
  .requiredProjectClasses |= sort |
  .requiredTargetFrameworks |= sort |
  .requiredTests |= sort_by(.id, .project)
' "$repo/fsassay-policy.lock.json")
policy_sha=$(printf '%s' "$policy_compact" | sha256sum | awk '{print $1}')

cp -- "$repo/examples/three-owner/fixture.json" "$evidence/consumer/three-owner.json"
expected_producers=$(jq -cn --arg commit "$head_commit" --arg tree "$head_tree" --arg policy "$policy_sha" '[
  {assay:"FsAssay",tool:"FsAssay",toolVersion:"1.0.4",commitSha:$commit,treeSha:$tree,policySha256:$policy},
  {assay:"PostgresAssay",tool:"PostgresAssay",toolVersion:"1.0.0"}
]')
jq -n \
  --argjson producers "$expected_producers" \
  --arg owner_path "$evidence/consumer/three-owner.json" '{
  requiredAssays: ["FsAssay", "PostgresAssay"],
  expectedProducers: $producers,
  lineage: "Active",
  threeOwnerFixture: $owner_path,
  receipts: []
}' > gate-b-policy.json

run_check freeze_plan 0 "$runner" engine freeze wallet-workflow.json frozen-plan.json --state-dir "$evidence/consumer/state"
plan_digest=$(jq -er '.planDigest' frozen-plan.json)
source_digest=$(jq -er '.sourceDigest' frozen-plan.json)
policy_digest=$(jq -er '.policyDigest' frozen-plan.json)
target_digest=$(jq -er '.targetDigest' frozen-plan.json)

touch -- "$evidence/interrupt"
start=("$runner" engine apply wallet-workflow.json --plan-digest "$plan_digest" --state-dir "$evidence/consumer/state")
record_command interrupted "${start[@]}"
"${start[@]}" > "$evidence/interrupted.out" 2> "$evidence/interrupted.err" &
runner_pid=$!
for ((attempt = 0; attempt < 300; attempt++)); do
    if [[ -e "$evidence/committed" ]]; then
        break
    fi
    kill -0 "$runner_pid"
    sleep 0.2
done
[[ -e "$evidence/committed" ]]
pkill -KILL -P "$runner_pid" 2>/dev/null || true
kill -KILL "$runner_pid" 2>/dev/null || true
result=0
wait "$runner_pid" 2>/dev/null || result=$?
runner_pid=
printf '%s\n' "$result" > "$evidence/interrupted.exit"
touch -- "$evidence/return-response"
rm -- "$evidence/interrupt"
unset FLYWAY_PASSWORD

run_check status_after_kill 0 "$runner" engine status --context wallet-release --state-dir "$evidence/consumer/state"
run_id=$(jq -er '.[0].RunId' "$evidence/status_after_kill.out")
jq -e '.[0].Status == "running" and (.[0].Steps | map(select(.Id == "migrate")) | .[0].Status == "running")' "$evidence/status_after_kill.out" > /dev/null

# 6. Conflicting Flyway checksum in PostgreSQL is refused on resume and persisted for CLI and MCP triage
run_check original_checksum 0 "${psql[@]}" -c "SELECT checksum FROM public.flyway_schema_history WHERE version='2'"
checksum=$(cat -- "$evidence/original_checksum.out")
[[ $checksum =~ ^-?[0-9]+$ ]]
run_check tamper_checksum 0 "${psql[@]}" -c "UPDATE public.flyway_schema_history SET checksum=0 WHERE version='2'"
run_check conflicting_ledger 3 "$runner" engine resume wallet-workflow.json --run "$run_id" --state-dir "$evidence/consumer/state"
grep -Fq -- 'Flyway checksum conflicts with the frozen migration' "$evidence/conflicting_ledger.err"

run_check triage_conflict 0 "$runner" engine triage --context wallet-release --state-dir "$evidence/consumer/state"
jq -cn '{
  jsonrpc:"2.0",id:1,method:"initialize",params:{protocolVersion:"2025-11-25",capabilities:{},clientInfo:{name:"wallet-drill",version:"0.2.0"}}
},{
  jsonrpc:"2.0",method:"notifications/initialized"
},{
  jsonrpc:"2.0",id:2,method:"tools/call",params:{name:"hallmark_triage",arguments:{context:"wallet-release"}}
}' > "$evidence/mcp_triage_conflict.request.ndjson"
run_check mcp_triage_conflict 0 bash -c '"$1" mcp --state-dir "$2" < "$3"' _ \
    "$runner" "$evidence/consumer/state" "$evidence/mcp_triage_conflict.request.ndjson"
jq -es --arg run "$run_id" --slurpfile cli "$evidence/triage_conflict.out" '
  [.[] | select(.id == 2)] | .[0].result |
  .isError == false and
  .structuredContent == $cli[0] and
  .structuredContent.runId == $run and
  (.structuredContent.safeNextAction | contains("External ledger conflict detected for [migrate]; do not promote or resume")) and
  any(.structuredContent.unresolvedSteps[];
      .stepId == "migrate" and .status == "conflict" and .runId == $run and
      .version == "2" and .script == "V2__index_wallet_operations.sql" and
      (.systemIdentifier | length > 0) and .databaseOid > 0)
' "$evidence/mcp_triage_conflict.out" > /dev/null

run_check restore_checksum 0 "${psql[@]}" -c "UPDATE public.flyway_schema_history SET checksum=$checksum WHERE version='2'"

# 7. Gate B refusal 1: Missing required PostgresAssay receipt prevents release signing
#    Meanwhile, migrate recovers from PostgreSQL ledger readback with zero additional Flyway dispatches and resolves the conflict!
jq -n \
  --arg commit "$head_commit" \
  --arg tree "$head_tree" \
  --arg policy_sha "$policy_sha" \
  --argjson snapshot "$policy_compact" '{
  schemaVersion: "fsassay-authority-receipt/1.1.0",
  tool: "FsAssay",
  toolVersion: "1.0.4",
  candidate: {
    kind: "commit",
    commitSha: $commit,
    approvedHeadSha: $commit,
    treeSha: $tree,
    dirty: false,
    syntheticMergeSha: "not-applicable",
    packageSha256: "not-applicable",
    repositoryRelativeTarget: "Hallmark.slnx"
  },
  policy: {
    status: "loaded",
    path: "fsassay-policy.lock.json",
    sha256: $policy_sha,
    snapshot: $snapshot,
    error: ""
  },
  toolchain: {
    sdkVersion: "10.0.115",
    runtimeVersion: "10.0.15",
    fsharpCompilerServiceVersion: "43.10.103.0"
  },
  outcome: "Pass",
  authoritative: true,
  reasons: [],
  counts: {
    projectsDiscovered: 2,
    projectsLoaded: 2,
    projectsWorkspaceLoaded: 2,
    projectsSupported: 2,
    projectsFailed: 0,
    projectsSkipped: 0,
    projectsUnsupported: 0,
    eligibleFiles: 1,
    analyzedFiles: 1,
    compilerIncompleteFiles: 0
  },
  projects: [
    {
      path: "src/Hallmark.Core.fsproj",
      projectClass: "other",
      targetFrameworks: ["net10.0"],
      supported: true,
      loaded: true,
      status: "loaded",
      reason: ""
    },
    {
      path: "tests/Hallmark.Tests.fsproj",
      projectClass: "test",
      targetFrameworks: ["net10.0"],
      supported: true,
      loaded: true,
      status: "loaded",
      reason: ""
    }
  ],
  sources: [
    {
      path: "src/Operations.fs",
      disposition: "analyzed",
      reason: ""
    }
  ],
  tests: [
    {
      id: "hallmark-lifecycle-suite",
      project: "tests/Hallmark.Tests.fsproj",
      status: "passed",
      passed: 80,
      failed: 0,
      skipped: 0
    }
  ],
  rules: [],
  findings: [],
  appliedBaselineRecords: [],
  appliedSuppressions: [],
  policyErrors: [],
  evidenceErrors: [],
  missingEvidence: [],
  toolFailures: []
}' > "$evidence/consumer/fsassay-receipt.json"
printf '{"outcome":"Pass","authoritative":true,"producer":"PostgresAssay","version":"1.0.0"}\n' > "$evidence/consumer/pgassay-receipt.json"
fs_receipt="sha256:$(sha256sum "$evidence/consumer/fsassay-receipt.json" | awk '{print $1}')"
pg_receipt="sha256:$(sha256sum "$evidence/consumer/pgassay-receipt.json" | awk '{print $1}')"

jq -n \
  --argjson producers "$expected_producers" \
  --arg owner_path "$evidence/consumer/three-owner.json" \
  --arg plan "$plan_digest" \
  --arg src "$source_digest" \
  --arg pol "$policy_digest" \
  --arg tgt "$target_digest" \
  --arg rec "$fs_receipt" \
  --arg path "$evidence/consumer/fsassay-receipt.json" '{
  requiredAssays: ["FsAssay", "PostgresAssay"],
  expectedProducers: $producers,
  lineage: "Active",
  threeOwnerFixture: $owner_path,
  receipts: [
    {
      assay: "FsAssay",
      outcome: "Pass",
      receiptDigest: $rec,
      receiptPath: $path,
      planDigest: $plan,
      sourceDigest: $src,
      policyDigest: $pol,
      targetDigest: $tgt
    }
  ]
}' > gate-b-policy.json

run_check resume_missing_receipt 1 "$runner" engine resume wallet-workflow.json --run "$run_id" --state-dir "$evidence/consumer/state"
jq -e '(.Steps | map(select(.Id == "migrate")) | .[0].Status == "succeeded" and .[0].Message == "reconciled-from-external-ledger")
   and (.Steps | map(select(.Id == "release")) | .[0].Status == "failed" and (.[0].Message | contains("MissingRequiredAssays")))' \
   "$evidence/resume_missing_receipt.out" > /dev/null
[[ ! -e "$evidence/consumer/state/wallet-release/releases/0.2.0.json" ]]
[[ $(wc -l < "$evidence/dispatches.txt") == 1 ]]

# 8. Gate B refusal 2: Mismatched targetDigest binding on PostgresAssay prevents release signing
jq -n \
  --argjson producers "$expected_producers" \
  --arg owner_path "$evidence/consumer/three-owner.json" \
  --arg plan "$plan_digest" \
  --arg src "$source_digest" \
  --arg pol "$policy_digest" \
  --arg tgt "$target_digest" \
  --arg fs_rec "$fs_receipt" \
  --arg pg_rec "$pg_receipt" \
  --arg fs_path "$evidence/consumer/fsassay-receipt.json" \
  --arg pg_path "$evidence/consumer/pgassay-receipt.json" '{
  requiredAssays: ["FsAssay", "PostgresAssay"],
  expectedProducers: $producers,
  lineage: "Active",
  threeOwnerFixture: $owner_path,
  receipts: [
    {
      assay: "FsAssay",
      outcome: "Pass",
      receiptDigest: $fs_rec,
      receiptPath: $fs_path,
      planDigest: $plan,
      sourceDigest: $src,
      policyDigest: $pol,
      targetDigest: $tgt
    },
    {
      assay: "PostgresAssay",
      outcome: "Pass",
      receiptDigest: $pg_rec,
      receiptPath: $pg_path,
      planDigest: $plan,
      sourceDigest: $src,
      policyDigest: $pol,
      targetDigest: "sha256:0000000000000000000000000000000000000000000000000000000000000000"
    }
  ]
}' > gate-b-policy.json

run_check resume_mismatched_receipt 1 "$runner" engine resume wallet-workflow.json --run "$run_id" --state-dir "$evidence/consumer/state"
jq -e '(.Steps | map(select(.Id == "release")) | .[0].Status == "failed" and (.[0].Message | contains("MismatchedReceiptIdentity")))' \
   "$evidence/resume_mismatched_receipt.out" > /dev/null
[[ ! -e "$evidence/consumer/state/wallet-release/releases/0.2.0.json" ]]
[[ $(wc -l < "$evidence/dispatches.txt") == 1 ]]

# 9. Valid bound Gate B receipts (with raw receipt files & threeOwnerFixture) admit release signing without re-dispatching Flyway
jq -n \
  --argjson producers "$expected_producers" \
  --arg plan "$plan_digest" \
  --arg src "$source_digest" \
  --arg pol "$policy_digest" \
  --arg tgt "$target_digest" \
  --arg fs_rec "$fs_receipt" \
  --arg pg_rec "$pg_receipt" \
  --arg fs_path "$evidence/consumer/fsassay-receipt.json" \
  --arg pg_path "$evidence/consumer/pgassay-receipt.json" \
  --arg owner_path "$evidence/consumer/three-owner.json" '{
  requiredAssays: ["FsAssay", "PostgresAssay"],
  expectedProducers: $producers,
  lineage: "Active",
  threeOwnerFixture: $owner_path,
  receipts: [
    {
      assay: "FsAssay",
      outcome: "Pass",
      receiptDigest: $fs_rec,
      receiptPath: $fs_path,
      planDigest: $plan,
      sourceDigest: $src,
      policyDigest: $pol,
      targetDigest: $tgt
    },
    {
      assay: "PostgresAssay",
      outcome: "Pass",
      receiptDigest: $pg_rec,
      receiptPath: $pg_path,
      planDigest: $plan,
      sourceDigest: $src,
      policyDigest: $pol,
      targetDigest: $tgt
    }
  ]
}' > gate-b-policy.json

run_check resume_admitted 0 "$runner" engine resume wallet-workflow.json --run "$run_id" --state-dir "$evidence/consumer/state"
jq -e '.Status == "succeeded" and (.Steps | length == 7) and (.Steps | map(select(.Id == "release")) | .[0].Status == "succeeded")' \
   "$evidence/resume_admitted.out" > /dev/null
[[ -e "$evidence/consumer/state/wallet-release/releases/0.2.0.json" ]]
[[ $(wc -l < "$evidence/dispatches.txt") == 1 ]]

# 9a. Repeated convergence (resume -> converge -> converge) reuses the same signed release and does not re-dispatch Flyway
run_check converge_first 0 "$runner" engine converge wallet-workflow.json --run "$run_id" --state-dir "$evidence/consumer/state"
converge_1_id=$(jq -er '.RunId' "$evidence/converge_first.out")
run_check converge_second 0 "$runner" engine converge wallet-workflow.json --run "$converge_1_id" --state-dir "$evidence/consumer/state"
[[ $(wc -l < "$evidence/dispatches.txt") == 1 ]]

# Verify PostgreSQL data integrity and V2 index
run_check after 0 "${psql[@]}" -c "$wallet_rows"
cmp -s -- "$evidence/before.out" "$evidence/after.out"
run_check database_result 0 "${psql[@]}" -c "
SELECT count(*) FROM public.flyway_schema_history WHERE version='2' AND success;
SELECT count(*) FROM pg_indexes WHERE schemaname='public' AND indexname='operations_wallet_idx';"
[[ $(cat -- "$evidence/database_result.out") == $'1\n1' ]]

# 10. Create deterministic signed bundle and verify from two independent consumers
run_check public_key 0 "$runner" engine public-key --context wallet-release --state-dir "$evidence/consumer/state"
cp -- "$evidence/public_key.out" "$evidence/independent-consumer/public-key.pem"

# 10a. Post-seal approval withdrawal refuses promotion and convergence until re-authorized
jq '.corrections = [{"correctionId":"VERA-CORR-POST-SEAL","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Post-seal compatibility withdrawal","effectiveAt":"2026-11-25T00:00:00Z","kind":"Withdrawn"}]' \
    "$repo/examples/three-owner/fixture.json" > "$evidence/consumer/three-owner.json"
run_check promote_withdrawn 3 "$runner" engine promote 0.2.0 --channel prod --context wallet-release \
    --public-key "$evidence/independent-consumer/public-key.pem" --state-dir "$evidence/consumer/state"
[[ ! -e "$evidence/consumer/state/wallet-release/channels/prod.json" ]]
cp -- "$repo/examples/three-owner/fixture.json" "$evidence/consumer/three-owner.json"
run_check promote_admitted 0 "$runner" engine promote 0.2.0 --channel prod --context wallet-release \
    --public-key "$evidence/independent-consumer/public-key.pem" --state-dir "$evidence/consumer/state"
[[ -s "$evidence/consumer/state/wallet-release/channels/prod.json" ]]

run_check create_bundle 0 "$runner" engine bundle "$evidence/independent-consumer/wallet-release.bundle.zip" \
    --context wallet-release --run "$run_id" --public-key "$evidence/independent-consumer/public-key.pem" \
    --state-dir "$evidence/consumer/state"

# Independent Consumer A: Native Rust Hallmark CLI verifier (zero .NET dependency)
cd -- "$evidence/independent-consumer"
run_check rust_bundle_verify 0 "$runner" bundle-verify \
    "$evidence/independent-consumer/wallet-release.bundle.zip" \
    --public-key "$evidence/independent-consumer/public-key.pem"
jq -e '.valid == true and .verifier == "hallmark-cli-rust" and .name == "wallet-release" and .version == "0.2.0" and .artifactCount == 8' \
   "$evidence/rust_bundle_verify.out" > /dev/null

# Independent Consumer B: Isolated rootless Podman container using only the bundle and public key
unset HALLMARK_WORKER_DLL
run_check container_bundle_verify 0 env HALLMARK_WORKER_IMAGE="$worker_image" \
    "$runner" engine bundle-verify \
    "$evidence/independent-consumer/wallet-release.bundle.zip" \
    --public-key "$evidence/independent-consumer/public-key.pem"
jq -e '.valid == true and .name == "wallet-release" and .version == "0.2.0" and .artifactCount == 8' \
   "$evidence/container_bundle_verify.out" > /dev/null

printf 'Wallet six-stage lifecycle recovery and Gate B release drill passed. Evidence is in %s\n' "$evidence"
