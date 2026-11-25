#!/usr/bin/env bash
set -euo pipefail
source fixture/common.sh

sample=$(jq -er '.sample' target.json)
database=$(jq -er '.database.container' target.json)
database_name=$(jq -er '.database.name' target.json)
flyway=$(jq -er '.database.flyway' target.json)
dotnet=$(jq -er '.toolchain.dotnet' target.json)
packages=$(jq -er '.toolchain.packages' target.json)
[[ $(sha256sum "$dotnet" | cut -d ' ' -f1) == "$(jq -er '.toolchain.dotnetSha256' target.json)" ]]
root=$PWD

case "$sample" in
    inventory)
        app=samples/80-inventory-reservation/src/Inventory.Api/Inventory.Api.csproj
        tests=samples/80-inventory-reservation/tests/Inventory.Tests/Inventory.Tests.csproj
        api=Inventory.Api.dll
        test_dll=Inventory.Tests.dll
        tests_minimum=27
        test_report=(--report-xunit-trx --report-xunit-trx-filename tests.trx)
        port=5080
        snapshot='SELECT json_build_object(
          '\''stock'\'', (SELECT available FROM inventory.stock WHERE sku='\''widget'\''),
          '\''legacy'\'', (SELECT quantity FROM inventory.reservations WHERE request_id='\''legacy'\''),
          '\''reservations'\'', (SELECT count(*) FROM inventory.reservations),
          '\''outbox'\'', (SELECT count(*) FROM inventory.outbox),
          '\''upgraded'\'', to_regclass('\''inventory.inventory_outbox_kind_idx'\'') IS NOT NULL
        );'
        ;;
    wallet)
        app=samples/40-wallet/Wallet.Api/Wallet.Api.fsproj
        tests=tests/Wallet.Tests/Wallet.Tests.fsproj
        api=Wallet.Api.dll
        test_dll=Wallet.Tests.dll
        tests_minimum=22
        test_report=(--report-trx --report-trx-filename tests.trx)
        port=5088
        snapshot='SELECT json_build_object(
          '\''balance'\'', (SELECT balance FROM wallets WHERE id='\''a'\''),
          '\''legacy'\'', (SELECT amount FROM operations WHERE reference='\''legacy'\''),
          '\''operations'\'', (SELECT count(*) FROM operations),
          '\''outbox'\'', (SELECT count(*) FROM outbox),
          '\''upgraded'\'', to_regclass('\''public.operations_wallet_idx'\'') IS NOT NULL
        );'
        ;;
    *) exit 64 ;;
esac

psql=("${podman[@]}" exec "$database" psql -XwqtA -v ON_ERROR_STOP=1 -U fixture -d "$database_name")

connection() {
    local mapped
    mapped=$("${podman[@]}" port "$database" 5432/tcp)
    [[ $mapped =~ ^127[.]0[.]0[.]1:([0-9]+)$ ]]
    printf 'Host=127.0.0.1;Port=%s;Database=%s;Username=fixture;Password=%s;SSL Mode=Disable;Timeout=5;Command Timeout=5' \
        "${BASH_REMATCH[1]}" "$1" "$DATABASE_PASSWORD"
}

postgres_baseline() {
    mkdir -p -m 755 out/baseline-sql
    install -m 644 fixture/V1__baseline.sql out/baseline-sql/V1__baseline.sql
    export FLYWAY_PASSWORD=$DATABASE_PASSWORD
    "${podman[@]}" run --rm --timeout 90 --pull never --network "container:$database" \
        --cap-drop=all --security-opt=no-new-privileges \
        --mount "type=bind,source=$root/out/baseline-sql,target=/flyway/sql,readonly" \
        --env FLYWAY_PASSWORD "$flyway" \
        "-url=jdbc:postgresql://127.0.0.1:5432/$database_name" -user=fixture \
        -placeholderReplacement=false -target=1 -outputType=json migrate
    "${psql[@]}" -c "$snapshot" > out/before.json
    jq -e '.legacy > 0 and .outbox == 1 and .upgraded == false' out/before.json > /dev/null
}

test_app() (
    test_database="${database_name}_tests"
    [[ $test_database =~ ^[a-z_]+$ ]]
    # Native suites can reset synthetic test rows. Their database is separate
    # from the deployed database used by the upgrade and HTTP checks.
    "${psql[@]}" -c "CREATE DATABASE $test_database"
    connection_string=$(connection "$test_database")
    export ASSAY_INVENTORY_DATABASE="$connection_string"
    export FSPLAYGROUND_POSTGRES="$connection_string"
    "$dotnet" "out/tests/$test_dll" --minimum-expected-tests "$tests_minimum" \
        "${test_report[@]}" --results-directory "$root/out"
    test -s out/tests.trx
)

http_code() {
    curl --noproxy '*' --silent --show-error --max-time 4 \
        --output "$1" --write-out '%{http_code}' "${@:2}"
}

probe_app() (
    local connection_string
    local endpoint
    local unauthenticated
    connection_string=$(connection "$database_name")
    export ASSAY_INVENTORY_DATABASE="$connection_string"
    export FSPLAYGROUND_POSTGRES="$connection_string"
    export FSPLAYGROUND_TOKEN="${APPLICATION_TOKEN-}"

    if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
        printf 'Fixture HTTP port %s is occupied\n' "$port" >&2
        return 1
    fi
    "$dotnet" "out/installed/$api" > out/app.log 2>&1 &
    server=$!
    stop_app() {
        kill "$server" 2>/dev/null || true
        wait "$server" 2>/dev/null || true
    }
    # End the shell on a signal so EXIT can dispose of its own child.
    trap 'exit 130' INT
    trap 'exit 143' TERM
    trap stop_app EXIT

    endpoint="http://127.0.0.1:$port"
    # Each readiness request is bounded and has an observable protocol response.
    wait_for 40 curl --noproxy '*' --silent --max-time 1 "$endpoint/healthz"
    kill -0 "$server"
    if [[ $sample == inventory ]]; then
        local url="$endpoint/reservations"
        local body='{"requestId":"legacy","sku":"widget","quantity":4}'
        local next='{"requestId":"after-upgrade","sku":"widget","quantity":2}'
        local changed='{"requestId":"legacy","sku":"widget","quantity":3}'
        local json_header='Content-Type: application/json'
        [[ $(http_code out/legacy.json -H "$json_header" --data "$body" "$url") == 200 ]]
        [[ $(http_code out/accepted.json -H "$json_header" --data "$next" "$url") == 201 ]]
        [[ $(http_code out/conflict.json -H "$json_header" --data "$changed" "$url") == 409 ]]
        jq -e '.status == "replayed" and .remaining == 96' out/legacy.json > /dev/null
        jq -e '.status == "reserved" and .remaining == 94' out/accepted.json > /dev/null
        jq -e '.status == "conflict"' out/conflict.json > /dev/null
    else
        local url="$endpoint/debit"
        local body='{"reference":"legacy","wallet":"a","amount":15}'
        local next='{"reference":"after-upgrade","wallet":"a","amount":5}'
        local changed='{"reference":"legacy","wallet":"a","amount":16}'
        local json_header='Content-Type: application/json'
        # Read the private header on stdin so the token stays out of argv.
        authenticated_code() {
            printf 'Authorization: Bearer %s\n' "$APPLICATION_TOKEN" | http_code "$1" -H @- "${@:2}"
        }
        unauthenticated=$(http_code out/unauthorized.json -H "$json_header" --data "$next" "$url")
        [[ $unauthenticated == 401 ]]
        [[ $(authenticated_code out/legacy.json -H "$json_header" --data "$body" "$url") == 200 ]]
        [[ $(authenticated_code out/accepted.json -H "$json_header" --data "$next" "$url") == 200 ]]
        [[ $(authenticated_code out/conflict.json -H "$json_header" --data "$changed" "$url") == 409 ]]
        jq -e '.status == "accepted" and .balance == 85' out/legacy.json > /dev/null
        jq -e '.status == "accepted" and .balance == 80' out/accepted.json > /dev/null
    fi
    jq -n --slurpfile legacy out/legacy.json --slurpfile accepted out/accepted.json \
        --slurpfile conflict out/conflict.json \
        --arg testsSha256 "$(sha256sum out/tests.trx | cut -d ' ' -f1)" \
        '{legacy:$legacy[0], accepted:$accepted[0], conflict:$conflict[0], testsTrxSha256:$testsSha256}' > out/exercise.json
)

case "$stage" in
    build)
        [[ $("$dotnet" --version) == 11.0.100 ]]
        mkdir -p out/source out/distribution out/tests
        tar -xzf app-source.tar.gz -C out/source
        mkdir -p out/source/.packages
        (
            cd out/source
            "$dotnet" restore "$app" --locked-mode --packages "$packages" --nologo
            "$dotnet" publish "$app" -c Release --no-restore --nologo -o "$root/out/distribution"
            "$dotnet" restore "$tests" --locked-mode --packages "$packages" --nologo
            "$dotnet" build "$tests" -c Release --no-restore --nologo -o "$root/out/tests"
        )
        pack out/distribution
        ;;
    install)
        install_package
        start_target "$database"
        wait_for 100 "${podman[@]}" exec "$database" pg_isready -h 127.0.0.1 -U fixture -d "$database_name"
        ;;
    baseline)
        jq -e --arg id "$database" '.database.container == $id' out/config.json > /dev/null
        postgres_baseline
        ;;
    exercise)
        test_app
        # All probe POSTs are explicit fixture effects. A successful release also
        # requires the separate read-only database verification step.
        probe_app
        ;;
    verify)
        "${psql[@]}" -c "$snapshot" > out/after.json
        jq -e '.upgraded and .outbox == 2 and
            (if has("balance") then (.balance == 80 and .legacy == 15 and .operations == 2)
             else (.stock == 94 and .legacy == 4 and .reservations == 2) end)' out/after.json > /dev/null
        jq -n --slurpfile before out/before.json --slurpfile after out/after.json \
            --slurpfile migration out/migration.json \
            '{before:$before[0], after:$after[0], migration:$migration[0]}' > out/verification.json
        ;;
    *) exit 64 ;;
esac
