# Hallmark

Hallmark is a typed six-stage application delivery and release admission system (`Build`, `Install`, `Configure`, `Migrate`, `Verify`, `Release`).

## Component Ownership

| Formal name | Responsibility |
|---|---|
| **Hallmark CLI (Rust)** | Operator commands, pinned worker launch and isolation, independent bundle verification |
| **Hallmark Lifecycle Engine (F#)** | Planning, execution, the run journal, recovery decisions, and release admission |

The **Hallmark Lifecycle Engine (F#)** (`Hallmark.slnx`) runs the six-stage workflow graph, maintains the durable run journal, handles target-aware recovery and convergence, evaluates bound Gate B Assay receipts over the release stage's verification ancestors, and produces signed release bundles. The **Hallmark CLI (Rust)** (`rust/Cargo.toml`) provides the installed operator CLI (`hallmark`), launches the pinned rootless Podman worker (`Containerfile.worker`), and independently verifies release bundles. The Rust Wallet migration pilot remains the qualified migration path until the F# PostgreSQL adapter completes the live clean-install Wallet promotion drill (`HM-08`).

See the [Architecture Decision Record](docs/ARCHITECTURE.md) and [CF-067](REQUIREMENTS.md#cf-067--unify-the-hallmark-cli-rust-and-hallmark-lifecycle-engine-f) for the formal boundary contract.

## Build and Run

Use the Rust toolchain in [rust-toolchain.toml](rust/rust-toolchain.toml) and the .NET 10 SDK in [global.json](global.json). Run these commands from the repository root:

```bash
dotnet build Hallmark.slnx -c Release
cd rust
cargo fetch --locked
cargo install --locked --offline --path cli --root ../artifacts/rust
cd ..
artifacts/rust/bin/hallmark --help
artifacts/rust/bin/hallmark engine capabilities
```

To execute the **Hallmark Lifecycle Engine (F#)** inside its pinned rootless Podman worker container:

```bash
HALLMARK_WORKER_IMAGE=sha256:<worker-image-digest> \
  artifacts/rust/bin/hallmark engine plan examples/local-service/canonforge.json --state-dir /tmp/hallmark-state
```

Use the [Wallet fixture](examples/rust-wallet/README.md) to exercise a real PostgreSQL migration interruption, or follow [local operation](RUNNING.md) for a prepared target. See the [command reference](COMMANDS.md) for the input, output, and exit contract.

The [lifecycle portfolio](examples/portfolio/README.md) runs C# and F# APIs,
PostgreSQL, DuckDB, Kafka with OpenSearch, Swift and Android through local
build, deployment, migration, verification and signed release drills.

## Verify a Change

Qualify the **Hallmark Lifecycle Engine (F#)** with `FsAssay` v1.0.4 (including registered TRX test execution) and the **Hallmark CLI (Rust)** with `RustAssay` and Cargo tests:

```bash
bash scripts/verify-fsharp.sh /path/to/fsassay-tool-root /path/to/fsharp-evidence /path/to/nuget-cache
bash scripts/verify-rust.sh /path/to/rust-assay ec2f519a89f7d99a33d4b863d688025bc0527b02 /path/to/rust-evidence
```

The [qualification record](QUALIFICATION.md) describes observed cases and remaining promotion gates. [Requirements](REQUIREMENTS.md#active-delivery-priorities), [Architecture](docs/ARCHITECTURE.md), [Roadmap](ROADMAP.md), [Gantt](docs/delivery/gantt.html), [Kanban](docs/delivery/kanban.html), and the [Interactive Architecture Diagram](docs/delivery/architecture.html) track the delivery milestones.

Hallmark is published under the [Apache License 2.0](LICENSE).
