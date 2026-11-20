namespace Hallmark.Runtime

open System
open System.IO
open System.Text.Json
open Hallmark.Lab
open Hallmark.Execution

module Wire =
    let private invalid label message = invalidOp (label + ": " + message)
    let private fields label allowed required (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then invalid label "expected an object"
        let names = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if names.Length <> (List.distinct names).Length then invalid label "duplicate field"
        for name in names do if not (List.contains name allowed) then invalid label ("unknown field " + name)
        for name in required do if not (List.contains name names) then invalid label ("missing field " + name)
    let private text label (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.String then invalid label "expected a string"
        value.GetString()
    let private strings label (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Array then invalid label "expected an array"
        value.EnumerateArray() |> Seq.map (text label) |> Seq.toList
    let private get (key: string) (value: JsonElement) = value.GetProperty key
    let private optional (key: string) fallback (decode: JsonElement -> 'a) (value: JsonElement) =
        match value.TryGetProperty key with true, x -> decode x | _ -> fallback
    let private environment (value: JsonElement) =
        fields "environment" (value.EnumerateObject() |> Seq.map _.Name |> Seq.toList) [] value
        value.EnumerateObject()
        |> Seq.map (fun p ->
            if not (System.Text.RegularExpressions.Regex.IsMatch(p.Name,"\\A[A-Za-z_][A-Za-z0-9_-]*\\z")) then invalid "environment/header" "invalid name"
            let v =
                if p.Value.ValueKind = JsonValueKind.String then Literal (text p.Name p.Value)
                else
                    fields p.Name ["secret";"prefix"] ["secret"] p.Value
                    Secret { Name = get "secret" p.Value |> text "secret"; Prefix = optional "prefix" "" (text "prefix") p.Value }
            p.Name, v)
        |> Map.ofSeq
    let private operation (value: JsonElement) =
        let kind = get "type" value |> text "operation.type"
        match kind with
        | "process" ->
            fields kind ["type";"executable";"arguments";"directory";"environment";"stdinFile";"expectedExit"] ["type";"executable";"arguments"] value
            Process {
                Executable = get "executable" value |> text "executable"
                Arguments = get "arguments" value |> strings "arguments"
                Directory = optional "directory" "." (text "directory") value
                Environment = optional "environment" Map.empty environment value
                StandardInputFile = optional "stdinFile" None (text "stdinFile" >> Some) value
                ExpectedExit = optional "expectedExit" 0 (fun v -> v.GetInt32()) value
            }
        | "write-file" ->
            fields kind ["type";"path";"content"] ["type";"path";"content"] value
            WriteFile (get "path" value |> text "path", get "content" value |> text "content")
        | "capture" ->
            fields kind ["type";"path"] ["type";"path"] value
            Capture (get "path" value |> text "path")
        | "compose-up" ->
            fields kind ["type";"file";"project";"environment"] ["type";"file";"project"] value
            let project = get "project" value |> text "project"
            if not (System.Text.RegularExpressions.Regex.IsMatch(project,"\\Acf-[a-z0-9-]{1,50}\\z")) then invalid kind "project must use an owned cf- prefix"
            ComposeUp (get "file" value |> text "file", project, optional "environment" Map.empty environment value)
        | "release" ->
            fields kind ["type";"version"] ["type";"version"] value
            SealRelease (get "version" value |> text "version")
        | "http" ->
            fields kind ["type";"url";"method";"body";"headers";"status";"contains"] ["type";"url"] value
            HttpCheck {
                Url = get "url" value |> text "url"
                Method = optional "method" "GET" (text "method") value
                Body = optional "body" "" (text "body") value
                Headers = optional "headers" Map.empty environment value
                Status = optional "status" 200 (fun v -> v.GetInt32()) value
                Contains = optional "contains" "" (text "contains") value
            }
        | _ -> invalid "operation" ("unsupported type " + kind)
    let parse (json: string) =
        try
            if System.Text.Encoding.UTF8.GetByteCount json > 1048576 then invalid "manifest" "exceeds 1 MiB"
            use doc = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth=32))
            let root = doc.RootElement
            fields "manifest" ["schemaVersion";"name";"context";"inputs";"allowedExecutables";"steps"] ["schemaVersion";"name";"context";"inputs";"allowedExecutables";"steps"] root
            if (get "schemaVersion" root).GetInt32() <> 1 then invalid "manifest" "unsupported schema version"
            let stepsElement = get "steps" root
            if stepsElement.ValueKind <> JsonValueKind.Array || stepsElement.GetArrayLength() > 256 then invalid "steps" "expected at most 256 steps"
            let steps = [
                for step in stepsElement.EnumerateArray() do
                    fields "step" ["id";"kind";"effect";"repeat";"needs";"operation";"timeoutSeconds";"maxOutputBytes";"outputs";"retrySafety"] ["id";"kind";"effect";"repeat";"operation"] step
                    let read key = get key step |> text key
                    let kind = match read "kind" with "Build" -> Build | "Install" -> Install | "Configure" -> Configure | "Migrate" -> Migrate | "Verify" -> Verify | "Release" -> Release | x -> invalid "kind" x
                    let effect = match read "effect" with "Observation" -> Observation | "DesiredState" -> DesiredState | "Imperative" -> Imperative | x -> invalid "effect" x
                    let repetition = match read "repeat" with "Reconcile" -> Repetition.Reconcile | "ExecuteOnce" -> ExecuteOnce | x -> invalid "repeat" x
                    let safety = match optional "retrySafety" "manual" (text "retrySafety") step with "manual" -> Manual | "reconcile" -> RetrySafety.Reconcile | "external-ledger" -> ExternalLedger | x -> invalid "retrySafety" x
                    if safety = RetrySafety.Reconcile && effect = Imperative then invalid "retrySafety" "imperative action cannot claim reconciliation"
                    let timeout = optional "timeoutSeconds" 120 (fun v -> v.GetInt32()) step
                    let output = optional "maxOutputBytes" 262144 (fun v -> v.GetInt32()) step
                    if timeout < 1 || timeout > 1800 || output < 128 || output > 4194304 then invalid "budget" "timeout/output outside supported bounds"
                    yield {
                        Id = read "id"; Kind = kind; Effect = effect; Repetition = repetition
                        Needs = optional "needs" [] (strings "needs") step
                        Operation = get "operation" step |> operation
                        TimeoutSeconds = timeout; MaxOutputBytes = output
                        Outputs = optional "outputs" [] (strings "outputs") step
                        RetrySafety = safety
                    }
            ]
            let allowed = get "allowedExecutables" root |> strings "allowedExecutables"
            if allowed.Length>64 || (List.distinct allowed).Length<>allowed.Length || allowed |> List.exists (fun name -> String.IsNullOrWhiteSpace name || name |> Seq.exists Char.IsControl) then invalid "allowedExecutables" "expected at most 64 unique executable names"
            for step in steps do
                match step.Operation with
                | Process p when not (List.contains p.Executable allowed) -> invalid step.Id "executable is not allowed by the manifest"
                | ComposeUp _ when not (List.contains "docker" allowed) -> invalid step.Id "docker is not allowed by the manifest"
                | WriteFile _ | ComposeUp _ when step.Effect<>DesiredState -> invalid step.Id "configuration/deployment adapters require DesiredState"
                | Capture _ | HttpCheck _ when step.Effect<>Observation -> invalid step.Id "capture/HTTP adapters require Observation"
                | HttpCheck p when p.Method<>"GET" && p.Method<>"HEAD" -> invalid step.Id "HTTP observation supports GET/HEAD; mutations require an explicit process boundary"
                | SealRelease _ when step.Kind<>Release || step.Effect<>Imperative -> invalid step.Id "release adapter requires an imperative Release stage"
                | _ -> ()
            let canonical = JsonSerializer.Serialize(root)
            Ok {
                Name = get "name" root |> text "name"; Context = get "context" root |> text "context"
                Inputs = get "inputs" root |> strings "inputs"; AllowedExecutables = allowed
                Steps = steps; CanonicalDefinition = canonical
            }
        with
        | :? JsonException as e -> Error ("Invalid JSON: " + e.Message)
        | :? InvalidOperationException as e -> Error e.Message
        | :? FormatException as e -> Error e.Message
        | :? Collections.Generic.KeyNotFoundException as e -> Error e.Message
