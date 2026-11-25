namespace CanonForge.Lab

open System
open System.Text.RegularExpressions

type StageId = private StageId of string

module StageId =
    let value (StageId value) = value

type StageKind = Build | Install | Configure | Migrate | Verify | Release
type EffectKind = Observation | DesiredState | Imperative
type Repetition = Reconcile | ExecuteOnce
type EvidenceStage = Preview | Admission | Execution

type InputDraft = { Name: string; Digest: string }

type StageDraft = {
    Id: string
    Kind: StageKind
    Effect: EffectKind
    Repetition: Repetition
    AdapterDigest: string
    ActionDigest: string
    Inputs: InputDraft list
    DependsOn: string list
}

type PlanDraft = {
    SchemaVersion: int
    Name: string
    ProducerDigest: string
    TargetDigest: string
    ConfigurationDigest: string
    PolicyDigest: string
    Stages: StageDraft list
}

type PlanningError =
    | UnsupportedSchema of int
    | EmptyPlan
    | TooManyStages of int
    | InvalidIdentity of field: string
    | InvalidDigest of field: string
    | DuplicateStage of string
    | DuplicateInput of stage: string * input: string
    | DuplicateDependency of stage: string * dependency: string
    | MissingDependency of stage: string * dependency: string
    | UnsafeRepetition of stage: string
    | VerificationMustObserve of stage: string
    | CyclicDependencies of stages: string list
    | ReleaseNeedsVerification of stage: string
    | ReleaseVerificationGap of stage: string * unverifiedStages: string list

type PlannedStage = private {
    Id: StageId
    Kind: StageKind
}

type ValidatedPlan = private {
    Stages: PlannedStage list
    Definition: PlanDraft
}

type CheckpointClaim = {
    PlanDigest: string
    StageId: string
    EvidenceStage: EvidenceStage
}

type CheckpointError = NotExecutionEvidence | PlanIdentityChanged | StageNotInPlan

module Planning =
    let private validIdentity value =
        not (String.IsNullOrWhiteSpace value)
        && Regex.IsMatch(value, "\\A[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)

    let private validDigest value =
        not (isNull value)
        && Regex.IsMatch(value, "\\Asha256:[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)

    let private duplicates values =
        values |> List.countBy id |> List.choose (fun (value, count) -> if count > 1 then Some value else None)

    let private sortStages (stages: StageDraft list) =
        let rec visit completed remaining ordered =
            match remaining with
            | [] -> Ok ordered
            | _ ->
                let ready, waiting =
                    remaining
                    |> List.partition (fun stage -> stage.DependsOn |> List.forall (fun dependency -> Set.contains dependency completed))
                match ready with
                | [] -> Error [CyclicDependencies (waiting |> List.map _.Id |> List.sort)]
                | _ ->
                    let batch = ready |> List.sortBy _.Id
                    let completed = batch |> List.fold (fun state stage -> Set.add stage.Id state) completed
                    visit completed waiting (ordered @ batch)
        visit Set.empty stages []

    let validate (draft: PlanDraft) =
        let identity field value = if validIdentity value then [] else [InvalidIdentity field]
        let digest field value = if validDigest value then [] else [InvalidDigest field]
        let ids = draft.Stages |> List.map _.Id |> Set.ofList
        let errors = [
            if draft.SchemaVersion <> 1 then UnsupportedSchema draft.SchemaVersion
            if draft.Stages.IsEmpty then EmptyPlan
            if draft.Stages.Length > 256 then TooManyStages draft.Stages.Length
            yield! identity "name" draft.Name
            for field, value in ["producer", draft.ProducerDigest; "target", draft.TargetDigest;
                                 "configuration", draft.ConfigurationDigest; "policy", draft.PolicyDigest] do
                yield! digest field value
            for duplicate in draft.Stages |> List.map _.Id |> duplicates do DuplicateStage duplicate
            for stage in draft.Stages do
                yield! identity "stage.id" stage.Id
                yield! digest (stage.Id + ".adapter") stage.AdapterDigest
                yield! digest (stage.Id + ".action") stage.ActionDigest
                if stage.Effect = Imperative && stage.Repetition = Reconcile then UnsafeRepetition stage.Id
                if stage.Kind = Verify && stage.Effect <> Observation then VerificationMustObserve stage.Id
                for duplicate in stage.DependsOn |> duplicates do DuplicateDependency (stage.Id, duplicate)
                for dependency in stage.DependsOn do
                    yield! identity (stage.Id + ".dependency") dependency
                    if not (Set.contains dependency ids) then MissingDependency (stage.Id, dependency)
                for duplicate in stage.Inputs |> List.map _.Name |> duplicates do DuplicateInput (stage.Id, duplicate)
                for input in stage.Inputs do
                    yield! identity (stage.Id + ".input.name") input.Name
                    yield! digest (stage.Id + ".input.digest") input.Digest
        ]
        if not errors.IsEmpty then Error errors
        else
            sortStages draft.Stages
            |> Result.bind (fun ordered ->
                let byId = draft.Stages |> List.map (fun stage -> stage.Id, stage) |> Map.ofList
                let ancestors =
                    ordered
                    |> List.fold (fun known stage ->
                        let dependencies =
                            stage.DependsOn
                            |> List.map (fun dependency -> Set.add dependency (Map.find dependency known))
                            |> Set.unionMany
                        Map.add stage.Id dependencies known) Map.empty
                let releaseErrors = [
                    for stage in draft.Stages do
                        if stage.Kind = Release then
                            let dependencies = Map.find stage.Id ancestors
                            let gates = dependencies |> Set.filter (fun dependency -> byId[dependency].Kind = Verify)
                            if gates.IsEmpty then ReleaseNeedsVerification stage.Id
                            else
                                let covered = gates |> Seq.map (fun gate -> Map.find gate ancestors) |> Set.unionMany
                                let unverified =
                                    dependencies
                                    |> Set.filter (fun dependency ->
                                        let ancestor = byId[dependency]
                                        ancestor.Kind <> Release && ancestor.Effect <> Observation
                                        && not (Set.contains dependency covered))
                                    |> Set.toList
                                if not unverified.IsEmpty then ReleaseVerificationGap (stage.Id, unverified)
                ]
                if not releaseErrors.IsEmpty then Error releaseErrors
                else
                    Ok {
                        Stages = ordered |> List.map (fun stage -> { Id = StageId stage.Id; Kind = stage.Kind })
                        Definition = draft
                    })

    let orderedStageIds plan = plan.Stages |> List.map (fun stage -> StageId.value stage.Id)
    let internal definition plan = plan.Definition

module Checkpoint =
    /// Compatibility only; the caller must independently establish the producer
    /// and authenticity of any supplied execution claim.
    let validate plan expectedPlanDigest claim =
        if claim.EvidenceStage <> Execution then Error NotExecutionEvidence
        elif claim.PlanDigest <> expectedPlanDigest then Error PlanIdentityChanged
        elif not (Planning.orderedStageIds plan |> List.contains claim.StageId) then Error StageNotInPlan
        else Ok ()
