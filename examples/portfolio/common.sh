# Sourced by the case script from the reviewed fixture directory.
umask 077
podman=(podman --remote=false)
stage=$1

start_target() {
    local id=$1
    if [[ $("${podman[@]}" inspect --format '{{.State.Running}}' "$id") != true ]]; then
        "${podman[@]}" start "$id" > /dev/null
    fi
}

wait_for() {
    local attempts=$1
    shift
    local attempt
    for ((attempt = 0; attempt < attempts; attempt++)); do
        if "$@" > /dev/null 2>&1; then
            return 0
        fi
        sleep 0.25
    done
    printf 'Target failed readiness check for %s\n' "$1" >&2
    return 1
}

pack() {
    tar -czf out/distribution.tar.gz -C "$1" .
    jq -n --slurpfile target target.json \
        --arg digest "$(sha256sum out/distribution.tar.gz | cut -d ' ' -f1)" \
        '{distributionSha256:$digest, target:$target[0]}' > out/build.json
}

install_package() {
    local expected actual
    expected=$(jq -er .distributionSha256 out/build.json)
    actual=$(sha256sum out/distribution.tar.gz | cut -d ' ' -f1)
    [[ $expected == "$actual" ]]
    mkdir -p out/installed
    tar -xzf out/distribution.tar.gz -C out/installed
    jq -n --arg digest "$actual" \
        '{installedFromSha256:$digest}' > out/install.json
}
