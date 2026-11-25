namespace CanonForge.Lab.Tests

open System
open Xunit
open CanonForge.Lab
open CanonForge.Lab.Serialization

type AdmissionTests() =
    let validHash tag = ContentDigest.ofText ("admission-test:" + tag)

    [<Fact>]
    member _.``Gate B semilattice satisfies dominance commutativity associativity and idempotence``() =
        let all = [Pass; Inconclusive; Fail; ToolFailure]
        for a in all do
            Assert.Equal(a, Admission.combineOutcome a a)
            Assert.Equal(a, Admission.combineOutcome Pass a)
            Assert.Equal(ToolFailure, Admission.combineOutcome ToolFailure a)
            for b in all do
                Assert.Equal(Admission.combineOutcome a b, Admission.combineOutcome b a)
                for c in all do
                    Assert.Equal(
                        Admission.combineOutcome (Admission.combineOutcome a b) c,
                        Admission.combineOutcome a (Admission.combineOutcome b c)
                    )

    [<Fact>]
    member _.``Gate B refuses missing required assays and superseded or withdrawn VERA releases``() =
        let receipts = [
            { Assay = FsAssay; Outcome = Pass; ReceiptDigest = validHash "fsassay" }
            { Assay = PostgresAssay; Outcome = Pass; ReceiptDigest = validHash "pgassay" }
        ]
        let missingVerdict = Admission.evaluateGateB [FsAssay; PostgresAssay; RustAssay] receipts Active
        Assert.False(missingVerdict.Admitted)
        Assert.Equal(Inconclusive, missingVerdict.AggregateOutcome)
        Assert.Contains(MissingRequiredAssays [RustAssay], missingVerdict.Rejections)

        let passingVerdict = Admission.evaluateGateB [FsAssay; PostgresAssay] receipts Active
        Assert.True(passingVerdict.Admitted)
        Assert.Equal(Pass, passingVerdict.AggregateOutcome)

        let supersededVerdict = Admission.evaluateGateB [FsAssay; PostgresAssay] receipts (Superseded "rel-2026-11-24")
        Assert.False(supersededVerdict.Admitted)
        Assert.Contains(ReleaseSuperseded "rel-2026-11-24", supersededVerdict.Rejections)

        let withdrawnVerdict = Admission.evaluateGateB [FsAssay; PostgresAssay] receipts (Withdrawn "CVE-regression")
        Assert.False(withdrawnVerdict.Admitted)
        Assert.Contains(ReleaseWithdrawn "CVE-regression", withdrawnVerdict.Rejections)

    [<Fact>]
    member _.``Bound release gate verifies plan source policy and target identity bindings``() =
        let binding = {
            PlanDigest = validHash "plan-1"
            SourceDigest = validHash "src-1"
            PolicyDigest = validHash "pol-1"
            TargetDigest = validHash "tgt-1"
        }
        let matchingReceipt = {
            Assay = FsAssay
            Outcome = Pass
            ReceiptDigest = validHash "fsassay-receipt"
            PlanDigest = binding.PlanDigest
            SourceDigest = binding.SourceDigest
            PolicyDigest = binding.PolicyDigest
            TargetDigest = binding.TargetDigest
        }
        let passing = Admission.evaluateBoundReleaseGate binding [FsAssay] [matchingReceipt] Active
        Assert.True(passing.Admitted)

        let mismatchedTarget = { matchingReceipt with TargetDigest = validHash "other-target" }
        let rejected = Admission.evaluateBoundReleaseGate binding [FsAssay] [mismatchedTarget] Active
        Assert.False(rejected.Admitted)
        Assert.Contains(MismatchedReceiptIdentity (FsAssay, "targetDigest"), rejected.Rejections)

    [<Fact>]
    member _.``Migration Ledger v2 catches expected catalog mismatch live pg_catalog drift checksum drift and active sessions``() =
        let v1 = { Version = "001"; Checksum = 101; ExpectedCatalogDigest = validHash "cat-v1" }
        let v2 = { Version = "002"; Checksum = 202; ExpectedCatalogDigest = validHash "cat-v2" }
        let appliedV1 = { Version = "001"; Checksum = 101; ExecutionSuccess = true; ObservedCatalogDigest = validHash "cat-v1" }

        // 1. Clean predecessor, 0 active sessions -> ReadyToMigrate ["002"]
        let ready =
            Admission.auditMigrationLedgerV2
                [v1; v2]
                { LedgerRows = [appliedV1]; CurrentCatalogDigest = validHash "cat-v1"; ActiveNonIdleSessions = 0 }
        Assert.Equal(ReadyToMigrate ["002"], ready)

        // 2. Recorded catalog digest in ledger differs from script's ExpectedCatalogDigest -> CatalogDigestMismatch
        let wrongExpectedCatalog =
            Admission.auditMigrationLedgerV2
                [{ v1 with ExpectedCatalogDigest = validHash "expected-other" }; v2]
                { LedgerRows = [appliedV1]; CurrentCatalogDigest = validHash "cat-v1"; ActiveNonIdleSessions = 0 }
        Assert.Equal(CatalogDigestMismatch ("001", validHash "expected-other", validHash "cat-v1"), wrongExpectedCatalog)

        // 3. Manual DBA ALTER TABLE caused live catalog digest drift after migration -> LiveCatalogDrift
        let drifted =
            Admission.auditMigrationLedgerV2
                [v1; v2]
                { LedgerRows = [appliedV1]; CurrentCatalogDigest = validHash "tampered-catalog"; ActiveNonIdleSessions = 0 }
        Assert.Equal(LiveCatalogDrift (validHash "cat-v1", validHash "tampered-catalog"), drifted)

        // 4. Active non-idle sessions holding locks -> ActiveSessionsBlocking
        let blockedBySessions =
            Admission.auditMigrationLedgerV2
                [v1; v2]
                { LedgerRows = [appliedV1]; CurrentCatalogDigest = validHash "cat-v1"; ActiveNonIdleSessions = 3 }
        Assert.Equal(ActiveSessionsBlocking 3, blockedBySessions)

        // 5. Modified V1 script checksum -> ChecksumDrift
        let checksumDrifted =
            Admission.auditMigrationLedgerV2
                [{ v1 with Checksum = 999 }; v2]
                { LedgerRows = [appliedV1]; CurrentCatalogDigest = validHash "cat-v1"; ActiveNonIdleSessions = 0 }
        Assert.Equal(ChecksumDrift ("001", 999, 101), checksumDrifted)

    [<Fact>]
    member _.``Bundle v2 target admission blocks cross-environment replay and non-rootless targets``() =
        let prodTarget = {
            Environment = "prod-eu-1"
            RuntimeOs = "linux-x86_64"
            RootlessPodman = true
            PolicyDigest = validHash "policy-v1"
        }
        let validBundle = {
            Magic = "HM\u0001"
            FormatVersion = 2
            ReleaseDigest = validHash "rel-v1"
            EvidenceDigest = validHash "ev-v1"
            Target = prodTarget
            Lineage = Active
        }
        Assert.Equal(Ok (), Admission.verifyBundleAdmission validBundle prodTarget)

        let stagingBundle = { validBundle with Target = { prodTarget with Environment = "staging" } }
        match Admission.verifyBundleAdmission stagingBundle prodTarget with
        | Error errs -> Assert.Contains(TargetEnvironmentMismatch ("staging", "prod-eu-1"), errs)
        | Ok () -> failwith "Expected environment mismatch rejection"

    [<Fact>]
    member _.``Pure F# hallmark computation expression DSL builds and validates a 6-stage lifecycle plan``() =
        let result =
            hallmark {
                service "wallet-lifecycle" "1.1.0"
                build "compile" "dotnet-publish" []
                install "container-image" "podman-build" ["compile"]
                configure "app-config" "render-env" ["container-image"]
                migrate "db-migrate" "sql-ledger-v2" ["app-config"]
                verify "assay-gates" "fsassay-pgassay" ["db-migrate"]
                release "seal-bundle" "hm-v2-bundle" ["assay-gates"]
            }
        match result with
        | Ok plan ->
            Assert.Equal<string list>(
                ["compile"; "container-image"; "app-config"; "db-migrate"; "assay-gates"; "seal-bundle"],
                Plan.orderedStageIds plan
            )
        | Error errs -> failwithf "Expected valid plan from hallmark CE DSL, got: %A" errs

    [<Fact>]
    member _.``Flyway CRC32 checksum and history inspection match the Rust Wallet migration kernel``() =
        let sqlLf = "CREATE INDEX operations_wallet_idx ON operations (wallet_id);\n"
        let sqlCrLf = "\uFEFFCREATE INDEX operations_wallet_idx ON operations (wallet_id);\r\n"
        let cs1 = Admission.computeFlywayChecksum sqlLf
        let cs2 = Admission.computeFlywayChecksum sqlCrLf
        Assert.Equal(cs1, cs2)

        let m1 = { Version = 1; Script = "V1__wallet.sql"; Checksum = 111 }
        let m2 = { Version = 2; Script = "V2__index_wallet_operations.sql"; Checksum = cs1 }
        let row1 = { Version = Some "1"; Script = "V1__wallet.sql"; Kind = "SQL"; Checksum = Some 111; Success = true }
        let row2 = { Version = Some "2"; Script = "V2__index_wallet_operations.sql"; Kind = "SQL"; Checksum = Some cs1; Success = true }

        Assert.Equal(ExpectedPredecessor, Admission.inspectFlywayHistory [m1] m2 [row1])
        Assert.Equal(CurrentMigrationCommitted cs1, Admission.inspectFlywayHistory [m1] m2 [row1; row2])
        Assert.Equal(
            ConflictingMigrationChecksum (2, cs1, Some 0),
            Admission.inspectFlywayHistory [m1] m2 [row1; { row2 with Checksum = Some 0 }]
        )

    [<Fact>]
    member _.``Three-owner release coordination holds cutover on pending or withdrawn VERA evidence and deduplicates multi-path downstream handoffs``() =
        let src1 = validHash "pg-src"
        let src2 = validHash "kafka-src"
        let src3 = validHash "os-src"
        let src4 = validHash "report-src"

        let packages = [
            { PackageId = "wallet-ledger-pg"; ComponentName = "Wallet PG"; OwnerTeam = "postgres-ledger-team"; CurrentVersion = "1.1.0"; CandidateVersion = "1.2.0"; SourceDigest = src1; DependsOn = [] }
            { PackageId = "wallet-cdc-kafka"; ComponentName = "Wallet Kafka"; OwnerTeam = "kafka-stream-team"; CurrentVersion = "1.0.0"; CandidateVersion = "1.1.0"; SourceDigest = src2; DependsOn = ["wallet-ledger-pg"] }
            { PackageId = "wallet-search-os"; ComponentName = "Wallet OpenSearch"; OwnerTeam = "opensearch-projection-team"; CurrentVersion = "1.0.0"; CandidateVersion = "1.1.0"; SourceDigest = src3; DependsOn = ["wallet-ledger-pg"; "wallet-cdc-kafka"] }
            { PackageId = "backoffice-reporting"; ComponentName = "Reporting"; OwnerTeam = "reporting-team"; CurrentVersion = "2.0.0"; CandidateVersion = "2.0.0"; SourceDigest = src4; DependsOn = [] }
        ]

        let changeSets = [
            { ChangeSetId = "CS-101"; PackageId = "wallet-ledger-pg"; Classification = AdditiveMigration "V2__index"; Summary = "Add operations_wallet_idx and settlement_trace column" }
            // Duplicate ChangeSetId to verify CF-044 idempotence
            { ChangeSetId = "CS-101"; PackageId = "wallet-ledger-pg"; Classification = AdditiveMigration "V2__index"; Summary = "Add operations_wallet_idx and settlement_trace column" }
        ]

        let rPg = { PackageId = "wallet-ledger-pg"; OwnerTeam = "postgres-ledger-team"; Assay = PostgresAssay; Outcome = Pass; ReceiptDigest = validHash "rec-pg"; EvaluatedAgainstSourceDigest = src1 }
        let rKafka = { PackageId = "wallet-cdc-kafka"; OwnerTeam = "kafka-stream-team"; Assay = FsAssay; Outcome = Pass; ReceiptDigest = validHash "rec-kafka"; EvaluatedAgainstSourceDigest = src2 }
        let rOs1 = { PackageId = "wallet-search-os"; OwnerTeam = "opensearch-projection-team"; Assay = OpenSearchAssay; Outcome = Pass; ReceiptDigest = validHash "rec-os-1"; EvaluatedAgainstSourceDigest = src3 }

        // 1. Missing OpenSearch downstream receipt holds cutover while excluding unrelated backoffice-reporting (CF-043, CF-090)
        let heldPending = Admission.evaluateThreeOwnerCutover "2026.11.0" packages changeSets [rPg; rKafka] []
        Assert.False(heldPending.Admitted)
        Assert.Equal<string list>(["wallet-cdc-kafka"; "wallet-ledger-pg"; "wallet-search-os"], heldPending.AffectedPackages)
        Assert.Equal<string list>(["backoffice-reporting"], heldPending.ExcludedUnrelatedPackages)
        Assert.Single(heldPending.ReleaseNotes) |> ignore
        let osHandoff = heldPending.Handoffs |> List.find (fun h -> h.ConsumingPackageId = "wallet-search-os")
        Assert.Equal("opensearch-projection-team", osHandoff.ResponsibleOwner)
        Assert.Equal<string list>(
            ["wallet-ledger-pg -> wallet-cdc-kafka -> wallet-search-os"; "wallet-ledger-pg -> wallet-search-os"],
            osHandoff.DependencyPaths
        )

        // 2. When all three owners supply passing receipts, cutover is admitted and all 4 role views are populated (CF-045)
        let admitted = Admission.evaluateThreeOwnerCutover "2026.11.0" packages changeSets [rPg; rKafka; rOs1] []
        Assert.True(admitted.Admitted)
        Assert.Empty(admitted.Blockers)
        Assert.NotEmpty(admitted.RoleViews.DeveloperView)
        Assert.NotEmpty(admitted.RoleViews.QaView)
        Assert.NotEmpty(admitted.RoleViews.OperatorView)
        Assert.NotEmpty(admitted.RoleViews.ProductView)

        // 3. Withdrawing the OpenSearch compatibility receipt under VERA holds cutover and surfaces the correction (CF-077, CF-090)
        let withdrawal = {
            CorrectionId = "VERA-CORR-01"
            AffectedReceiptDigest = rOs1.ReceiptDigest
            Actor = "opensearch-projection-team"
            Reason = "Mapping strictness regression on settlement_trace field"
            EffectiveAt = "2026-11-24T15:00:00Z"
            Kind = ReceiptWithdrawn
        }
        // Even if rOs1 is replayed twice, the withdrawal correction remains in effect
        let heldAfterWithdrawal = Admission.evaluateThreeOwnerCutover "2026.11.0" packages changeSets [rPg; rKafka; rOs1; rOs1] [withdrawal]
        Assert.False(heldAfterWithdrawal.Admitted)
        Assert.Contains(heldAfterWithdrawal.Blockers, fun b -> b.Contains("VERA-CORR-01") && b.Contains("withdrawn"))

        // 4. Supplying a fresh replacement receipt from opensearch-projection-team re-admits cutover
        let rOs2 = { rOs1 with ReceiptDigest = validHash "rec-os-2" }
        let readmitted = Admission.evaluateThreeOwnerCutover "2026.11.0" packages changeSets [rPg; rKafka; rOs1; rOs2] [withdrawal]
        Assert.True(readmitted.Admitted)
