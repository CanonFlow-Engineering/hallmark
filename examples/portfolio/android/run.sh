#!/usr/bin/env bash
set -euo pipefail
source fixture/common.sh

sdk=$(jq -er '.android.sdk' target.json)
serial=$(jq -er '.android.serial' target.json)
kotlin=$(jq -er '.android.kotlin' target.json)
tools="$sdk/build-tools/$(jq -er '.android.buildTools' target.json)"
jar="$sdk/platforms/$(jq -er '.android.platform' target.json)/android.jar"
package=org.canonflow.hallmark.receiptdrill
adb=("$sdk/platform-tools/adb" -s "$serial")

device_identity() {
    local current
    current=$("${adb[@]}" shell getprop ro.build.fingerprint | tr -d '\r')
    [[ $current == "$(jq -er '.android.fingerprint' target.json)" ]]
}

read_state() {
    "${adb[@]}" shell run-as "$package" cat files/receipt.json | tr -d '\r'
}

schema_ready() {
    read_state | jq -e --argjson version "$1" '.schema == $version and .artifactId == "mobile-draft-0001"'
}

launch() {
    "${adb[@]}" shell am start -W -n "$package/.ReceiptActivity" | tee out/launch.txt
    grep -Fxq 'Status: ok' <(tr -d '\r' < out/launch.txt)
}

compile_apk() {
    local version=$1
    local temp="$PWD/out/android-$version"
    mkdir -p "$temp/dex"
    printf 'package %s\ninternal const val applicationVersion = %s\n' "$package" "$version" > "$temp/Version.kt"
    "$kotlin" -Werror -jvm-target 1.8 -classpath "$jar" -include-runtime \
        fixture/ReceiptActivity.kt "$temp/Version.kt" -d "$temp/classes.jar"
    "$tools/d8" --min-api 28 --lib "$jar" --output "$temp/dex" "$temp/classes.jar"
    "$tools/aapt2" link -o "$temp/unsigned.apk" -I "$jar" --manifest fixture/AndroidManifest.xml \
        --version-code "$version" --version-name "$version.0"
    zip -jq "$temp/unsigned.apk" "$temp/dex/classes.dex"
    "$tools/zipalign" -f 4 "$temp/unsigned.apk" "$temp/aligned.apk"
    "$tools/apksigner" sign --ks out/local-key.p12 --ks-key-alias hallmark-debug \
        --ks-pass pass:android --key-pass pass:android --out "out/distribution/receipt-v$version.apk" "$temp/aligned.apk"
    "$tools/apksigner" verify --print-certs "out/distribution/receipt-v$version.apk" > "out/cert-v$version.txt"
}

case "$stage" in
    build)
        mkdir -p out/distribution
        keytool -genkeypair -keystore out/local-key.p12 -storetype PKCS12 \
            -storepass android -keypass android -alias hallmark-debug \
            -dname 'CN=Hallmark Local Drill' -keyalg RSA -keysize 2048 -validity 14 -noprompt
        compile_apk 1
        compile_apk 2
        cmp -- out/cert-v1.txt out/cert-v2.txt
        pack out/distribution
        ;;
    install)
        device_identity
        install_package
        existing=$("${adb[@]}" shell pm list packages "$package" | tr -d '\r')
        if [[ -z $existing ]]; then
            "${adb[@]}" install out/installed/receipt-v1.apk
            launch
            wait_for 35 schema_ready 1
            read_state > out/before.json
        else
            test -s out/before.json
        fi
        ;;
    migrate)
        device_identity
        jq -e --arg serial "$serial" '.android.serial == $serial' out/config.json > /dev/null
        "${adb[@]}" install -r out/installed/receipt-v2.apk
        launch
        wait_for 35 schema_ready 2
        before=$(jq -er .artifactId out/before.json)
        read_state > out/migrated-state.json
        jq -e --arg id "$before" '.schema == 2 and .artifactId == $id' out/migrated-state.json > /dev/null
        jq -n --slurpfile before out/before.json --slurpfile after out/migrated-state.json \
            '{before:$before[0],after:$after[0]}' > out/migration.json
        ;;
    verify)
        device_identity
        schema_ready 2 > /dev/null
        version=$("${adb[@]}" shell pm list packages --show-versioncode "$package" | tr -d '\r')
        [[ $version == "package:$package versionCode:2" ]]
        jq -n --arg package "$package" --arg serial "$serial" --slurpfile upgrade out/migration.json \
            '{package:$package,emulator:$serial,versionCode:2,receipt:$upgrade[0]}' > out/verification.json
        ;;
    *) exit 64 ;;
esac
