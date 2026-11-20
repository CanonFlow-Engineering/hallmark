# Hallmark qualification

Hallmark is an experimental local delivery preview. The supported verification
sequence starts from the pinned .NET SDK in `global.json`.

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
