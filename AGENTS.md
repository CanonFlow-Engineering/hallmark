# Hallmark contributor instructions

Hallmark's F# preview is the behavioral reference. Build the maintained
lifecycle engine in Rust under CF-067. Keep planning and recovery decisions
separate from the filesystem, process, network, persistence and CLI shell.
Preserve supported inputs, effect semantics and retained reference tests.

For F# work, read the current FsAssay Shape and authority contracts from the
identified producer, run FsAssay over the actual changed project/source scope,
and retain the exact command, source/configuration/producer identities, exit code
and raw receipt. Run compilation and behavior tests as separate evidence. Use
supported registered test producers; report unsupported/missing verification.
Do not weaken test minimums, policy, scope or rule admission to obtain a pass.
Zero admitted rules/findings does not establish purity or production readiness.

For Hallmark-owned Rust changes, run the pinned RustAssay checker on the
declared functional-core scope. Record the actual scope and effect boundaries,
exact invocation, source and producer identities, exit and raw receipt. Run
the Cargo build and behavior tests as separate evidence. CI and Hallmark's own
release admission require the scoped RustAssay result under CF-074. Deployed
workloads use the verifier according to their effective target policy.

For changes to the delivery plan views, author TypeScript in
`docs/delivery` and rebuild the browser bundle. Run TypeScriptAssay with
the `new` policy in that directory and the producer pin in the delivery
views workflow. Retain the native verdict and separate compiler and
browser evidence.

When the local CanonFlow skill collection is available, select relevant skills
through its catalog. Do not claim to have used unavailable tools or guidance.
An unavailable producer/toolchain is missing evidence to address and report.

Build and tests: `dotnet restore Hallmark.slnx --locked-mode`, then
`dotnet build Hallmark.slnx -c Release --no-restore` and
`dotnet test Hallmark.slnx -c Release --no-build --no-restore`.
Use `scripts/verify.sh TOOL_ROOT FRESH_EVIDENCE_DIR NUGET_CACHE` for the identified
FsAssay 1.0.4 package and pinned SDK container. Evidence may be written outside
the checkout so the candidate remains clean. Never replace an old raw receipt.

Historical Labs receipts describe their original commit/tree, paths and inputs.
They must not be relabelled as authoritative checks of a new repository commit.
Keep local private state, credentials, signer private keys, sandbox configuration,
vendor binaries/manuals and application payloads out of commits.

Home: CanonFlow-Engineering/hallmark. Consumers: the local example and staged
Playground applications. Status: experimental local preview. Production or
assurance-kernel promotion needs its separate documented qualification; a push
does not supply missing authority or introduce a dependency from CanonFlow core.
