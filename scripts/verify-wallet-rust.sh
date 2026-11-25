#!/usr/bin/env bash
set -euo pipefail

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
evidence=$(realpath -m -- "${1:?Supply a fresh evidence directory}")
engine=${2:-docker}
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
lifetime_probe=
runner_pid=
cleanup() {
    local result=$?
    if [[ -n $runner_pid ]]; then
        kill -KILL "$runner_pid" 2>/dev/null || true
        wait "$runner_pid" 2>/dev/null || true
    fi
    touch -- "$evidence/return-response"
    if [[ -n $database ]]; then
        "${runtime[@]}" rm --force --volumes "$database" > "$evidence/database-cleanup.out" \
            2> "$evidence/database-cleanup.err" || true
    fi
    if [[ -n $lifetime_probe ]]; then
        "${runtime[@]}" rm --force --volumes "$lifetime_probe" > "$evidence/lifetime-cleanup.out" \
            2> "$evidence/lifetime-cleanup.err" || true
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
git status --short -- rust scripts/verify-wallet-rust.sh examples > "$evidence/source-status.txt"
sha256sum -- "$repo/scripts/verify-wallet-rust.sh" "$runtime_cli" > "$evidence/fixture-tools.sha256"
printf '%s\n' "$engine" > "$evidence/container-engine.txt"
if [[ $engine == podman ]]; then
    run_check rootless 0 "${runtime[@]}" info --format '{{.Host.Security.Rootless}}'
    grep -Fxq -- true "$evidence/rootless.out"
fi
run_check images 0 "${runtime[@]}" image inspect --format '{{.Id}} {{.Os}}/{{.Architecture}}' "$postgres_image" "$flyway_image"
if [[ $engine == podman ]]; then
    run_check lifetime_start 0 timeout --kill-after=2s 15s "${runtime[@]}" run -d --pull never \
        --network none --timeout 2 --entrypoint /bin/sh "$postgres_image" -c 'echo started && exec sleep 30'
    read -r lifetime_probe < "$evidence/lifetime_start.out"
    run_check lifetime_wait 0 timeout --kill-after=2s 15s "${runtime[@]}" wait "$lifetime_probe"
    grep -Eq -- '^-?[1-9][0-9]*$' "$evidence/lifetime_wait.out"
    run_check lifetime_state 0 "${runtime[@]}" container inspect \
        --format '{{.State.Status}} {{.State.OOMKilled}}' "$lifetime_probe"
    grep -Fxq -- 'exited false' "$evidence/lifetime_state.out"
    run_check lifetime_logs 0 "${runtime[@]}" logs "$lifetime_probe"
    grep -Fxq -- started "$evidence/lifetime_logs.out"
fi
cd -- "$repo/rust"
run_check compiler 0 rustc -vV
compiler_pin=$(sed -n 's/^channel = "\([^"]*\)"$/\1/p' rust-toolchain.toml)
grep -Fxq -- "release: $compiler_pin" "$evidence/compiler.out"
export CARGO_TARGET_DIR="$evidence/target"
run_check build 0 cargo build --release --locked --offline --package hallmark-cli
cd -- "$repo"

mkdir -- "$evidence/bundle" "$evidence/consumer" "$evidence/adapters"
mkdir -m 755 -- "$evidence/bootstrap"
install -m 755 -- "$CARGO_TARGET_DIR/release/hallmark" "$evidence/bundle/hallmark"
run_check pack 0 tar -czf "$evidence/hallmark-linux.tar.gz" -C "$evidence/bundle" hallmark
run_check install 0 tar -xzf "$evidence/hallmark-linux.tar.gz" -C "$evidence/consumer"
runner="$evidence/consumer/hallmark"
sha256sum -- "$evidence/hallmark-linux.tar.gz" "$runner" > "$evidence/distribution.sha256"
run_check tools 0 "$runner" --version

install -m 644 -- examples/rust-wallet/migrations/V1__wallet.sql "$evidence/bootstrap/"
install -m 644 -- "$evidence/bootstrap/V1__wallet.sql" examples/rust-wallet/migrations/V2__index_wallet_operations.sql "$evidence/consumer/"
sha256sum -- "$evidence/consumer/"*.sql > "$evidence/fixture-sql.sha256"
export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
fixture_password=$POSTGRES_PASSWORD
export FLYWAY_PASSWORD="$fixture_password"
run_check database 0 "${runtime[@]}" run -d --pull never --network none --memory 512m \
    --env "POSTGRES_DB=$db_name" --env POSTGRES_USER=fixture --env POSTGRES_PASSWORD "$postgres_image"
read -r database < "$evidence/database.out"
run_check database_network 0 "${runtime[@]}" container inspect --format '{{.HostConfig.NetworkMode}}' "$database"
grep -Fxq -- none "$evidence/database_network.out"
run_check published_ports 0 "${runtime[@]}" container inspect --format '{{json .HostConfig.PortBindings}}' "$database"
jq -e '(. // {}) | length == 0' "$evidence/published_ports.out" > /dev/null

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
run_check second_target 0 "${runtime[@]}" exec "$database" psql -XwqtA -v ON_ERROR_STOP=1 -U fixture -d postgres \
    -c "CREATE DATABASE wallet_other TEMPLATE $db_name"
wallet_rows="
SELECT json_build_object(
    'wallets', (SELECT json_agg(wallets ORDER BY id) FROM wallets),
    'operations', (SELECT json_agg(operations ORDER BY reference) FROM operations),
    'outbox', (SELECT json_agg(outbox ORDER BY reference) FROM outbox)
);"
run_check before 0 "${psql[@]}" -c "$wallet_rows"

cd -- "$evidence/consumer"
jq -n --arg id "$database" --arg name "$db_name" --arg engine "$engine" \
    '{schemaVersion:1,context:"wallet-pilot",database:{containerId:$id,name:$name,user:"fixture"},migrations:["V1__wallet.sql","V2__index_wallet_operations.sql"]}
     | if $engine == "podman" then .containerEngine = $engine else . end' \
    > wallet.json
jq '.database.name = "wallet_other"' wallet.json > other.json

ln -s -- "$runtime_cli" "$evidence/adapters/real-runtime"
cat > "$evidence/adapters/$engine" <<'WRAPPER'
#!/usr/bin/bash
set -euo pipefail
adapters=$(cd -- "$(dirname -- "$0")" && pwd)
proof=$(dirname -- "$adapters")
case "${1-}" in
    --host) action=${3-} ;;
    --remote=false) action=${2-} ;;
    *) exit 64 ;;
esac
if [[ $action == info && -e "$proof/rootful-probe" ]]; then
    printf '%s\n' '{"host":{"security":{"rootless":false}},"store":{"graphRoot":"/fixture"}}'
elif [[ $action == run && ${*: -1} == migrate ]]; then
    if [[ -e "$proof/hold-before-dispatch" ]]; then
        exit 41
    fi
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
sha256sum -- "$evidence/adapters/$engine" > "$evidence/fixture-adapter.sha256"
operator_path=$PATH
if [[ $engine == podman ]]; then
    mkdir -- "$evidence/engine-guard"
    cat > "$evidence/engine-guard/docker" <<'GUARD'
#!/usr/bin/bash
touch -- "$(dirname -- "$0")/../docker-unexpected"
exit 77
GUARD
    chmod 755 -- "$evidence/engine-guard/docker"
    operator_path="$evidence/engine-guard:$operator_path"
fi
export PATH="$evidence/adapters:$operator_path"
mkdir -m 700 -- rejected uncertain state other corrupt

run_check plan 0 "$runner" plan wallet.json
jq -e '.admission == "expectedPredecessor" and (.observedLedger | length == 1)' "$evidence/plan.out" > /dev/null
if [[ $engine == podman ]]; then
    jq -e '.inputs.provider | has("podmanSha256") and has("rootlessGraphRoot")' "$evidence/plan.out" > /dev/null
    touch -- "$evidence/rootful-probe"
    run_check rootful_refused 3 "$runner" plan wallet.json
    grep -Fq -- 'requires a local rootless runtime' "$evidence/rootful_refused.err"
    rm -- "$evidence/rootful-probe"
fi
digest=$(jq -er '.planDigest' "$evidence/plan.out")
run_check bad_pin 3 "$runner" apply wallet.json --plan-digest wrong --state-dir rejected
grep -Fq -- 'reviewed plan digest differs' "$evidence/bad_pin.err"
[[ ! -e rejected/run-v1.json ]]

touch -- "$evidence/hold-before-dispatch"
run_check uncertain_apply 3 "$runner" apply wallet.json --plan-digest "$digest" --state-dir uncertain
run_check uncertain_resume 3 "$runner" resume wallet.json --run "$digest" --state-dir uncertain
jq -e '.result.status == "pending"' "$evidence/uncertain_resume.out" > /dev/null
grep -Fq -- 'migration commit is unconfirmed' "$evidence/uncertain_resume.err"
[[ ! -e "$evidence/dispatches.txt" ]]
rm -- "$evidence/hold-before-dispatch"

touch -- "$evidence/interrupt"
start=("$runner" apply wallet.json --plan-digest "$digest" --state-dir state)
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
jq -e '.run.result.status == "pending" and .run.operation.subject.migrationId == "2"' state/run-v1.json > /dev/null
cp -- state/run-v1.json "$evidence/pending-at-interruption.json"
run_check concurrent_status 3 "$runner" status --state-dir state
grep -Fq -- 'state directory is locked' "$evidence/concurrent_status.err"
kill -KILL "$runner_pid"
result=0
wait "$runner_pid" 2>/dev/null || result=$?
runner_pid=
printf '%s\n' "$result" > "$evidence/interrupted.exit"
[[ $result == 137 ]]
touch -- "$evidence/return-response"
rm -- "$evidence/interrupt"
unset FLYWAY_PASSWORD

run_check pending_status 0 "$runner" status --state-dir state
jq -e '.result.status == "pending"' "$evidence/pending_status.out" > /dev/null
run_check wrong_run 3 "$runner" resume wallet.json --run wrong --state-dir state
grep -Fq -- 'run ID differs' "$evidence/wrong_run.err"
if [[ $engine == podman ]]; then
    jq '.containerEngine = "docker"' wallet.json > wrong-engine.json
    run_check wrong_engine 3 "$runner" resume wrong-engine.json --run "$digest" --state-dir state
    grep -Fq -- 'container engine changed' "$evidence/wrong_engine.err"
fi
run_check wrong_target 3 "$runner" resume other.json --run "$digest" --state-dir state
grep -Fq -- 'identity changed' "$evidence/wrong_target.err"

cp -- V2__index_wallet_operations.sql "$evidence/original-v2.sql"
printf '\n' >> V2__index_wallet_operations.sql
run_check wrong_source 3 "$runner" resume wallet.json --run "$digest" --state-dir state
grep -Fq -- 'identity changed' "$evidence/wrong_source.err"
cp -- "$evidence/original-v2.sql" V2__index_wallet_operations.sql

run_check original_checksum 0 "${psql[@]}" -c "SELECT checksum FROM public.flyway_schema_history WHERE version='2'"
checksum=$(cat -- "$evidence/original_checksum.out")
[[ $checksum =~ ^-?[0-9]+$ ]]
run_check change_checksum 0 "${psql[@]}" -c "UPDATE public.flyway_schema_history SET checksum=0 WHERE version='2'"
run_check conflicting_ledger 3 "$runner" resume wallet.json --run "$digest" --state-dir state
grep -Fq -- 'checksum conflicts' "$evidence/conflicting_ledger.err"
run_check restore_checksum 0 "${psql[@]}" -c "UPDATE public.flyway_schema_history SET checksum=$checksum WHERE version='2'"

run_check resume 0 "$runner" resume wallet.json --run "$digest" --state-dir state
jq -e --arg digest "$digest" --arg checksum "$checksum" \
    '.runId == $digest and .result.status == "applied" and (.result.ledger | length == 2) and .operation.expectedChecksum == $checksum' \
    "$evidence/resume.out" > /dev/null
cp -- state/run-v1.json "$evidence/applied-state.json"
run_check duplicate_resume 0 "$runner" resume wallet.json --run "$digest" --state-dir state
cmp -s -- state/run-v1.json "$evidence/applied-state.json"
jq '.run.operation.subject.operationId = "changed"' state/run-v1.json > corrupt/run-v1.json
run_check corrupted_state 3 "$runner" status --state-dir corrupt
grep -Fq -- 'failed its consistency check' "$evidence/corrupted_state.err"
run_check after 0 "${psql[@]}" -c "$wallet_rows"
cmp -s -- "$evidence/before.out" "$evidence/after.out"
run_check database_result 0 "${psql[@]}" -c "
SELECT count(*) FROM public.flyway_schema_history WHERE version='2' AND success;
SELECT count(*) FROM pg_indexes WHERE schemaname='public' AND indexname='operations_wallet_idx';"
[[ $(cat -- "$evidence/database_result.out") == $'1\n1' ]]
[[ $(wc -l < "$evidence/dispatches.txt") == 1 ]]

export PATH="$operator_path"
run_check plan_second 0 "$runner" plan other.json
if [[ $engine == podman ]]; then
    read -r runtime_sha256 _ < <(sha256sum -- "$runtime_cli")
    jq -e --arg digest "$runtime_sha256" '.inputs.provider.podmanSha256 == $digest' \
        "$evidence/plan_second.out" > /dev/null
fi
other_digest=$(jq -er '.planDigest' "$evidence/plan_second.out")
export FLYWAY_PASSWORD="$fixture_password"
run_check normal_apply 0 "$runner" apply other.json --plan-digest "$other_digest" --state-dir other
unset FLYWAY_PASSWORD fixture_password
jq -e '.result.status == "applied"' "$evidence/normal_apply.out" > /dev/null
[[ ! -e "$evidence/docker-unexpected" ]]
printf 'Wallet Rust recovery passed. Evidence is in %s\n' "$evidence"
