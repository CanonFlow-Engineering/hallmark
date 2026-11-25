# Hallmark contributor instructions

Build the Hallmark lifecycle engine in Rust under CF-067. Keep planning and
recovery decisions separate from the filesystem, process, network, persistence
and CLI shell. Use the [archived preview](QUALIFICATION.md#archived-f-preview)
as the behavioral reference when porting its contracts. Keep the original
receipts bound to their recorded subjects.

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

Use the toolchain in `rust/rust-toolchain.toml` and fetch dependencies with
`cargo fetch --locked` in `rust/`. Run `scripts/verify-rust.sh` for the full
workspace and the scoped core receipt. For migration, run
`scripts/verify-wallet-rust.sh` against the owned fixture. Write new evidence to
a fresh directory outside the checkout.

Historical Labs receipts describe their original commit/tree, paths and inputs.
They must not be relabelled as authoritative checks of a new repository commit.
Keep local private state, credentials, signer private keys, sandbox configuration,
vendor binaries/manuals and application payloads out of commits.

Home: CanonFlow-Engineering/hallmark. Consumers: the local example and staged
Playground applications. Status: experimental local preview. Production or
assurance-kernel promotion needs its separate documented qualification; a push
does not supply missing authority or introduce a dependency from CanonFlow core.
