# Local operation

Build/install from README.md, then select an explicit state directory:

```bash
hallmark plan examples/local/hallmark.json --state-dir artifacts/state
hallmark apply examples/local/hallmark.json --state-dir artifacts/state
hallmark status --context local-example --state-dir artifacts/state
hallmark history --context local-example --state-dir artifacts/state
```

Use the returned run ID for resume, drift or converge. A new clone/build can have
new producer identities; old runs from another checkout are historical evidence,
not an instruction to bypass resume compatibility.

Portable real-application preparation is in examples/playground/README.md.
Keep private state and signer keys outside commits. For a recorded owned Compose
run, `hallmark destroy --context NAME --run ID --state-dir DIR` checks ownership
and definition bytes and retains volumes/evidence. This does not delete unrelated
containers or perform database rollback.
