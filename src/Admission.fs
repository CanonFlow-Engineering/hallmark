namespace CanonForge.Lab

open System
open CanonForge.Lab.Serialization

/// Course 1: Gate B 4-State Dominance Semilattice & VERA Lineage Authority.
type GateOutcome =
    | Pass
    | Inconclusive
    | Fail
    | ToolFailure

type AssayKind =
    | FsAssay
    | PostgresAssay
    | OpenSearchAssay
    | DuckDbAssay
    | OracleAssay
    | RustAssay
    | PythonAssay
    | CSharpAssay

type AssayReceipt = {
    Assay: AssayKind
    Outcome: GateOutcome
    ReceiptDigest: string
}

type BoundAssayReceipt = {
    Assay: AssayKind
    Outcome: GateOutcome
    ReceiptDigest: string
    PlanDigest: string
    SourceDigest: string
    PolicyDigest: string
    TargetDigest: string
}

type ReleaseIdentityBinding = {
    PlanDigest: string
    SourceDigest: string
    PolicyDigest: string
    TargetDigest: string
}

type VeraLineageStatus =
    | Active
    | Superseded of byReleaseId: string
    | Withdrawn of reason: string

type GateBRejection =
    | MissingRequiredAssays of AssayKind list
    | InvalidReceiptDigest of AssayKind
    | MismatchedReceiptIdentity of AssayKind * field: string
    | NonPassingOutcome of GateOutcome
    | ReleaseSuperseded of byReleaseId: string
    | ReleaseWithdrawn of reason: string

type GateBVerdict = {
    AggregateOutcome: GateOutcome
    Admitted: bool
    Rejections: GateBRejection list
}

/// Course 2: Migration Ledger v2 (READ_LEDGER_V2) with ExpectedCatalogDigest verification.
type MigrationScriptSpec = {
    Version: string
    Checksum: int
    ExpectedCatalogDigest: string
}

type LedgerRowV2 = {
    Version: string
    Checksum: int
    ExecutionSuccess: bool
    ObservedCatalogDigest: string
}

type LiveDatabaseObservation = {
    LedgerRows: LedgerRowV2 list
    CurrentCatalogDigest: string
    ActiveNonIdleSessions: int
}

type MigrationAuditDecision =
    | ReadyToMigrate of pendingVersions: string list
    | AlreadyCurrent
    | FailedPriorMigration of version: string
    | ChecksumDrift of version: string * expected: int * observed: int
    | CatalogDigestMismatch of version: string * expectedDigest: string * observedDigest: string
    | LiveCatalogDrift of expectedDigest: string * observedDigest: string
    | ActiveSessionsBlocking of activeCount: int
    | OutOfOrderHistory of detail: string

/// Course 3: Bundle v2 & Target Fingerprint Admission.
type TargetFingerprint = {
    Environment: string
    RuntimeOs: string
    RootlessPodman: bool
    PolicyDigest: string
}

type BundleV2Header = {
    Magic: string
    FormatVersion: int
    ReleaseDigest: string
    EvidenceDigest: string
    Target: TargetFingerprint
    Lineage: VeraLineageStatus
}

type BundleAdmissionError =
    | InvalidBundleMagic of string
    | UnsupportedBundleVersion of int
    | InvalidBundleDigest of field: string
    | TargetEnvironmentMismatch of expected: string * actual: string
    | TargetOsMismatch of expected: string * actual: string
    | RootlessPodmanRequired
    | TargetPolicyMismatch of expected: string * actual: string
    | BundleSuperseded of byReleaseId: string
    | BundleWithdrawn of reason: string

type FlywayMigration = {
    Version: int
    Script: string
    Checksum: int
}

type FlywayLedgerEntry = {
    Version: string option
    Script: string
    Kind: string
    Checksum: int option
    Success: bool
}

type FlywayHistoryState =
    | ExpectedPredecessor
    | CurrentMigrationCommitted of checksum: int
    | ConflictingMigrationChecksum of version: int * expected: int * observed: int option
    | DivergedFlywayHistory of reason: string

/// Course 5: Three-Owner Release Coordination, Change-Set Impact, Downstream Handoffs & VERA Corrections (CF-043..CF-045, CF-077, CF-090).
type CompatibilityClassification =
    | BackwardCompatible
    | AdditiveMigration of migrationVersion: string
    | BreakingContract of migrationVersion: string
    | Unclassified

type PackageNode = {
    PackageId: string
    ComponentName: string
    OwnerTeam: string
    CurrentVersion: string
    CandidateVersion: string
    SourceDigest: string
    DependsOn: string list
}

type ExplicitChangeSet = {
    ChangeSetId: string
    PackageId: string
    Classification: CompatibilityClassification
    Summary: string
}

type ComponentReceipt = {
    PackageId: string
    OwnerTeam: string
    Assay: AssayKind
    Outcome: GateOutcome
    ReceiptDigest: string
    EvaluatedAgainstSourceDigest: string
}

type EvidenceCorrectionKind =
    | ReceiptSuperseded of replacementReceiptDigest: string
    | ReceiptWithdrawn

type EvidenceCorrection = {
    CorrectionId: string
    AffectedReceiptDigest: string
    Actor: string
    Reason: string
    EffectiveAt: string
    Kind: EvidenceCorrectionKind
}

type HandoffEvidenceStatus =
    | CompatibilitySatisfied of receiptDigest: string
    | CompatibilityPending of reason: string
    | CompatibilityCorrectionRequired of correctionId: string * reason: string

type DownstreamHandoff = {
    OriginatingPackageId: string
    OriginatingVersion: string
    ConsumingPackageId: string
    ConsumingVersion: string
    ResponsibleOwner: string
    DependencyPaths: string list
    OwedAction: string
    Status: HandoffEvidenceStatus
    UpstreamNote: string
}

type RoleLifecycleViews = {
    DeveloperView: string list
    QaView: string list
    OperatorView: string list
    ProductView: string list
}

type ThreeOwnerCutoverVerdict = {
    ReleaseVersion: string
    Admitted: bool
    AffectedPackages: string list
    ExcludedUnrelatedPackages: string list
    Handoffs: DownstreamHandoff list
    Blockers: string list
    ReleaseNotes: string list
    RoleViews: RoleLifecycleViews
}

module Admission =
    let private isValidSha256 (value: string) =
        not (isNull value)
        && value.Length = 71
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && (value.Substring(7) |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))

    /// Dominance semilattice join: ToolFailure > Fail > Inconclusive > Pass.
    let combineOutcome (left: GateOutcome) (right: GateOutcome) =
        match left, right with
        | ToolFailure, _ | _, ToolFailure -> ToolFailure
        | Fail, _ | _, Fail -> Fail
        | Inconclusive, _ | _, Inconclusive -> Inconclusive
        | Pass, Pass -> Pass

    let evaluateGateB
        (requiredAssays: AssayKind list)
        (receipts: AssayReceipt list)
        (lineage: VeraLineageStatus)
        : GateBVerdict =
        let observedKinds = receipts |> List.map _.Assay |> Set.ofList
        let missing =
            requiredAssays
            |> List.filter (fun required -> not (Set.contains required observedKinds))
            |> List.distinct

        let invalidDigests =
            receipts
            |> List.choose (fun r ->
                if isValidSha256 r.ReceiptDigest then None
                else Some (InvalidReceiptDigest r.Assay))

        let foldedOutcome =
            match receipts with
            | [] -> Inconclusive
            | head :: tail ->
                tail |> List.fold (fun acc item -> combineOutcome acc item.Outcome) head.Outcome

        let aggregate =
            if not missing.IsEmpty then combineOutcome foldedOutcome Inconclusive
            elif not invalidDigests.IsEmpty then combineOutcome foldedOutcome ToolFailure
            else foldedOutcome

        let lineageRejections =
            match lineage with
            | Active -> []
            | Superseded byId -> [ReleaseSuperseded byId]
            | Withdrawn reason -> [ReleaseWithdrawn reason]

        let rejections = [
            if not missing.IsEmpty then MissingRequiredAssays missing
            yield! invalidDigests
            if aggregate <> Pass then NonPassingOutcome aggregate
            yield! lineageRejections
        ]

        {
            AggregateOutcome = aggregate
            Admitted = (aggregate = Pass && rejections.IsEmpty)
            Rejections = rejections
        }

    let evaluateBoundReleaseGate
        (expectedBinding: ReleaseIdentityBinding)
        (requiredAssays: AssayKind list)
        (receipts: BoundAssayReceipt list)
        (lineage: VeraLineageStatus)
        : GateBVerdict =
        let baseReceipts =
            receipts
            |> List.map (fun r -> { Assay = r.Assay; Outcome = r.Outcome; ReceiptDigest = r.ReceiptDigest })
        let baseVerdict = evaluateGateB requiredAssays baseReceipts lineage
        let bindingMismatches =
            receipts
            |> List.collect (fun r -> [
                if r.PlanDigest <> expectedBinding.PlanDigest then
                    MismatchedReceiptIdentity (r.Assay, "planDigest")
                if r.SourceDigest <> expectedBinding.SourceDigest then
                    MismatchedReceiptIdentity (r.Assay, "sourceDigest")
                if r.PolicyDigest <> expectedBinding.PolicyDigest then
                    MismatchedReceiptIdentity (r.Assay, "policyDigest")
                if r.TargetDigest <> expectedBinding.TargetDigest then
                    MismatchedReceiptIdentity (r.Assay, "targetDigest")
            ])
        let aggregate =
            if bindingMismatches.IsEmpty then baseVerdict.AggregateOutcome
            else combineOutcome baseVerdict.AggregateOutcome Inconclusive
        let allRejections = baseVerdict.Rejections @ bindingMismatches
        {
            AggregateOutcome = aggregate
            Admitted = (aggregate = Pass && allRejections.IsEmpty)
            Rejections = allRejections
        }

    let auditMigrationLedgerV2
        (scripts: MigrationScriptSpec list)
        (observation: LiveDatabaseObservation)
        : MigrationAuditDecision =
        let failedRow = observation.LedgerRows |> List.tryFind (fun row -> not row.ExecutionSuccess)
        match failedRow with
        | Some row -> FailedPriorMigration row.Version
        | None ->
            let rec comparePrefix (expectedList: MigrationScriptSpec list) (observedList: LedgerRowV2 list) =
                match expectedList, observedList with
                | exp :: expTail, obs :: obsTail ->
                    if exp.Version <> obs.Version then
                        Error (OutOfOrderHistory ("Expected version " + exp.Version + " but observed " + obs.Version))
                    elif exp.Checksum <> obs.Checksum then
                        Error (ChecksumDrift (exp.Version, exp.Checksum, obs.Checksum))
                    elif exp.ExpectedCatalogDigest <> obs.ObservedCatalogDigest then
                        Error (CatalogDigestMismatch (exp.Version, exp.ExpectedCatalogDigest, obs.ObservedCatalogDigest))
                    else
                        comparePrefix expTail obsTail
                | remainingScripts, [] ->
                    Ok (remainingScripts |> List.map _.Version)
                | [], extraObserved :: _ ->
                    Error (OutOfOrderHistory ("Database has unknown applied version " + extraObserved.Version))

            match comparePrefix scripts observation.LedgerRows with
            | Error rejection -> rejection
            | Ok pendingVersions ->
                let lastApplied = observation.LedgerRows |> List.tryLast
                let catalogDrift =
                    match lastApplied with
                    | Some lastRow when lastRow.ObservedCatalogDigest <> observation.CurrentCatalogDigest ->
                        Some (LiveCatalogDrift (lastRow.ObservedCatalogDigest, observation.CurrentCatalogDigest))
                    | _ -> None
                match catalogDrift with
                | Some drift -> drift
                | None ->
                    if pendingVersions.IsEmpty then
                        AlreadyCurrent
                    elif observation.ActiveNonIdleSessions > 0 then
                        ActiveSessionsBlocking observation.ActiveNonIdleSessions
                    else
                        ReadyToMigrate pendingVersions

    let verifyBundleAdmission
        (bundle: BundleV2Header)
        (destination: TargetFingerprint)
        : Result<unit, BundleAdmissionError list> =
        let errors = [
            if bundle.Magic <> "HM\u0001" then
                InvalidBundleMagic bundle.Magic
            if bundle.FormatVersion <> 2 then
                UnsupportedBundleVersion bundle.FormatVersion
            if not (isValidSha256 bundle.ReleaseDigest) then
                InvalidBundleDigest "releaseDigest"
            if not (isValidSha256 bundle.EvidenceDigest) then
                InvalidBundleDigest "evidenceDigest"
            if bundle.Target.Environment <> destination.Environment then
                TargetEnvironmentMismatch (bundle.Target.Environment, destination.Environment)
            if bundle.Target.RuntimeOs <> destination.RuntimeOs then
                TargetOsMismatch (bundle.Target.RuntimeOs, destination.RuntimeOs)
            if not bundle.Target.RootlessPodman || not destination.RootlessPodman then
                RootlessPodmanRequired
            if bundle.Target.PolicyDigest <> destination.PolicyDigest then
                TargetPolicyMismatch (bundle.Target.PolicyDigest, destination.PolicyDigest)
            match bundle.Lineage with
            | Active -> ()
            | Superseded byId -> BundleSuperseded byId
            | Withdrawn reason -> BundleWithdrawn reason
        ]
        if errors.IsEmpty then Ok () else Error errors

    let private crc32Byte (crc: uint32) (b: byte) : uint32 =
        let step (acc: uint32) =
            if (acc &&& 1u) <> 0u then (acc >>> 1) ^^^ 0xEDB88320u
            else acc >>> 1
        crc ^^^ uint32 b
        |> step |> step |> step |> step
        |> step |> step |> step |> step

    /// Flyway hashes UTF-8 SQL without its leading BOM or line terminators (\r / \n).
    let computeFlywayChecksum (sql: string) : int =
        let clean =
            if not (isNull sql) && sql.StartsWith("\uFEFF", StringComparison.Ordinal) then
                sql.Substring(1)
            elif isNull sql then ""
            else sql
        let finalCrc =
            clean.Split([| '\r'; '\n' |])
            |> Array.fold (fun crc line ->
                System.Text.Encoding.UTF8.GetBytes(line)
                |> Array.fold crc32Byte crc) 0xFFFFFFFFu
        int (finalCrc ^^^ 0xFFFFFFFFu)

    let inspectFlywayHistory
        (preceding: FlywayMigration list)
        (current: FlywayMigration)
        (observed: FlywayLedgerEntry list)
        : FlywayHistoryState =
        if observed.Length < preceding.Length then
            DivergedFlywayHistory "Observed Flyway history has fewer entries than expected predecessors"
        else
            let prefix, rest = List.splitAt preceding.Length observed
            let sameScript (row: FlywayLedgerEntry) (m: FlywayMigration) =
                row.Version = Some (string m.Version)
                && row.Script = m.Script
                && row.Kind = "SQL"
                && row.Success
            let priorAgrees =
                List.zip prefix preceding
                |> List.forall (fun (row, m) -> sameScript row m && row.Checksum = Some m.Checksum)
            if not priorAgrees then
                DivergedFlywayHistory "Observed Flyway predecessor history diverged from frozen SQL sequence"
            else
                match rest with
                | [] -> ExpectedPredecessor
                | [row] when sameScript row current ->
                    match row.Checksum with
                    | Some cs when cs = current.Checksum -> CurrentMigrationCommitted cs
                    | other -> ConflictingMigrationChecksum (current.Version, current.Checksum, other)
                | _ -> DivergedFlywayHistory "Observed Flyway history contains unexpected trailing or failed rows"

    /// Compute all unique dependency paths from any changed upstream package to a target package (CF-043, CF-090).
    let findDependencyPaths (packages: PackageNode list) (changedIds: Set<string>) (targetId: string) : string list =
        let byId = packages |> List.map (fun p -> p.PackageId, p) |> Map.ofList
        let rec dfs (visited: Set<string>) (currentId: string) : string list list =
            match Map.tryFind currentId byId with
            | None -> []
            | Some pkg ->
                let direct =
                    if Set.contains currentId changedIds then [[currentId]]
                    else []
                let upstreamPaths =
                    pkg.DependsOn
                    |> List.filter (fun dep -> not (Set.contains dep visited))
                    |> List.collect (fun dep ->
                        dfs (Set.add dep visited) dep
                        |> List.map (fun path -> path @ [currentId]))
                direct @ upstreamPaths
        dfs (Set.singleton targetId) targetId
        |> List.filter (fun path -> not path.IsEmpty && Set.contains (List.head path) changedIds)
        |> List.map (String.concat " -> ")
        |> List.distinct
        |> List.sort

    let evaluateThreeOwnerCutover
        (releaseVersion: string)
        (packages: PackageNode list)
        (changeSets: ExplicitChangeSet list)
        (receipts: ComponentReceipt list)
        (corrections: EvidenceCorrection list)
        : ThreeOwnerCutoverVerdict =
        let dedupedChanges =
            changeSets
            |> List.distinctBy _.ChangeSetId
        let changedIds =
            dedupedChanges
            |> List.map _.PackageId
            |> Set.ofList

        let pathsByPackage =
            packages
            |> List.map (fun p -> p.PackageId, findDependencyPaths packages changedIds p.PackageId)
            |> Map.ofList

        let affectedPackages =
            packages
            |> List.filter (fun p ->
                Set.contains p.PackageId changedIds
                || not (Map.find p.PackageId pathsByPackage).IsEmpty)

        let affectedIdSet = affectedPackages |> List.map _.PackageId |> Set.ofList
        let excludedPackages =
            packages
            |> List.filter (fun p -> not (Set.contains p.PackageId affectedIdSet))
            |> List.map _.PackageId
            |> List.sort

        let unclassifiedBlockers =
            dedupedChanges
            |> List.choose (fun cs ->
                match cs.Classification with
                | Unclassified -> Some (sprintf "ChangeSet %s for package %s is missing compatibility classification" cs.ChangeSetId cs.PackageId)
                | _ -> None)

        let correctionByDigest =
            corrections
            |> List.map (fun c -> c.AffectedReceiptDigest, c)
            |> Map.ofList

        let resolvePackageStatus (pkg: PackageNode) : HandoffEvidenceStatus =
            let matchingReceipts =
                receipts
                |> List.filter (fun r ->
                    r.PackageId = pkg.PackageId
                    && r.OwnerTeam = pkg.OwnerTeam
                    && r.EvaluatedAgainstSourceDigest = pkg.SourceDigest)
            match matchingReceipts with
            | [] ->
                CompatibilityPending ("Missing compatibility receipt from owner " + pkg.OwnerTeam + " for " + pkg.PackageId + "@" + pkg.CandidateVersion)
            | candidates ->
                // Check if there is an active, non-corrected passing receipt
                let activePassing =
                    candidates
                    |> List.tryFind (fun r ->
                        r.Outcome = Pass
                        && isValidSha256 r.ReceiptDigest
                        && not (Map.containsKey r.ReceiptDigest correctionByDigest))
                match activePassing with
                | Some validReceipt ->
                    CompatibilitySatisfied validReceipt.ReceiptDigest
                | None ->
                    let corrected =
                        candidates
                        |> List.tryPick (fun r ->
                            Map.tryFind r.ReceiptDigest correctionByDigest
                            |> Option.map (fun corr ->
                                match corr.Kind with
                                | ReceiptWithdrawn ->
                                    CompatibilityCorrectionRequired (corr.CorrectionId, sprintf "Receipt %s withdrawn by %s: %s" r.ReceiptDigest corr.Actor corr.Reason)
                                | ReceiptSuperseded replacement ->
                                    CompatibilityCorrectionRequired (corr.CorrectionId, sprintf "Receipt %s superseded by %s (%s)" r.ReceiptDigest replacement corr.Reason)))
                    match corrected with
                    | Some c -> c
                    | None ->
                        let firstNonPass = candidates |> List.head
                        CompatibilityPending (sprintf "Receipt %s for %s has non-passing outcome %A" firstNonPass.ReceiptDigest pkg.PackageId firstNonPass.Outcome)

        let byId = packages |> List.map (fun p -> p.PackageId, p) |> Map.ofList
        let changeByPkg = dedupedChanges |> List.map (fun c -> c.PackageId, c) |> Map.ofList

        let handoffs =
            affectedPackages
            |> List.map (fun pkg ->
                let paths = Map.find pkg.PackageId pathsByPackage
                let originId =
                    match paths with
                    | firstPath :: _ ->
                        let parts = firstPath.Split(" -> ")
                        parts[0]
                    | [] -> pkg.PackageId
                let originPkg = Map.find originId byId
                let upstreamNote =
                    match Map.tryFind originId changeByPkg with
                    | Some cs -> sprintf "[%s] %s (%s -> %s): %s" cs.ChangeSetId originPkg.PackageId originPkg.CurrentVersion originPkg.CandidateVersion cs.Summary
                    | None -> sprintf "%s (%s -> %s)" originPkg.PackageId originPkg.CurrentVersion originPkg.CandidateVersion
                let status = resolvePackageStatus pkg
                let owedAction =
                    match status with
                    | CompatibilitySatisfied _ -> "No action owed; compatibility evidence verified"
                    | CompatibilityPending _ -> "Supply passing Assay compatibility receipt bound to " + pkg.SourceDigest
                    | CompatibilityCorrectionRequired (_, reason) -> "Re-evaluate compatibility after VERA correction (" + reason + ")"
                {
                    OriginatingPackageId = originPkg.PackageId
                    OriginatingVersion = originPkg.CandidateVersion
                    ConsumingPackageId = pkg.PackageId
                    ConsumingVersion = pkg.CandidateVersion
                    ResponsibleOwner = pkg.OwnerTeam
                    DependencyPaths = paths
                    OwedAction = owedAction
                    Status = status
                    UpstreamNote = upstreamNote
                })

        let handoffBlockers =
            handoffs
            |> List.choose (fun h ->
                match h.Status with
                | CompatibilitySatisfied _ -> None
                | CompatibilityPending reason ->
                    Some (sprintf "Owner %s (%s): %s" h.ResponsibleOwner h.ConsumingPackageId reason)
                | CompatibilityCorrectionRequired (cid, reason) ->
                    Some (sprintf "Owner %s (%s) [Correction %s]: %s" h.ResponsibleOwner h.ConsumingPackageId cid reason))

        let allBlockers = unclassifiedBlockers @ handoffBlockers
        let admitted = allBlockers.IsEmpty && not affectedPackages.IsEmpty

        let classLabel = function
            | BackwardCompatible -> "backward-compatible"
            | AdditiveMigration ver -> "additive-migration(" + ver + ")"
            | BreakingContract ver -> "breaking-contract(" + ver + ")"
            | Unclassified -> "unclassified"

        let releaseNotes =
            dedupedChanges
            |> List.sortBy _.ChangeSetId
            |> List.map (fun cs ->
                let pkg = Map.find cs.PackageId byId
                sprintf "Release %s · %s (%s -> %s) [%s] owner=%s: %s"
                    releaseVersion pkg.PackageId pkg.CurrentVersion pkg.CandidateVersion (classLabel cs.Classification) pkg.OwnerTeam cs.Summary)

        let roleViews = {
            DeveloperView =
                handoffs
                |> List.map (fun h ->
                    sprintf "pkg=%s version=%s owner=%s paths=[%s] note=%s"
                        h.ConsumingPackageId h.ConsumingVersion h.ResponsibleOwner (String.concat "; " h.DependencyPaths) h.UpstreamNote)
            QaView =
                handoffs
                |> List.map (fun h ->
                    let stateStr =
                        match h.Status with
                        | CompatibilitySatisfied digest -> "PASS(" + digest + ")"
                        | CompatibilityPending reason -> "PENDING(" + reason + ")"
                        | CompatibilityCorrectionRequired (cid, reason) -> "CORRECTION_REQUIRED(" + cid + ": " + reason + ")"
                    sprintf "pkg=%s candidate=%s evidence=%s" h.ConsumingPackageId h.ConsumingVersion stateStr)
            OperatorView =
                [
                    sprintf "release=%s cutover=%s affected=%d excluded=%d blockers=%d"
                        releaseVersion (if admitted then "ADMITTED" else "HELD") affectedPackages.Length excludedPackages.Length allBlockers.Length
                    yield! allBlockers |> List.map (fun b -> "BLOCKER: " + b)
                ]
            ProductView =
                [
                    sprintf "Release %s status: %s (%d change sets, %d verified components)"
                        releaseVersion
                        (if admitted then "Ready for cutover" else "Held pending compatibility evidence")
                        dedupedChanges.Length
                        (handoffs |> List.filter (fun h -> match h.Status with CompatibilitySatisfied _ -> true | _ -> false) |> List.length)
                    yield! releaseNotes
                ]
        }

        {
            ReleaseVersion = releaseVersion
            Admitted = admitted
            AffectedPackages = affectedPackages |> List.map _.PackageId |> List.sort
            ExcludedUnrelatedPackages = excludedPackages
            Handoffs = handoffs
            Blockers = allBlockers
            ReleaseNotes = releaseNotes
            RoleViews = roleViews
        }

/// Course 4: Pure F# Computation Expression DSL (`hallmark { ... }`).
type HallmarkBuilder() =
    member _.Yield(()) : PlanDraft =
        {
            SchemaVersion = 1
            Name = "hallmark-service"
            ProducerDigest = ContentDigest.ofText "hallmark-lifecycle-engine-fsharp-v1"
            TargetDigest = ContentDigest.ofText "rootless-podman-linux-x86_64"
            ConfigurationDigest = ContentDigest.ofText "default-config-v1"
            PolicyDigest = ContentDigest.ofText "fsassay-l4-policy-v1"
            Stages = []
        }

    member private _.AddStage(draft: PlanDraft, id: string, kind: StageKind, effect: EffectKind, repetition: Repetition, actionTag: string, dependsOn: string list) =
        let stage = {
            Id = id
            Kind = kind
            Effect = effect
            Repetition = repetition
            AdapterDigest = ContentDigest.ofText ("podman-adapter:" + id)
            ActionDigest = ContentDigest.ofText ("action:" + id + ":" + actionTag)
            Inputs = [{ Name = "source-closure"; Digest = ContentDigest.ofText ("input:" + draft.Name) }]
            DependsOn = dependsOn
        }
        { draft with Stages = draft.Stages @ [stage] }

    [<CustomOperation("service")>]
    member _.Service(draft: PlanDraft, name: string, configTag: string) =
        {
            draft with
                Name = name
                ConfigurationDigest = ContentDigest.ofText ("config:" + name + ":" + configTag)
        }

    [<CustomOperation("build")>]
    member this.Build(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Build, Imperative, ExecuteOnce, actionTag, dependsOn)

    [<CustomOperation("install")>]
    member this.Install(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Install, DesiredState, Reconcile, actionTag, dependsOn)

    [<CustomOperation("configure")>]
    member this.Configure(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Configure, DesiredState, Reconcile, actionTag, dependsOn)

    [<CustomOperation("migrate")>]
    member this.Migrate(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Migrate, Imperative, ExecuteOnce, actionTag, dependsOn)

    [<CustomOperation("verify")>]
    member this.Verify(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Verify, Observation, Reconcile, actionTag, dependsOn)

    [<CustomOperation("release")>]
    member this.Release(draft: PlanDraft, id: string, actionTag: string, dependsOn: string list) =
        this.AddStage(draft, id, Release, Imperative, ExecuteOnce, actionTag, dependsOn)

    member _.Run(draft: PlanDraft) =
        Plan.validate draft

[<AutoOpen>]
module HallmarkDsl =
    let hallmark = HallmarkBuilder()
