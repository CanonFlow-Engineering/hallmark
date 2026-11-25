# Executed comparison and remaining parity

Qualification date: 2026-11-16. CanonForge `0.2.0-local.1`; Weave `0.9.2`.
[Weave inventory](WEAVE-EXTRACTION.md) records the full reference extraction.
[comparison-summary.json](comparison-summary.json) identifies runs and evidence.

## Applications and results

| Sample | CanonForge | Weave | Behavioral evidence |
|---|---|---|---|
| FSharpAssay.Playground 40-wallet | Build, capture, PostgreSQL install, Flyway migration, configuration, API install, Verify and signed release | Package build/dist/publish and systemd API install; database/Flyway provisioned separately | 7 HTTP probes on each path; original 22-test PostgreSQL/HTTP suite passes |
| CSharpAssay.Playground 40-functional-shape | Exact .NET 11 RC2 build, artifact/configuration, API install, Verify and signed release | Package build/dist/publish and systemd API install | 6 HTTP probes on each path; original 55 behavior + 7 architecture tests pass |
| GSTFlow NativeAOT CLI | Builds/captures the native binary, then refuses release when Verify exits 1 | oneshot unit activation fails; install reports failure | Native runtime reports missing F# Printf specialization code; both retain failure |

The first two use the same staged application payloads. Weave consumes them via
a read-only fixture bind: native nonempty artifact exports were not qualified.
Its wallet database and Flyway invocation are external fixture preparation.
These adaptations are material limits of the comparison. The wallet source binds
loopback; probes execute inside each application container to preserve that
contract. Both paths use the same probe implementation. Final CanonForge recipes classify
fixture exercise as imperative and keep Verify read-only. No host application
ports are published by the CanonForge fixtures.

Original application repositories were not edited. F# uses SDK 10.0.301. C# keeps
SDK 11.0.100-rc.2.26504.105, net11.0, native unions, preview language, nullable,
warning policy and analyzer scope. A CsAssay analyzer was rebuilt from identified
source; matching lock identities changed only in the disposable consumer copy.
The ordinary C# dotnet-test coordinator stalled and was stopped; its already-built
MTP executables subsequently ran directly with minimum counts and native xUnit
TRX reports. The stalled command is not reported as a pass.

GSTFlow has no original SDK pin; the packaging workspace selects SDK 10.0.301
and preserves PublishAot=true. Its compiler initially resolved 10.0.9 and reported
GHSA-p3jp-9v83-76w9. A scoped packaging rebuild with RuntimeFrameworkVersion
10.0.14 resolves the patched compiler/runtime, preserving the original source.
[Microsoft's advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9)
names 10.0.14 as fixed for that issue. Native compatibility warnings and the
observed runtime failure remain. Neither a successful build nor its initial
success-looking console line was accepted as a successful verification exit.

## Measured capability matrix

“Documented” below is an interface claim, not a passed runtime test.

| Capability | Weave observation | CanonForge observation |
|---|---|---|
| Typed graph/effects | Dhall, catalog action taxonomy, Current admission tested | F# validated graph/effects; deterministic planning and refusal tests |
| Discovery/help | 231 help outputs; 161 pages; 1,464 signatures | 21 commands, packaged schema, capabilities and tested installed CLI |
| Build | Empty-export package build exercised; native payload export unqualified | Pinned SDK builds of real source; declared input/producer identities |
| Artifact distribution | Publish/index/install exercised with shared fixture payload | SHA-256 artifact capture/dedup, immutable release bundle and offline byte verification |
| Install/configuration | Raw systemd resource on vendor sandbox | Atomic files and owned Docker Compose reconciliation |
| Dependency outputs | Advertised typed build/install exports | Checked ancestor/output references; runtime substitution |
| Plan/apply identity | Plan/analyze/dry-run differences observed | Freeze and optional expected plan digest; mismatch refuses effects |
| Source/output drift | Capture-related interfaces documented | Declared source hashes and output bytes checked on resume/release |
| Live service drift | Raw unchanged-unit converge left stopped service inactive despite exit 0 | Stopped containers and changed config detected; convergence repaired and Verify reran |
| Database migration | Typed PostgreSQL/transaction surfaces extracted; native blocks untested | Flyway 13.12.1/PostgreSQL 18.7 recipe executed; repeated migrate applied zero new migrations |
| Database transaction scope | Journaled handle semantics documented | Delegated to Flyway/PostgreSQL; no cross-lifecycle atomicity claim |
| Recovery | Partial GST install retained; automatic retry guidance displayed | Changed identity/output refuses; unsafe retry refuses; explicit converge reuses build/migration/release |
| Secrets | Rich catalog; fixture used a restricted environment file | Local encryption, named/versioned delivery, redaction and rotation-invalidates-resume tests |
| Ownership/concurrency | systemd resource identities visible | Context locks and resource claims; competing file owner rejected |
| Deadlines/cancellation | Not runtime-qualified here | Timeout, cancellation, output-cap termination and bounded state tested |
| Frozen/offline use | Two-package weave.lock written | Frozen identified preview and portable signed release verification; no general offline source build claim |
| Release composition | Rich release CLI documented, not deployed here | Signed immutable local release composition exercised |
| Promotion/rollback | Channels/hops/rollouts documented | Verified local pointers; no automatic app/schema rollback |
| Teardown | Service package removal exercised separately | Owned definition checks; Compose teardown retains volumes and evidence |
| Structured evidence | NDJSON execution observed; converge execute JSON help mismatch | JSON runs, actual exit/status, logs, event-chain checks and portable artifacts |
| Multi-node/Kubernetes/RBAC | Advertised, untested | Not implemented/qualified |
| Hermetic build cache | Advertised source closure/build facilities, not qualified | CAS byte dedup only; general build cache deferred |
| Progressive rollout/fleet controller | Advertised, untested | Not implemented/qualified |
| Provenance trust | Bundle signing advertised, not qualified here | Local explicit-key ECDSA; no KMS, keyless identity or SLSA certification |
| Assay authority | Not assessed | Actual FsAssay Inconclusive; separate compiler/tests retained |

## Iteration results

1. Baseline: M0 had 30 tests and no executor. Preserved its receipt before work.
2. Execution: added the local engine, strict wire format, vault, artifacts and
   releases. Both real applications deployed; 47 initial tests passed.
3. Recovery: stopped services and changed configuration. Added live Compose
   inspection and explicit convergence. Both applications repaired; the wallet
   migration and immutable release were reused. Retained Weave's raw-unit result.
4. Packaging and negative cases: portable release verification, immutable release
   corruption refusal, state budget and ambient environment tests bring the suite
   to 52. Installed the packed CLI, exercised its example and release commands,
   reran real deployments, and retained GSTFlow's rejected release.

All missing capabilities stay visible. This is a qualified local experiment with
bounded evidence, not full enterprise parity. The next promotion candidate needs
admitted Assay evidence, hermetic build/provider adapters, migration recovery,
external trust/authorization and progressive multi-target deployment qualification.

## Final replay

CanonForge application releases 1.4.0 separate imperative fixture exercise from
read-only report/availability verification. Weave packages 1.1.0 use patched
runtimes and passed their real source-version forward check and upgrade. Both
paths retain six orders and seven wallet behavior cases. The final CanonForge
fault loop reuses exercise and release, repairs stopped containers/configuration,
and returns to zero observed file/service drift. A fresh portable preparation
of the wallet recipe also executes all nine steps successfully.

Compiler SDKs stay unchanged. Final runtime images are ASP.NET 10.0.14
(sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c)
and ASP.NET 11 RC3
(sha256:e1ba5ef29b9c0f51b4236cbd25268b84a1e15f8fb1dca21f141bd0358903a1f2).
The wallet's separate 22 tests and C# 55+7 tests pass on those runtime images.
GSTFlow's 23 core tests pass, while its native executable remains failed.
