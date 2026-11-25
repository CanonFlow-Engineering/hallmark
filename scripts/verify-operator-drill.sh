#!/usr/bin/env bash
set -euo pipefail

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
drill_dir=$(realpath -m -- "${1:?Supply a fresh drill directory}")
mkdir -p -m 700 -- "$drill_dir/state/wallet-release/ledger" "$drill_dir/state/wallet-release/runs"

printf 'wallet-service-v0.2.0\n' > "$drill_dir/source.txt"
cp -- "$repo/examples/rust-wallet/migrations/V1__wallet.sql" "$drill_dir/V1__wallet.sql"
cp -- "$repo/examples/rust-wallet/migrations/V2__index_wallet_operations.sql" "$drill_dir/V2__index_wallet_operations.sql"

# Record the committed V2 migration witness in the target ledger directory
cat > "$drill_dir/state/wallet-release/ledger/migrate.json" <<'EOF'
{"checksum":"-1934977602","databaseOid":16384,"ledgerEntries":2,"script":"V2__index_wallet_operations.sql","status":"committed","systemIdentifier":"7711743786825170983","version":"2"}
EOF
cp -- "$drill_dir/state/wallet-release/ledger/migrate.json" "$drill_dir/wallet-ledger.json"

# Create the three-owner fixture with a VERA withdrawal on OpenSearch projection receipt
jq '.corrections = [
  {
    "correctionId": "VERA-CORR-2026-09",
    "affectedReceiptDigest": "sha256:cccc333333333333333333333333333333333333333333333333333333333333",
    "actor": "opensearch-projection-team",
    "reason": "Strictness drift on settlement_trace keyword subfield",
    "effectiveAt": "2026-11-24T15:30:00Z",
    "kind": "Withdrawn"
  }
]' "$repo/examples/three-owner/fixture.json" > "$drill_dir/three-owner-incident.json"

# Create a workflow manifest and run record where build..verify succeeded (with migrate committed) and release failed at Gate B
cat > "$drill_dir/workflow.json" <<'EOF'
{
  "schemaVersion": 1,
  "name": "wallet-release",
  "context": "wallet-release",
  "inputs": ["source.txt", "V1__wallet.sql", "V2__index_wallet_operations.sql"],
  "allowedExecutables": ["/bin/sh"],
  "steps": [
    {
      "id": "build",
      "kind": "Build",
      "effect": "Imperative",
      "repeat": "ExecuteOnce",
      "needs": [],
      "operation": {
        "type": "process",
        "executable": "/bin/sh",
        "arguments": ["-eu", "-c", "printf 'WALLET-SERVICE-V0.2.0\n' > wallet-service.txt"]
      },
      "outputs": ["wallet-service.txt"],
      "retrySafety": "manual"
    },
    {
      "id": "migrate",
      "kind": "Migrate",
      "effect": "Imperative",
      "repeat": "ExecuteOnce",
      "needs": ["build"],
      "operation": {
        "type": "process",
        "executable": "/bin/sh",
        "arguments": ["-eu", "-c", "test -s wallet-ledger.json"]
      },
      "outputs": ["wallet-ledger.json"],
      "retrySafety": "external-ledger"
    },
    {
      "id": "verify",
      "kind": "Verify",
      "effect": "Observation",
      "repeat": "Reconcile",
      "needs": ["migrate"],
      "operation": {
        "type": "process",
        "executable": "/bin/sh",
        "arguments": ["-eu", "-c", "test -s wallet-ledger.json"]
      },
      "outputs": [],
      "retrySafety": "reconcile"
    },
    {
      "id": "release",
      "kind": "Release",
      "effect": "Imperative",
      "repeat": "ExecuteOnce",
      "needs": ["verify"],
      "operation": {
        "type": "release",
        "version": "0.2.0"
      },
      "outputs": [],
      "retrySafety": "external-ledger"
    }
  ]
}
EOF

cat > "$drill_dir/gate-b-policy.json" <<'EOF'
{
  "requiredAssays": ["FsAssay", "PostgresAssay", "OpenSearchAssay"],
  "lineage": "Withdrawn:VERA-CORR-2026-09 (opensearch-projection-team withdrew sha256:cccc3333...)",
  "receipts": []
}
EOF

runner="${PORTFOLIO_HALLMARK:-}"
if [[ -z $runner ]]; then
  if [[ -x "$repo/rust/target/release/hallmark" ]]; then
    runner="$repo/rust/target/release/hallmark"
  else
    runner="$repo/rust/target/debug/hallmark"
  fi
fi
rc=0
"$runner" engine apply "$drill_dir/workflow.json" --state-dir "$drill_dir/state" > "$drill_dir/initial-apply.out" 2> "$drill_dir/initial-apply.err" || rc=$?
[[ $rc -eq 1 ]]
jq -e '.Status == "failed" and .Steps[-1].Id == "release"' "$drill_dir/initial-apply.out" > /dev/null
printf 'Prepared operator incident workspace at %s\n' "$drill_dir"
