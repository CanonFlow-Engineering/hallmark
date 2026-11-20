# Real Playground recipes

These recipes preserve the chosen application source and SDK and run entirely
against owned local fixtures. Wallet uses SDK 10.0.301 and a patched 10.0.14
runtime; orders uses SDK 11 RC2 and the separately tested RC3 runtime. All Docker
images are pinned in the templates. No source framework downgrade is used.

## Prepare a wallet

Use a new destination and a unique cf- project name. The original checkout is
read, copied and fingerprinted; it is not modified.

```bash
python3 prepare.py wallet /path/to/FSharpAssay.Playground /tmp/cf-wallet-example --cache /path/to/nuget-cache --project cf-wallet-example
```

Initialize named fixture secrets through stdin, with no trailing newline:

```bash
python3 -c 'import secrets,sys; sys.stdout.write(secrets.token_hex(24))' | hallmark secret-set database --context cf-wallet-example --state-dir /path/to/hallmark-state
python3 -c 'import secrets,sys; sys.stdout.write(secrets.token_hex(24))' | hallmark secret-set token --context cf-wallet-example --state-dir /path/to/hallmark-state
hallmark plan /tmp/cf-wallet-example/hallmark.json --state-dir /path/to/hallmark-state
hallmark apply /tmp/cf-wallet-example/hallmark.json --state-dir /path/to/hallmark-state
```

The nine steps build/capture, install PostgreSQL, migrate through Flyway,
configure/install the API, exercise seven fixture behaviors imperatively, observe
the report/current availability read-only, and seal a release. The source's
loopback-only listener is preserved. A bounded curl sidecar shares the target
network namespace, so the app needs no diagnostic utilities or host port.

## Prepare orders

Use an identified CsAssay.Analyzers.0.2.0-rc.1 package from the chosen producer:

```bash
python3 prepare.py orders /path/to/CSharpAssay.Playground /tmp/cf-orders-example --cache /path/to/nuget11-cache --project cf-orders-example --analyzer-package /path/to/CsAssay.Analyzers.0.2.0-rc.1.nupkg
```

Build uses locked restore. A different same-version analyzer archive correctly
fails its old content hash. Refresh locks only in the disposable stage using the
application's producer preparation contract, record before/after identities, then
repeat locked restore/build and original tests. Do not disable analyzers, lower
warning policy or edit application types. The actual comparison records that
scoped adaptation in its source-adaptations.json receipt.

The seven-step orders recipe needs no secret. It deploys the original native-union
API, exercises six cases imperatively, observes report/availability and seals.
Original 55 behavior and 7 architecture tests are separate evidence; the fixture
probes do not replace them.

## Reconcile and retain evidence

Fresh apply creates a new run and may execute imperative effects. Resume and
converge are explicit commands; use the recorded run ID. Converge repairs desired
state and performs read-only Verify while reusing fixture exercise, migration,
build and release. Changing source/configuration requires a fresh identified plan.
Use a new release version if its plan or artifact bytes change.

Public-key export, release/bundle verification and owned teardown are documented
in ../../COMMANDS.md. Teardown retains volumes and evidence. These are local
qualification recipes; backup/restore, production credentials, target fencing,
external authorization and progressive rollout remain separate requirements.

[Qualification](../../QUALIFICATION.md) describes the observed scope for these recipes.
