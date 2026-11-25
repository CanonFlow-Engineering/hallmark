#!/usr/bin/env bash
set -euo pipefail
source fixture/common.sh

image=$(jq -er '.swift.image' target.json)
run() {
    "${podman[@]}" run --rm --pull never --timeout 90 --network none \
        --userns keep-id --cap-drop all --security-opt no-new-privileges \
        --read-only --tmpfs /tmp:rw,nosuid,size=96m --env HOME=/tmp \
        --memory 1536m --pids-limit 128 \
        --mount "type=bind,source=$PWD,target=/sample" --workdir /sample \
        --entrypoint "$1" "$image" "${@:2}"
}

case "$stage" in
    build)
        mkdir -p out/distribution
        run /usr/bin/env swiftc -swift-version 6 -O -warnings-as-errors fixture/Receipt.swift -o out/distribution/receipt
        cp -- fixture/v1.json out/distribution/
        pack out/distribution
        ;;
    install)
        install_package
        ;;
    migrate)
        jq -e --arg image "$image" '.swift.image == $image' out/config.json > /dev/null
        run /sample/out/installed/receipt upgrade out/installed/v1.json out/migration.json
        ;;
    verify)
        artifact=$(run /sample/out/installed/receipt check out/migration.json)
        [[ $artifact == demo-asset-0001 ]]
        jq -e '. == {"schemaVersion":2,"artifactId":"demo-asset-0001","status":"passed"}' out/migration.json > /dev/null
        status=0
        run /sample/out/installed/receipt check fixture/unsupported.json > /dev/null 2> out/unsupported.err || status=$?
        [[ $status == 2 ]]
        grep -Fq 'unsupportedSchema(3)' out/unsupported.err
        jq -n --slurpfile receipt out/migration.json --argjson refusal "$status" \
            '{receipt:$receipt[0],unsupportedSchemaExit:$refusal}' > out/verification.json
        ;;
    *) exit 64 ;;
esac
