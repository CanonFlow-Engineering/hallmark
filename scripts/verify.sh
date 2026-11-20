#!/usr/bin/env bash
set -euo pipefail
sdk_image=mcr.microsoft.com/dotnet/sdk:10.0.301@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5
assay_dll=/tool/.store/fsassay.cli/1.0.4/fsassay.cli/1.0.4/tools/net10.0/any/FsAssay.Runner.dll

if [[ ${1:-} == --inside ]]; then
    cd /workspace
    dotnet restore Hallmark.slnx --locked-mode > /evidence/restore.log 2>&1
    dotnet build Hallmark.slnx -c Release --no-restore > /evidence/build.log 2>&1
    dotnet test Hallmark.slnx -c Release --no-build --no-restore \
        --logger 'trx;LogFileName=hallmark.trx' --results-directory /evidence/tests \
        > /evidence/tests.log 2>&1
    dotnet run --project demo/Hallmark.Demo.fsproj -c Release --no-build --no-restore \
        > /evidence/preview.json 2> /evidence/demo.stderr.log
    dotnet "$assay_dll" doctor > /evidence/doctor.log 2>&1
    assay_args=(dotnet "$assay_dll" check --out-json /evidence/fsassay.json
        --out-sarif /evidence/fsassay.sarif --assessment-json /evidence/assessment.json Hallmark.slnx)
    printf '%q ' "${assay_args[@]}" > /evidence/fsassay.command.txt
    printf '\n' >> /evidence/fsassay.command.txt
    status=0
    "${assay_args[@]}" > /evidence/fsassay.log 2>&1 || status=$?
    printf '%s\n' "$status" > /evidence/fsassay.exit
    exit "$status"
fi

tool_root=${1:?Usage: verify.sh TOOL_ROOT FRESH_EVIDENCE_DIR NUGET_CACHE}
evidence_dir=${2:?A fresh evidence directory is required}
nuget_cache=${3:?A prepared NuGet cache is required}
project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
tool_root=$(realpath -- "$tool_root")
nuget_cache=$(realpath -- "$nuget_cache")
evidence_dir=$(realpath -m -- "$evidence_dir")
if [[ -e $evidence_dir ]]; then
    printf 'Evidence directory already exists: %s\n' "$evidence_dir" >&2
    exit 64
fi
if [[ ! -f $tool_root/${assay_dll#/tool/} ]]; then
    printf 'FsAssay 1.0.4 is absent from the supplied tool root.\n' >&2
    exit 64
fi
mkdir -p -- "$evidence_dir"
container=(docker run --rm --network none --user "$(id -u):$(id -g)"
    --cap-drop ALL --security-opt no-new-privileges --pids-limit 256 --memory 3g --cpus 2
    -e DOTNET_CLI_HOME=/tmp/dotnet-home -e NUGET_PACKAGES=/nuget
    --mount "type=bind,src=$project_root,dst=/workspace"
    --mount "type=bind,src=$nuget_cache,dst=/nuget"
    --mount "type=bind,src=$tool_root,dst=/tool,readonly"
    --mount "type=bind,src=$evidence_dir,dst=/evidence"
    -w /workspace "$sdk_image"
    bash /workspace/scripts/verify.sh --inside)
printf '%q ' "${container[@]}" > "$evidence_dir/container.command.txt"
printf '\n' >> "$evidence_dir/container.command.txt"
"${container[@]}"
