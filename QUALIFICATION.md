# Hallmark qualification

Hallmark is an experimental local delivery preview. The supported verification
sequence starts from the pinned .NET SDK in `global.json`.

## Observed preview checks

The 2026-11-20 checks ran on source revision `46ad1d09bfe65c3e8de6b6b9fb48e8f5f143cf5c`.

| Check | Observed result |
|---|---|
| Locked restore and Release build | SDK 10.0.301, zero build errors. The offline container reported NU1900 because it could not fetch advisory data. |
| Standalone xUnit v2/VSTest run | 52 passed, zero failed and zero skipped. |
| Installed tool | `Hallmark.Cli` 0.2.0-local.1 installed from its packed archive. The `hallmark` command executed the seven-step local example and verified the signed bundle and its four artifacts. |
| FsAssay 1.0.4 `check` | **Inconclusive**, exit 2. The check route left the required xUnit suite unexecuted. |
| FsAssay 1.0.4 `verify` | **ToolFailure**, exit 3. The required test producer did not emit its registered TRX report. The separate VSTest run has its own result. |

## Reproduce the checks

```sh
dotnet restore Hallmark.slnx --locked-mode
dotnet build Hallmark.slnx -c Release --no-restore
dotnet test Hallmark.slnx -c Release --no-build --no-restore
```

Run `scripts/verify.sh TOOL_ROOT FRESH_EVIDENCE_DIR NUGET_CACHE` with the
identified FsAssay 1.0.4 package and the script's pinned SDK image. Retain the
exact command, producer and input identities, exit and original receipts.
Keep build and xUnit results alongside the FsAssay verdict. The script uses
`check`. Test verification requires a supported registered producer.

Pack and install the CLI as shown in [README](README.md). Exercise the installed
entry point with the [local example](examples/local/hallmark.json). Retain the
run identity and verify its release bundle against the explicitly selected
public key.

## Retained application evidence

The 2026-11-16 reference checks built and deployed the unchanged wallet and
orders sources in prepared fixtures. The wallet exercised seven HTTP probes and
22 independent tests. Orders exercised six HTTP probes, 55 behavior tests and
seven architecture tests. The wallet recipe used Flyway and PostgreSQL. The
original application source, compiler and runtime identities bound those runs.

The GSTFlow NativeAOT build produced a captured binary. Its startup failed with
missing F# `Printf` specialization code and the required verification blocked
release. The 23 passing rule-engine tests describe a separate scope.

The [Playground recipes](examples/playground/README.md) describe the fixture
preparation, adapters and supported local operations. The test and receipts
produced during the reference runs remain tied to their original sources and
toolchains. Current implementation and profile qualification are in the
[roadmap](ROADMAP.md).
