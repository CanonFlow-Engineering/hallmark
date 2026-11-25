# Wallet migration and six-stage release fixtures

This directory provides the Wallet V1 baseline SQL (`migrations/V1__wallet.sql`)
and the V2 index upgrade (`migrations/V2__index_wallet_operations.sql`) on
populated Wallet tables (`wallets`, `operations`, `outbox`).

## 1. Six-Stage Wallet Recovery & Gate B Release Drill (`HM-08`)

Exercises the full six-stage workflow (`Build`, `Install`, `Configure`,
`Migrate` via the first-class F# `postgres-migrate` adapter, `Verify`,
`Release`) through the **Hallmark CLI (Rust)** and **Hallmark Lifecycle Engine
(F#)** on local rootless Podman:

```bash
fixture=$(mktemp -d)
bash scripts/verify-wallet-engine.sh "$fixture/evidence" podman
```

The drill verifies:
1. Host manifest planning through the `sha256:`-pinned rootless Podman worker (`HALLMARK_WORKER_IMAGE`).
2. Interruption (`SIGKILL`) after Flyway commits `V2__index_wallet_operations.sql` to PostgreSQL 18.7, leaving the F# run journal in `"running"` status.
3. Refusal of tampered `public.flyway_schema_history` V2 checksums on `resume`.
4. Target-aware `ExternalLedger` recovery from PostgreSQL ledger readback with **1** total Flyway dispatch.
5. Gate B refusal of release signing when required bound receipts (`FsAssay`, `PostgresAssay`) are missing or have a mismatched `targetDigest`, followed by release signing once valid bound receipts are provided.
6. Independent verification of the signed release bundle (`wallet-release.bundle.zip`) by both the native **Hallmark CLI (Rust)** (`hallmark bundle-verify`) and an isolated `--network none --read-only` rootless Podman consumer container.

## 2. Standalone Rust Migration Pilot Drill

Exercises the migration-only Rust pilot against rootless Podman or Docker:

```bash
fixture=$(mktemp -d)
bash scripts/verify-wallet-rust.sh "$fixture/evidence" podman
```
