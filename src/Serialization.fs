namespace Hallmark.Lab.Serialization

open System
open System.IO
open System.Text
open System.Text.Json
open System.Security.Cryptography
open Hallmark.Lab

module ContentDigest =
    let ofText (text: string) =
        text
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun hash -> "sha256:" + hash.ToLowerInvariant()

module Plan =
    let private kindName = function
        | Build -> "Build" | Install -> "Install" | Configure -> "Configure"
        | Migrate -> "Migrate" | Verify -> "Verify" | Release -> "Release"

    let private effectName = function
        | Observation -> "Observation" | DesiredState -> "DesiredState" | Imperative -> "Imperative"

    let private repetitionName = function
        | Reconcile -> "Reconcile" | ExecuteOnce -> "ExecuteOnce"

    let private canonical (draft: PlanDraft) =
        use buffer = new MemoryStream()
        use writer = new Utf8JsonWriter(buffer)
        let text (key: string) (value: string) = writer.WriteString(key, value)
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", draft.SchemaVersion)
        text "name" draft.Name
        text "producerDigest" draft.ProducerDigest
        text "targetDigest" draft.TargetDigest
        text "configurationDigest" draft.ConfigurationDigest
        text "policyDigest" draft.PolicyDigest
        writer.WriteStartArray("stages")
        for stage in draft.Stages |> List.sortBy _.Id do
            writer.WriteStartObject()
            text "id" stage.Id
            text "kind" (kindName stage.Kind)
            text "effect" (effectName stage.Effect)
            text "repetition" (repetitionName stage.Repetition)
            text "adapterDigest" stage.AdapterDigest
            text "actionDigest" stage.ActionDigest
            writer.WriteStartArray("inputs")
            for input in stage.Inputs |> List.sortBy _.Name do
                writer.WriteStartObject()
                text "name" input.Name
                text "digest" input.Digest
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray("dependsOn")
            for dependency in List.sort stage.DependsOn do writer.WriteStringValue(dependency)
            writer.WriteEndArray()
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(buffer.ToArray())

    let validate draft = Planning.validate draft
    let orderedStageIds plan = Planning.orderedStageIds plan
    let canonicalJson plan = canonical (Planning.definition plan)
    let digest plan = ContentDigest.ofText (canonicalJson plan)

    let previewJson plan =
        JsonSerializer.Serialize({|
            schemaVersion = 1
            evidenceStage = "Preview"
            planDigest = digest plan
            stages = orderedStageIds plan |> List.toArray
        |})

    /// This checks compatibility, not authenticity, output integrity or permission.
    let validateCheckpointClaim plan claim =
        Checkpoint.validate plan (digest plan) claim
