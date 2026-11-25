#!/usr/bin/env bash
set -euo pipefail
umask 077

if (($# < 2)); then
    printf 'Usage: %s NEW_EVIDENCE_DIR inventory|wallet|duckdb|stream-index|swift|android [...]\n' "$0" >&2
    exit 64
fi

repo=$(cd -- "$(dirname -- "$0")/.." && pwd)
portfolio="$repo/examples/portfolio"
destination=$(realpath -m -- "$1")
shift
case "$destination/" in
    "$repo/"*) printf 'Choose an evidence directory outside the checkout\n' >&2; exit 64 ;;
esac
mkdir -m 700 -- "$destination"

runner=$(realpath -- "${PORTFOLIO_HALLMARK:?Set PORTFOLIO_HALLMARK to the built Rust CLI}")
export HALLMARK_WORKER_DLL
HALLMARK_WORKER_DLL=$(realpath -- "${HALLMARK_WORKER_DLL:?Set HALLMARK_WORKER_DLL to the published Hallmark.Cli.dll}")
unset HALLMARK_WORKER_IMAGE HALLMARK_BANQUET_IMAGE HALLMARK_BANQUET_DLL
podman=(podman --remote=false)
postgres_image=docker.io/library/postgres:18.7@sha256:2ea8630df176ad090ec92136dee850397c5af8781e4bcf2e4162593691c61bbc
flyway_image=docker.io/flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657

git -C "$repo" rev-parse HEAD 'HEAD^{tree}' > "$destination/hallmark-source.txt"
git -C "$repo" status --porcelain > "$destination/hallmark-status.txt"
sha256sum -- "$runner" "$HALLMARK_WORKER_DLL" > "$destination/hallmark-binaries.sha256"
"${podman[@]}" info --format '{{.Host.Security.Rootless}}' | grep -Fxq true
"$runner" --version > "$destination/hallmark-version.txt"

image_identity() {
    local id
    id=$("${podman[@]}" image inspect --format '{{.Id}}' "$1")
    [[ $id =~ ^(sha256:)?[a-f0-9]{64}$ ]]
    printf 'sha256:%s' "${id#sha256:}"
}

record_expected() {
    local name=$1 expected=$2
    shift 2
    local result=0
    printf '%q ' "$@" > "$evidence/$name.command"
    printf '\n' >> "$evidence/$name.command"
    "$@" > "$evidence/$name.out" 2> "$evidence/$name.err" || result=$?
    printf '%s\n' "$result" > "$evidence/$name.exit"
    if ((result != expected)); then
        printf '%s/%s exited %s, expected %s. See %s\n' "$sample" "$name" "$result" "$expected" "$evidence" >&2
        cat -- "$evidence/$name.err" >&2
        if ((result == 0)); then return 1; fi
        return "$result"
    fi
}

record() {
    record_expected "$1" 0 "${@:2}"
}

source "$portfolio/operator.sh"

prepare_dotnet() {
    local source revision packages database_name dotnet
    local -a members
    dotnet=$(realpath -- "${PORTFOLIO_DOTNET11:?Set PORTFOLIO_DOTNET11 to the .NET 11.0.100 dotnet executable}")
    [[ $("$dotnet" --version) == 11.0.100 ]]
    if [[ $sample == inventory ]]; then
        source="${PORTFOLIO_CSHARP:?Set PORTFOLIO_CSHARP to the CSharpAssay.Playground checkout}"
        revision=2c6697b940a3875a04cca46163dc5dd4b9f4e1f1
        packages="${PORTFOLIO_CS_PACKAGES:?Set PORTFOLIO_CS_PACKAGES to the original package cache}"
        members=(LICENSE global.json NuGet.config Directory.Build.props Directory.Build.targets Directory.Packages.props
                 Inventory.slnx .csassay.inventory.json tests/Directory.Build.props samples/80-inventory-reservation)
        database_name=inventory_portfolio
    else
        source="${PORTFOLIO_FSHARP:?Set PORTFOLIO_FSHARP to the FSharpAssay.Playground checkout}"
        revision=37fec1c7a17cf4e8ceb9a76b7f956151b1e6e28b
        packages="${PORTFOLIO_FS_PACKAGES:?Set PORTFOLIO_FS_PACKAGES to the original package cache}"
        members=(LICENSE NOTICE global.json NuGet.Config Directory.Build.props samples/40-wallet tests/Wallet.Tests)
        database_name=fsplayground_portfolio
    fi
    packages=$(realpath -- "$packages")
    git -C "$source" archive --format=tar.gz "$revision" "${members[@]}" > app-source.tar.gz
    cp -- "$portfolio/dotnet/run.sh" "$portfolio/dotnet/$sample/"*.sql fixture/
    git -C "$source" rev-parse "$revision" "$revision^{tree}" > "$evidence/application-source.txt"
    sha256sum -- "$dotnet" app-source.tar.gz >> "$evidence/application-source.txt"

    image_identity "$flyway_image" > "$evidence/flyway-image.txt"
    local postgres_id
    postgres_id=$(image_identity "$postgres_image")
    local password token database
    password=$(openssl rand -hex 24)
    database=$(POSTGRES_PASSWORD="$password" "${podman[@]}" create --pull never --label "$label" \
        --memory 768m --publish 127.0.0.1::5432 \
        --env "POSTGRES_DB=$database_name" --env POSTGRES_USER=fixture --env POSTGRES_PASSWORD "$postgres_id")
    printf '%s' "$password" | record database-secret "${hm[@]}" secret-set database-password --context "$sample" --state-dir "$state"
    jq -n --arg sample "$sample" --arg db "$database" --arg name "$database_name" \
        --arg dotnet "$dotnet" --arg packages "$packages" \
        --arg postgres "$postgres_id" --arg flyway "$flyway_image" --arg revision "$revision" \
        --arg sdkHash "$(sha256sum "$dotnet" | cut -d ' ' -f1)" \
        '{sample:$sample, revision:$revision, database:{container:$db,name:$name,image:$postgres,flyway:$flyway},
          toolchain:{dotnet:$dotnet,version:"11.0.100",dotnetSha256:$sdkHash,packages:$packages}}' > target.json
    inputs='["app-source.tar.gz"]'
    environment='{"DATABASE_PASSWORD":{"secret":"database-password"}}'
    if [[ $sample == wallet ]]; then
        wallet_assay_password=$password
        token=$(openssl rand -hex 24)
        printf '%s' "$token" | record application-secret "${hm[@]}" secret-set application-token --context "$sample" --state-dir "$state"
        environment=$(jq -c '.APPLICATION_TOKEN = {secret:"application-token"}' <<< "$environment")
    fi
}

prepare_duckdb() {
    local executable
    executable=$(realpath -- "${PORTFOLIO_DUCKDB:?Set PORTFOLIO_DUCKDB to the DuckDB 2.0 CLI}")
    local version
    version=$("$executable" -no-agent -batch --version < /dev/null)
    [[ $version == v2.0.0\ * ]]
    jq -n --arg sample "$sample" --arg cli "$executable" --arg version "$version" \
        --arg digest "$(sha256sum "$executable" | cut -d ' ' -f1)" \
        '{sample:$sample, duckdb:{executable:$cli,version:$version,sha256:$digest}}' > target.json
}

prepare_stream() {
    local kafka_image search_image kafka search
    kafka_image=$(image_identity "${PORTFOLIO_KAFKA_IMAGE:-docker.io/apache/kafka:4.3.0}")
    search_image=$(image_identity "${PORTFOLIO_OPENSEARCH_IMAGE:-docker.io/opensearchproject/opensearch:3.9.0}")
    kafka=$("${podman[@]}" create --pull never --label "$label" --network none --memory 1536m \
        --env KAFKA_HEAP_OPTS='-Xms384m -Xmx384m' "$kafka_image")
    search=$("${podman[@]}" create --pull never --label "$label" --network none --memory 2g \
        --env discovery.type=single-node --env OPENSEARCH_JAVA_OPTS='-Xms512m -Xmx512m' \
        --env DISABLE_INSTALL_DEMO_CONFIG=true --env DISABLE_SECURITY_PLUGIN=true "$search_image")
    jq -n --arg sample "$sample" --arg kafka "$kafka" --arg kafkaImage "$kafka_image" \
        --arg search "$search" --arg searchImage "$search_image" \
        '{sample:$sample, kafka:{container:$kafka,image:$kafkaImage}, search:{container:$search,image:$searchImage}}' > target.json
}

prepare_swift() {
    local image
    image=$(image_identity "${PORTFOLIO_SWIFT_IMAGE:-docker.io/library/swift:6.4.0-noble}")
    jq -n --arg sample "$sample" --arg image "$image" '{sample:$sample,swift:{image:$image}}' > target.json
}

prepare_android() {
    local sdk serial sdk_root kotlin build_tools=37.0.0
    sdk_root="${PORTFOLIO_ANDROID_SDK:?Set PORTFOLIO_ANDROID_SDK to the Android SDK root}"
    sdk=$(realpath -- "$sdk_root")
    kotlin=$(realpath -- "${PORTFOLIO_KOTLINC:?Set PORTFOLIO_KOTLINC to the installed kotlinc executable}")
    serial="${PORTFOLIO_ANDROID_SERIAL:?Set PORTFOLIO_ANDROID_SERIAL to the disposable emulator serial}"
    [[ $serial =~ ^emulator-[0-9]+$ ]]
    local adb="$sdk/platform-tools/adb"
    [[ $("$adb" -s "$serial" shell getprop ro.kernel.qemu | tr -d '\r') == 1 ]]
    [[ $("$adb" -s "$serial" shell getprop sys.boot_completed | tr -d '\r') == 1 ]]
    jq -n --arg sample "$sample" --arg sdk "$sdk" --arg serial "$serial" --arg kotlin "$kotlin" \
        --arg buildTools "$build_tools" --arg kotlinVersion "$("$kotlin" -version 2>&1)" \
        --arg dexVersion "$("$sdk/build-tools/$build_tools/d8" --version)" \
        --arg fingerprint "$("$adb" -s "$serial" shell getprop ro.build.fingerprint | tr -d '\r')" \
        '{sample:$sample,android:{sdk:$sdk,serial:$serial,kotlin:$kotlin,kotlinVersion:$kotlinVersion,
          buildTools:$buildTools,dexVersion:$dexVersion,platform:"android-35",fingerprint:$fingerprint}}' > target.json
}

install_consumer_package() {
    local bundle=$1 consumer=$2 digest=$3
    [[ $digest =~ ^[a-f0-9]{64}$ ]] || return 1
    unzip -p "$bundle" "artifacts/$digest" > "$consumer/distribution.tar.gz" || return
    printf '%s  %s\n' "$digest" "$consumer/distribution.tar.gz" | sha256sum --check - || return
    mkdir -- "$consumer/installed" || return
    tar -xzf "$consumer/distribution.tar.gz" --no-same-owner -C "$consumer/installed"
}

run_sample() (
    sample=$1
    case "$sample" in
        inventory|wallet|duckdb|stream-index|swift|android) ;;
        *) printf 'Unknown portfolio sample: %s\n' "$sample" >&2; exit 64 ;;
    esac

    local directory="$destination/$sample"
    local wallet_assay_password=
    mkdir -- "$directory"
    evidence="$directory"
    state="$directory/state"
    mkdir -- "$state" "$directory/target" "$directory/consumer"
    cd -- "$directory/target"
    mkdir -- fixture
    cp -- "$portfolio/common.sh" fixture/
    if [[ $sample != inventory && $sample != wallet ]]; then
        cp -- "$portfolio/$sample/"* fixture/
    fi

    label="io.canonflow.hallmark.portfolio=$(openssl rand -hex 16)"
    local containers
    cleanup() {
        local result=$?
        trap - EXIT
        if ! containers=$("${podman[@]}" ps -aq --filter "label=$label"); then
            exit 1
        fi
        if [[ -n $containers ]]; then
            local -a ids
            mapfile -t ids <<< "$containers"
            local id
            for id in "${ids[@]}"; do
                "${podman[@]}" logs "$id" > "$evidence/container-$id.log" 2>&1 || true
            done
            if ! "${podman[@]}" rm --force --volumes "${ids[@]}" > "$evidence/cleanup.out" 2> "$evidence/cleanup.err"; then
                printf 'Owned container cleanup failed. See %s\n' "$evidence/cleanup.err" >&2
                exit 1
            fi
        fi
        exit "$result"
    }
    trap cleanup EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM

    hm=(timeout --kill-after=15s 22m "$runner" engine)
    inputs='[]'
    environment='{}'
    case "$sample" in
        inventory|wallet) prepare_dotnet ;;
        duckdb) prepare_duckdb ;;
        stream-index) prepare_stream ;;
        swift) prepare_swift ;;
        android) prepare_android ;;
    esac
    jq -n --arg sample "$sample" --argjson inputs "$inputs" --argjson env "$environment" \
        --argjson target "$(cat target.json)" -f "$portfolio/workflow.jq" > "$evidence/workflow.request.json"
    record emit "${hm[@]}" emit-json "$evidence/workflow.request.json"
    cp -- "$evidence/emit.out" workflow.json
    record reemit "${hm[@]}" emit-json "$PWD/workflow.json"
    cmp -- workflow.json "$evidence/reemit.out"
    cp -- workflow.json "$evidence/workflow.json"

    if [[ $sample == wallet ]]; then
        prepare_wallet_gate
    fi
    record plan "${hm[@]}" plan "$PWD/workflow.json" --state-dir "$state"
    local digest run converged converged_repeat completed
    digest=$(jq -er '.planDigest' "$evidence/plan.out")
    mcp_preview
    completed="$evidence/apply.out"
    if [[ $sample == wallet ]]; then
        wallet_hold_and_resume "$digest"
        completed="$evidence/resume.out"
    else
        record apply "${hm[@]}" apply "$PWD/workflow.json" --plan-digest "$digest" --state-dir "$state"
    fi
    jq -e '.Status == "succeeded" and all(.Steps[]; .Status == "succeeded")' "$completed" > /dev/null
    run=$(jq -er .RunId "$completed")

    record converge "${hm[@]}" converge "$PWD/workflow.json" --run "$run" --state-dir "$state"
    converged=$(jq -er '.RunId' "$evidence/converge.out")
    jq -e --slurpfile first "$completed" --slurpfile definition workflow.json '
        def starts: .Steps | map({key:.Id,value:.Started}) | from_entries;
        ($first[0] | starts) as $before | starts as $after |
        .Status == "succeeded" and .RunId != $first[0].RunId and
        all($definition[0].steps[];
            ($after[.id] != null) and (($before[.id] == $after[.id]) == (.effect == "Imperative")))
        ' "$evidence/converge.out" > /dev/null

    record converge-repeat "${hm[@]}" converge "$PWD/workflow.json" --run "$converged" --state-dir "$state"
    converged_repeat=$(jq -er '.RunId' "$evidence/converge-repeat.out")
    jq -e --slurpfile first "$completed" --slurpfile definition workflow.json '
        def starts: .Steps | map({key:.Id,value:.Started}) | from_entries;
        ($first[0] | starts) as $before | starts as $after |
        .Status == "succeeded" and .RunId != $first[0].RunId and
        all($definition[0].steps[];
            ($after[.id] != null) and (($before[.id] == $after[.id]) == (.effect == "Imperative")))
        ' "$evidence/converge-repeat.out" > /dev/null

    record public-key "${hm[@]}" public-key --context "$sample" --state-dir "$state"
    local public_key="$directory/consumer/public.pem"
    local bundle="$directory/consumer/$sample.zip"
    cp -- "$evidence/public-key.out" "$public_key"
    record bundle "${hm[@]}" bundle "$bundle" --public-key "$public_key" \
        --context "$sample" --run "$run" --state-dir "$state"
    record independent-verify timeout --kill-after=5s 60s "$runner" bundle-verify "$bundle" --public-key "$public_key"
    jq -e '.valid and .artifactCount > 0' "$evidence/independent-verify.out" > /dev/null
    mcp_review_bundle "$bundle" "$public_key" "$run" "$digest"
    record consumer-install install_consumer_package "$bundle" "$directory/consumer" \
        "$(jq -er '.distributionSha256' out/build.json)"
    sha256sum -- "$bundle" "$public_key" > "$evidence/consumer.sha256"
    jq -n --arg case "$sample" --arg run "$run" --arg converge "$converged" --arg plan "$digest" \
        --arg completed "$(basename -- "$completed")" \
        --slurpfile verification "$evidence/independent-verify.out" \
        '{sample:$case, runId:$run, convergenceRunId:$converge, planDigest:$plan,
          completedRunReceipt:$completed, mcpPlan:"matched", mcpBundle:"matched",
          verification:$verification[0], consumerInstall:"completed"}' > "$evidence/result.json"
    printf '%s bundle verified (%s)\n' "$sample" "$run"
)

# Sequential cases keep the heaviest broker and search fixtures from sharing memory.
# A failed case stops this invocation. Individual cases can be rerun in fresh evidence.
for sample in "$@"; do
    run_sample "$sample"
done
