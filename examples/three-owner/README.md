# Three-Owner Release Coordination & VERA Correction Fixture (`HM-10`)

This fixture exercises multi-owner release coordination across three component
teams and an unrelated fourth component (`CF-043–CF-045, CF-075, CF-077–CF-078, CF-090`):

1. **`wallet-ledger-pg`** (`ownerTeam`: `postgres-ledger-team`) — Upstream PostgreSQL ledger upgrade (`V2__index_wallet_operations`).
2. **`wallet-cdc-kafka`** (`ownerTeam`: `kafka-stream-team`) — Downstream Kafka CDC stream consuming `wallet-ledger-pg`.
3. **`wallet-search-os`** (`ownerTeam`: `opensearch-projection-team`) — Downstream OpenSearch projection reaching `wallet-ledger-pg` through two dependency paths (`wallet-ledger-pg -> wallet-search-os` and `wallet-ledger-pg -> wallet-cdc-kafka -> wallet-search-os`), displayed once with both dependency paths preserved (`CF-090`).
4. **`backoffice-reporting`** (`ownerTeam`: `reporting-team`) — Unrelated component excluded from the change-set impact closure (`CF-043`).

## Run via CLI or MCP

```bash
./rust/target/debug/hallmark engine three-owner-check examples/three-owner/fixture.json
```

Or invoke the `hallmark_three_owner_evaluate` tool over `hallmark mcp`:

```bash
printf '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"hallmark_three_owner_evaluate","arguments":{"fixturePath":"examples/three-owner/fixture.json"}}}\n' \
  | ./rust/target/debug/hallmark mcp
```
