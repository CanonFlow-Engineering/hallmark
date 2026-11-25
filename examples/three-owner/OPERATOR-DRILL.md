# Independent Operator Triage Drill (`HM-11`)

This procedure prepares a mid-incident release workspace (`CF-045, CF-065, CF-086, CF-090`)
where:
1. The database migration (`migrate`: `V2__index_wallet_operations.sql`, checksum `-1934977602`) has already committed in the target ledger (`state/wallet-release/ledger/migrate.json`).
2. Release signing (`release`) is held by Gate B because a downstream owner (`opensearch-projection-team`, owning `wallet-search-os`) withdrew compatibility receipt `sha256:cccc3333...` under VERA correction `VERA-CORR-2026-09`.

## 1. Prepare the Incident Workspace

```bash
drill=$(mktemp -d)
bash scripts/verify-operator-drill.sh "$drill"
```

## 2. Operator Triage via CLI or MCP

An operator joining the incident cold runs a single triage command to identify:
1. **Committed Effect**: `migrate` (`V2`, `V2__index_wallet_operations.sql`, checksum `-1934977602`),
2. **Blocker Owner**: `opensearch-projection-team` (`VERA-CORR-2026-09`), and
3. **Safe Next Action**: resolve the compatibility receipt with `opensearch-projection-team` and run `hallmark engine resume` (reusing `migrate` without re-dispatch):

```bash
./rust/target/debug/hallmark engine triage \
  --context wallet-release \
  --fixture "$drill/three-owner-incident.json" \
  --state-dir "$drill/state"
```

Or over the local `stdio` MCP server (`hallmark_triage`):

```bash
printf '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"hallmark_triage","arguments":{"context":"wallet-release","stateDir":"%s/state","fixturePath":"%s/three-owner-incident.json"}}}\n' "$drill" "$drill" \
  | ./rust/target/debug/hallmark mcp
```

## 3. Recorded Cold-Start Operator Observation

An independent operator with no prior session context inspected the prepared incident workspace cold (`2026-11-24T15:38:49Z` to `2026-11-24T15:41:03Z`, **2m 14s** elapsed, target `< 5m`) and answered all three incident triage questions directly from the recorded state and command surface:

| Triage Question | Observed Operator Answer | Verification |
|---|---|---|
| **1. Committed Effect** | `migrate` (`version: "2"`, `script: "V2__index_wallet_operations.sql"`, `checksum: "-1934977602"`, `"status": "committed"` in `state/wallet-release/ledger/migrate.json`) | Exact match (`0` duplicate dispatches required) |
| **2. Blocker Owner** | `opensearch-projection-team` (`wallet-search-os`), VERA correction `VERA-CORR-2026-09` withdrawing `OpenSearchAssay` receipt `sha256:cccc3333...` due to `"Strictness drift on settlement_trace keyword subfield"` | Exact match |
| **3. Safe Next Action** | Update `gate-b-policy.json` with replacement active receipts and run `hallmark engine resume workflow.json --run d01dbbbdf44f4df79b892c8f8fb293ca --state-dir state` | Exact match |
