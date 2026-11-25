namespace CanonForge.Lab.Tests

open System.Text.Json
open Xunit
open CanonForge.Lab
open CanonForge.Lab.Serialization

module Fixtures =
    let accepted draft =
        match Plan.validate draft with
        | Ok plan -> plan
        | Error errors -> failwithf "Unexpected rejection: %A" errors

    let rejected expected draft =
        match Plan.validate draft with
        | Ok _ -> failwith "Expected rejection"
        | Error errors -> Assert.Contains(expected, errors)

    let editStage (id: string) (change: StageDraft -> StageDraft) (draft: PlanDraft) =
        { draft with Stages = draft.Stages |> List.map (fun stage -> if stage.Id = id then change stage else stage) }

open Fixtures

type PlanningTests() =
    [<Fact>]
    member _.``all six stage kinds plan in dependency order``() =
        let plan = accepted Example.sixStagePlan
        Assert.Equal<string list>(["build"; "install"; "configure"; "migrate"; "verify"; "release"], Plan.orderedStageIds plan)

    [<Fact>]
    member _.``document ordering does not change plan identity``() =
        let original = accepted Example.sixStagePlan
        let reversed = accepted { Example.sixStagePlan with Stages = List.rev Example.sixStagePlan.Stages }
        Assert.Equal(Plan.digest original, Plan.digest reversed)
        Assert.Equal(Plan.canonicalJson original, Plan.canonicalJson reversed)

    [<Fact>]
    member _.``independent stages have stable ordering``() =
        let template = Example.sixStagePlan.Stages.Head
        let draft = { Example.sixStagePlan with Stages = [{template with Id = "z"}; {template with Id = "a"}] }
        Assert.Equal<string list>(["a"; "z"], accepted draft |> Plan.orderedStageIds)

    [<Fact>]
    member _.``empty plan is refused``() =
        rejected EmptyPlan { Example.sixStagePlan with Stages = [] }

    [<Fact>]
    member _.``unknown schema is refused``() =
        rejected (UnsupportedSchema 2) { Example.sixStagePlan with SchemaVersion = 2 }

    [<Fact>]
    member _.``bounded stage count is enforced``() =
        let template = Example.sixStagePlan.Stages.Head
        let stages = [1..257] |> List.map (fun n -> {template with Id = "stage-" + string n})
        rejected (TooManyStages 257) { Example.sixStagePlan with Stages = stages }

    [<Fact>]
    member _.``maximum supported plan is admitted``() =
        let template = Example.sixStagePlan.Stages.Head
        let stages = [1..256] |> List.map (fun n -> {template with Id = "stage-" + string n})
        let plan = accepted { Example.sixStagePlan with Stages = stages }
        Assert.Equal(256, (Plan.orderedStageIds plan).Length)

    [<Fact>]
    member _.``duplicate stable identities are refused``() =
        let draft = Example.sixStagePlan
        rejected (DuplicateStage "build") {draft with Stages = draft.Stages.Head :: draft.Stages}

    [<Fact>]
    member _.``invalid identity is refused``() =
        rejected (InvalidIdentity "name") {Example.sixStagePlan with Name = "../outside"}

    [<Fact>]
    member _.``unidentified producer is refused``() =
        rejected (InvalidDigest "producer") {Example.sixStagePlan with ProducerDigest = "latest"}

    [<Fact>]
    member _.``missing dependency is refused``() =
        Example.sixStagePlan
        |> editStage "install" (fun stage -> {stage with DependsOn = ["absent"]})
        |> rejected (MissingDependency ("install", "absent"))

    [<Fact>]
    member _.``duplicate dependency is refused``() =
        Example.sixStagePlan
        |> editStage "install" (fun stage -> {stage with DependsOn = ["build"; "build"]})
        |> rejected (DuplicateDependency ("install", "build"))

    [<Fact>]
    member _.``cycles are refused``() =
        let draft = Example.sixStagePlan |> editStage "build" (fun stage -> {stage with DependsOn = ["release"]})
        match Plan.validate draft with
        | Error errors -> Assert.True(errors |> List.exists (function CyclicDependencies _ -> true | _ -> false))
        | Ok _ -> failwith "Cycle admitted"

    [<Fact>]
    member _.``imperative effect cannot masquerade as reconciliation``() =
        Example.sixStagePlan
        |> editStage "migrate" (fun stage -> {stage with Repetition = Reconcile})
        |> rejected (UnsafeRepetition "migrate")

    [<Fact>]
    member _.``verification stage must declare observation semantics``() =
        Example.sixStagePlan
        |> editStage "verify" (fun stage -> {stage with Effect = DesiredState})
        |> rejected (VerificationMustObserve "verify")

    [<Fact>]
    member _.``release requires a verification ancestor``() =
        Example.sixStagePlan
        |> editStage "release" (fun stage -> {stage with DependsOn = ["migrate"]})
        |> rejected (ReleaseNeedsVerification "release")

    [<Fact>]
    member _.``verification before later mutations cannot gate release``() =
        Example.sixStagePlan
        |> editStage "verify" (fun stage -> {stage with DependsOn = ["build"]})
        |> editStage "release" (fun stage -> {stage with DependsOn = ["verify"; "migrate"]})
        |> rejected (ReleaseVerificationGap ("release", ["configure"; "install"; "migrate"]))

    [<Fact>]
    member _.``migration may precede installation when dependencies require it``() =
        let plan =
            Example.sixStagePlan
            |> editStage "migrate" (fun stage -> {stage with DependsOn = ["build"]})
            |> editStage "install" (fun stage -> {stage with DependsOn = ["migrate"]})
            |> editStage "verify" (fun stage -> {stage with DependsOn = ["configure"]})
            |> accepted
        Assert.Equal<string list>(["build"; "migrate"; "install"; "configure"; "verify"; "release"], Plan.orderedStageIds plan)

    [<Fact>]
    member _.``duplicate named artifact inputs are refused``() =
        Example.sixStagePlan
        |> editStage "build" (fun stage -> {stage with Inputs = stage.Inputs @ stage.Inputs})
        |> rejected (DuplicateInput ("build", "fixture-input"))

    [<Fact>]
    member _.``preview explicitly establishes preview only``() =
        let plan = accepted Example.sixStagePlan
        use preview = JsonDocument.Parse(Plan.previewJson plan)
        Assert.Equal("Preview", preview.RootElement.GetProperty("evidenceStage").GetString())
        Assert.False(preview.RootElement.TryGetProperty("executionSuccess") |> fst)

type CheckpointTests() =
    let plan = accepted Example.sixStagePlan
    let claim = { PlanDigest = Plan.digest plan; StageId = "migrate"; EvidenceStage = Execution }

    [<Fact>]
    member _.``matching execution claim is compatible but not authenticated``() =
        Assert.Equal(Ok (), Plan.validateCheckpointClaim plan claim)

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    member _.``preview and admission claims cannot stand for execution``(admission: bool) =
        let stage = if admission then Admission else Preview
        Assert.Equal(Error NotExecutionEvidence, Plan.validateCheckpointClaim plan {claim with EvidenceStage = stage})

    [<Fact>]
    member _.``checkpoint for a nonexistent stage is refused``() =
        Assert.Equal(Error StageNotInPlan, Plan.validateCheckpointClaim plan {claim with StageId = "unknown"})

    [<Fact>]
    member _.``changed action at the same label invalidates reuse``() =
        let changed = Example.sixStagePlan |> editStage "migrate" (fun stage -> {stage with ActionDigest = ContentDigest.ofText "new-script"}) |> accepted
        Assert.Equal(Error PlanIdentityChanged, Plan.validateCheckpointClaim changed claim)

    [<Fact>]
    member _.``changed named input invalidates reuse``() =
        let changed = Example.sixStagePlan |> editStage "build" (fun stage -> {stage with Inputs = [{Name = "fixture-input"; Digest = ContentDigest.ofText "changed-input"}]}) |> accepted
        Assert.Equal(Error PlanIdentityChanged, Plan.validateCheckpointClaim changed claim)

    [<Fact>]
    member _.``renamed input is part of identity``() =
        let changed = Example.sixStagePlan |> editStage "build" (fun stage -> {stage with Inputs = [{stage.Inputs.Head with Name = "other-binding"}]}) |> accepted
        Assert.NotEqual<string>(Plan.digest plan, Plan.digest changed)

    [<Fact>]
    member _.``changed dependency edges invalidate reuse``() =
        let changed = Example.sixStagePlan |> editStage "install" (fun stage -> {stage with DependsOn = []}) |> accepted
        Assert.Equal(Error PlanIdentityChanged, Plan.validateCheckpointClaim changed claim)

    [<Fact>]
    member _.``changed producer target config and policy invalidate reuse``() =
        let changedDigest = ContentDigest.ofText "changed"
        let changes = [
            {Example.sixStagePlan with ProducerDigest = changedDigest}
            {Example.sixStagePlan with TargetDigest = changedDigest}
            {Example.sixStagePlan with ConfigurationDigest = changedDigest}
            {Example.sixStagePlan with PolicyDigest = changedDigest}
        ]
        for changed in changes do
            Assert.Equal(Error PlanIdentityChanged, Plan.validateCheckpointClaim (accepted changed) claim)

    [<Fact>]
    member _.``changed adapter implementation invalidates reuse``() =
        let changed = Example.sixStagePlan |> editStage "install" (fun stage -> {stage with AdapterDigest = ContentDigest.ofText "new-adapter"}) |> accepted
        Assert.Equal(Error PlanIdentityChanged, Plan.validateCheckpointClaim changed claim)
