# Hallmark lifecycle portfolio

Run the same six lifecycle stages against applications and data that have
already been deployed once. The .NET lanes use exact commits from the two Assay
Playgrounds. The other lanes use small, checked-in fixtures that exercise a
different recovery boundary.

| Sample | What Hallmark deploys | What the workflow verifies |
| --- | --- | --- |
| `inventory` | C# 15 Inventory API on .NET 11.0.100 with PostgreSQL 18.7 | The native 27-test suite, a historical reservation above today's limit, replay and conflict responses, a new reservation, and two matching outbox rows after a Flyway upgrade. |
| `wallet` | F# 11 Wallet API on .NET 11.0.100 with PostgreSQL 18.7 | The native 22-test suite, a retained debit, authorization refusal, replay and conflict responses, and one new debit after a Flyway upgrade. |
| `duckdb` | Versioned DuckDB 2.0 SQL package and Parquet report | Three sales preserve their IDs and total. The V2 database and Parquet both report 3,250 cents for east and 4,000 for west. |
| `stream-index` | Apache Kafka 4.3 and OpenSearch 3.9 | The broker retains three records. Replaying a repeated ID yields two documents in the new index. The alias switches to V2 after readback and returns the expected search hit. |
| `swift` | Swift Linux receipt CLI | The installed executable upgrades a V1 receipt, preserves its artifact ID, and rejects an unsupported receipt schema. |
| `android` | Kotlin application on an API 35 emulator | APK signatures match. Installing V2 over V1 preserves the artifact ID and upgrades the app's saved receipt in place. |

Each workflow runs Build, Install, Configure, Migrate, Verify and Release. The
.NET workflows also establish a V1 database with historical rows, then launch
the API and execute stateful HTTP probes after the native PostgreSQL migration.
The original test suites use a separate disposable database.

The harness emits each manifest through Hallmark's F# canonical serializer
and checks that emitting it again produces identical bytes. The local MCP
server returns the same plan digest and step order as the CLI, then verifies
the same signed bundle as the independent Rust consumer.

## Run the .NET pair

Use a local Linux host with rootless Podman, Bash, jq, curl, OpenSSL, tar, Git and
unzip. Run the following from the Hallmark checkout. Build Hallmark's worker
with its pinned .NET 10 SDK and use the exact .NET 11.0.100 SDK for the deployed
applications.

```bash
out=$(mktemp -d)
dotnet publish cli/Hallmark.Cli.fsproj -c Release -p:RestoreLockedMode=true -o "$out/worker"
(cd rust && cargo build --release --locked --package hallmark-cli)

export PORTFOLIO_HALLMARK="$PWD/rust/target/release/hallmark"
export HALLMARK_WORKER_DLL="$out/worker/Hallmark.Cli.dll"
export PORTFOLIO_DOTNET11=/path/to/dotnet11/dotnet
export PORTFOLIO_CSHARP=/path/to/CSharpAssay.Playground
export PORTFOLIO_FSHARP=/path/to/FSharpAssay.Playground
export PORTFOLIO_CS_PACKAGES="$PORTFOLIO_CSHARP/.packages/cache"
export PORTFOLIO_FS_PACKAGES="$HOME/.nuget/packages"

scripts/verify-portfolio.sh "$out/portfolio" inventory wallet
```

The C# source pin is `2c6697b940a3875a04cca46163dc5dd4b9f4e1f1`. The F# source
pin is `37fec1c7a17cf4e8ceb9a76b7f956151b1e6e28b`. Provide repositories that
contain those commits. Populate the package caches from the upstream projects'
locked restores and original preview packages. The builds use those locks.
Prepare the packaged FsAssay producer in the F# Playground before running
`wallet`. The portfolio uses the tool and package identity recorded in
`.packages/producer.json`.

Load the pinned PostgreSQL and Flyway images named in
[the harness](../../scripts/verify-portfolio.sh) into the rootless Podman
store. The two HTTP apps use loopback ports 5080 and 5088. Each PostgreSQL
fixture gets its own dynamically assigned loopback port and private password.

## Run the other targets

Use a fresh evidence directory for each invocation. Share the Hallmark CLI
and worker variables from the .NET example.

- Set `PORTFOLIO_DUCKDB` to the verified DuckDB 2.0.0 CLI and pass `duckdb`.
  Its release contains the SQL package and the generated Parquet bytes.
  [DuckDB 2.0.0](https://github.com/duckdb/duckdb/releases/tag/v2.0.0)
- Load `docker.io/apache/kafka:4.3.0` and
  `docker.io/opensearchproject/opensearch:3.9.0` in the rootless store and
  pass `stream-index`. The harness runs both containers with private network
  namespaces and invokes their native tools inside those namespaces. The
  OpenSearch service uses one node with security disabled for the local fixture.
  [Apache Kafka quickstart](https://kafka.apache.org/quickstart/) ·
  [OpenSearch container setup](https://docs.opensearch.org/latest/install-and-configure/install-opensearch/docker/)
- Load `docker.io/library/swift:6.4.0-noble` and pass `swift`. The pinned
  Linux image provides both the compiler and executable runtime.
  [Swift on Linux](https://www.swift.org/install/linux/)
- Set `PORTFOLIO_ANDROID_SDK` to an SDK with Platform 35, Build Tools 37.0.0
  and Platform Tools. Set `PORTFOLIO_KOTLINC` to Kotlin 2.4 and
  `PORTFOLIO_ANDROID_SERIAL` to a booted, dedicated Android emulator serial.
  Use JDK 21 and zip. Pass `android`. The workflow issues a local debug key,
  installs both versions on that emulator and verifies the final app data.
  [Android D8 and Kotlin compatibility](https://developer.android.com/build/kotlin-support) ·
  [Android Debug Bridge](https://developer.android.com/tools/adb)

For a preloaded image with a different local name, set
`PORTFOLIO_KAFKA_IMAGE`, `PORTFOLIO_OPENSEARCH_IMAGE` or
`PORTFOLIO_SWIFT_IMAGE`. The plan records the resolved image ID.

## Review one run

1. Open `workflow.json`, `plan.out`, and `mcp-plan.result.json` for the
   canonical manifest, CLI plan, and MCP plan.
2. Open the receipt named in `result.json` under `completedRunReceipt` for
   the step results. Open `target/out/verification.json`
   for the application or data assertions. The .NET workflows retain
   `target/out/tests.trx` and include its SHA-256 in the release evidence.
3. Open `converge.out` to review the second run. The harness requires each
   imperative stage to reuse its original execution and each reconciliation
   and verification stage to execute again.
4. Open `independent-verify.out` and `mcp-bundle.result.json` for the two
   bundle checks. The distribution from the accepted archive is extracted into a fresh
   `consumer/installed` directory after checking its SHA-256.
5. Use `result.json` for the two run IDs, plan digest and consumer outcome.
   Failed attempts retain their stage records and logs under `state/`.

These are local signed fixture releases. Keep native Assay receipts beside the
application tests. A governed release can require a target-specific Gate B
policy. The harness removes its owned Podman containers after the run. The
operator owns the supplied emulator and can stop it after the Android exercise.

## Wallet release hold and recovery

The Wallet run pairs a live PostgreSQL deployment with a simulated three-owner
handoff. A simulated search compatibility withdrawal and a pending FsAssay
receipt hold signing after Flyway commits V2 and the application passes Verify.
The CLI and MCP triage
reports agree on the failed release step, the committed migration, the
OpenSearch projection owner, and the proposed resume action. The default
MCP server also refuses the protected apply request.

The harness submits a replacement decision in the simulated handoff and runs
FsAssay on the pinned F# Playground source with a separate test database.
It binds the digest of the passing receipt to the frozen plan, source, policy,
and target, then resumes the same plan and run. It compares all completed step
records before and after resume, checks for a single migration start in the
journal, and queries PostgreSQL for one successful V2 ledger entry. Review
`owner-held.out`, `triage.out`, `owner-corrected.out`,
`wallet-assay/fsassay.json`, `active-gate.json`, `resume.out`, and
`flyway-v2.out`. The separate Kafka and OpenSearch workload exercises
real broker replay and index cutover with its own fixture events.

## Findings to carry into Hallmark

1. Add a target and toolchain preflight. The first Android attempt spent its
   output budget on diagnostics from an incompatible Kotlin and D8 pair.
   Detect that pairing, SDK availability and emulator readiness before Build.
2. Give each stateful adapter a readback contract. PostgreSQL already uses
   Flyway's durable history. Kafka replay, OpenSearch alias cutover, DuckDB
   file ownership and Android install identity need equivalent claims before
   they can support automatic recovery.
3. Bind native verifier receipts to the deployment subject. CI should be
   able to require the exact app commit, package, target and policy as part
   of release admission.
4. Put Inventory, Wallet and DuckDB in the regular qualification loop. Use
   the broker and search pair, Swift and Android for runners with those
   prerequisites. A macOS runner with Xcode can extend the Swift lane to iOS.
