#!/usr/bin/env bash
set -euo pipefail

checker=$(realpath -- "${1:?Supply the pinned RustAssay executable}")
producer=${2:?Supply the RustAssay source revision}
evidence=$(realpath -m -- "${3:?Supply a fresh evidence directory}")
project=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

if [[ ! -x $checker || ! $producer =~ ^[a-f0-9]{40}$ ]]; then
    printf 'An executable RustAssay and its full source revision are required.\n' >&2
    exit 64
fi
if ! mkdir -- "$evidence"; then
    printf 'Use a fresh evidence directory with an existing parent.\n' >&2
    exit 64
fi
cd -- "$project/rust"
export CARGO_TARGET_DIR="$evidence/target"

git -C "$project" rev-parse HEAD 'HEAD^{tree}' > "$evidence/source.txt"
git -C "$project" status --short -- rust scripts/verify-rust.sh > "$evidence/source-status.txt"
printf '%s\n' "$producer" > "$evidence/rustassay-source.txt"
sha256sum -- "$checker" > "$evidence/rustassay-binary.sha256"

source_identities() {
    sha256sum -- Cargo.toml cli/Cargo.toml Cargo.lock rust-toolchain.toml ../scripts/verify-rust.sh
    find src tests cli/src -type f -print0 | sort -z | xargs -0 sha256sum --
}

run_check() {
    local label=$1 output=$2 code=0
    shift 2
    printf '%q ' "$@" > "$evidence/$label.command"
    printf '\n' >> "$evidence/$label.command"
    "$@" > "$evidence/$output" 2> "$evidence/$label.stderr" || code=$?
    printf '%s\n' "$code" > "$evidence/$label.exit"
    printf '%s exit %s\n' "$label" "$code"
    return "$code"
}

source_identities > "$evidence/inputs.sha256"
run_check compiler compiler.stdout rustc -vV
expected=$(sed -n 's/^channel = "\([^"]*\)"$/\1/p' rust-toolchain.toml)
if ! grep -Fxq -- "release: $expected" "$evidence/compiler.stdout"; then
    printf 'The active Rust compiler differs from the project pin.\n' >&2
    exit 2
fi

failed=0
run_check format format.stdout cargo fmt --all --check || failed=1
run_check build build.stdout cargo build --workspace --locked --offline || failed=1
run_check test test.stdout cargo test --workspace --locked --offline || failed=1
run_check clippy clippy.stdout cargo clippy --workspace --all-targets --locked --offline -- -D warnings || failed=1
run_check rustassay rustassay.json "$checker" check src --json || failed=1
source_identities > "$evidence/inputs-after.sha256"
if ! cmp -s -- "$evidence/inputs.sha256" "$evidence/inputs-after.sha256"; then
    printf 'The checked Rust inputs changed during verification.\n' >&2
    exit 2
fi
exit "$failed"
