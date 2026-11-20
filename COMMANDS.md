# Hallmark command reference

Context commands accept `--state-dir DIR`, defaulting to `.hallmark` in the
current working directory. Select it explicitly when changing directories.
A manifest's parent is its source workspace; its context selects the target lock.

| Command | Behavior |
|---|---|
| `capabilities` | JSON inventory of stages, operations and commands |
| `schema` | Packaged workflow JSON Schema |
| `doctor` | Runtime, OS, assembly identity and Docker CLI availability |
| `plan MANIFEST` | Resolve declared inputs, secret versions and executables; validate and preview |
| `freeze MANIFEST FILE` | Save preview, plan digest, declared source identities and secret versions |
| `apply MANIFEST` | Start a fresh run; imperative effects may execute again |
| `resume MANIFEST --run ID` | Reuse completed outputs; refuse changed identity, drift or unsafe retries |
| `converge MANIFEST --run ID` | From a successful run, repair desired state and rerun Verify; reuse build/migration/release evidence |
| `status --context NAME` | Load integrity-checked run records |
| `history --context NAME` | Verify the event chain and report its head |
| `drift --context NAME --run ID` | Inspect recorded file bytes and expected Compose service state/health |
| `secret-set NAME --context NAME` | Read up to 64 KiB from stdin and create a new encrypted local version |
| `secret-list --context NAME` | List names and version identities |
| `public-key --context NAME` | Export the context release signer public key |
| `release-verify FILE --public-key FILE` | Verify signer, signature and local artifact bytes |
| `bundle FILE --context NAME --run ID --public-key FILE` | Create a portable release ZIP without overwriting a destination |
| `bundle-verify FILE --public-key FILE` | Offline verification of release, attested steps and bundled artifact bytes |
| `promote VERSION --channel NAME --context NAME --public-key FILE` | Change a local release pointer after verification; no application deployment or database rollback |
| `channels --context NAME` | Show local release pointers |
| `export FILE --context NAME --run ID` | Export run metadata and current history head |
| `destroy --context NAME --run ID` | Tear down recorded owned Compose projects after checking ownership and definition bytes |

Apply, resume and converge accept `--plan-digest sha256:...`. A mismatch refuses
effects. Use the digest emitted by freeze to bind a reviewed plan. Apply always
means a new run; resume is a distinct recovery decision.

## Wire format and references

The six required root fields are schemaVersion, name, context, inputs,
allowedExecutables and steps. Unknown/duplicate JSON fields are rejected. See
[schema/workflow-v1.schema.json](schema/workflow-v1.schema.json) and the
[local example](examples/local/hallmark.json). Schema validation checks wire
shape; plan additionally validates graph, effects and release ancestry.

Operations: process, write-file, capture, compose-up, http and release.
References use `{{ancestor.output}}`. The ancestor must be a declared dependency.
Output keys: process stdout/stderr/exitCode; file/capture path/digest; Compose
project/file; HTTP status/body; release path/digest/version.

Environment/header values are strings or `{"secret":"name","prefix":"Bearer "}`.
Literals support output references. Secret material is resolved at execution and
excluded from plans. The local key and ciphertext belong to the same OS user;
this is not a KMS or a boundary against that user. External programs and Docker
can retain delivered material under their own rules.

HTTP observation supports GET/HEAD. Explicit process recipes own build engines,
migrations and behavior probes. External-ledger retry safety is a promise about
the chosen engine's repeat behavior, not generic exactly-once execution. The
migration comparison uses Flyway 13.12.1 and its PostgreSQL history.

## Limits and evidence

Execution is serial within a context. Limits: 1 MiB manifest, 256 steps, 64
executables, 10,000 declared source files totaling 256 MiB, 1–1,800 seconds and
128 bytes–4 MiB output per step, 8 MiB run metadata, 64 MiB/100,000 history events,
and 512 MiB bundle. Large output belongs in captured files. Input enumeration
excludes bin, obj, .git, .hallmark and node_modules. Undeclared compiler,
network and tool inputs prevent a general hermetic-build claim.

Processes use literal argument lists. A small OS/Docker environment set plus
explicit variables is forwarded; inherited values and executable bytes take part
in identity. Deadlines/tree termination are local process facilities, not hostile
code containment. Path checks reject escapes and observed symlinks, without
claiming a kernel sandbox against a hostile concurrent filesystem actor.

Run checksums and event chains detect inconsistency; they do not authenticate a
producer. Release signatures require a separately trusted key and do not prove
correctness. Bundles authenticate release, artifact bytes and attested steps;
extra run/history documents are supplemental. Convergence records a new run and
retains the prior immutable release.

Teardown preserves volumes, user files, artifact/state history and ownership
claims. Drift after teardown is expected; convergence can reinstall the desired
state. Database restore/rollback requires a separately qualified procedure.

Exit codes: 0 successful operation, 1 unsuccessful run, 2 observed drift,
3 invalid/unavailable runtime evidence, 64 invalid invocation, 130 cancellation
before a run record is returned. Returned runs retain individual timeout,
cancellation/failure statuses and process exit codes. CLI success is not an
assurance verdict.
