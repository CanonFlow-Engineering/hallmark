# Hallmark source publication

The repository at `CanonFlow-Engineering/hallmark` contains the Rust engine,
Wallet recovery fixture, requirements and implementation plan.

Keep test fixtures synthetic and external consumer inputs identified in their
recipes. Preserve raw receipts with the exact source, configuration, producer
and run identities that produced them. Keep historical receipts, signing keys
and private fixture state outside the source tree.

Build, test, installed-tool and Assay results are in
[qualification](QUALIFICATION.md). The [roadmap](ROADMAP.md) defines the
criteria for broader delivery profiles.
