# Hallmark source publication

The repository at `CanonFlow-Engineering/hallmark` contains the F# preview,
portable examples, requirements and implementation plan.

Keep test fixtures synthetic and external consumer inputs identified in their
recipes. Preserve raw receipts with the exact source, configuration, producer
and run identities that produced them. Keep historical receipts, signing keys
and private fixture state outside the source tree.

For build, test, installed-tool and FsAssay scope, see
[Qualification](QUALIFICATION.md). The [roadmap](ROADMAP.md) defines the
criteria for broader delivery profiles.
