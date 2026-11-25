namespace CanonForge.Runtime

open System
open System.IO
open System.Text.Json
open CanonForge.Lab
open CanonForge.Execution

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
        | "postgres-migrate" ->
            fields kind ["type";"containerEngine";"containerId";"database";"user";"migrations";"passwordSecret"] ["type";"containerId";"database";"user";"migrations"] value
            let engine = optional "containerEngine" "podman" (text "containerEngine") value
            if engine <> "podman" && engine <> "docker" then invalid kind "containerEngine must be podman or docker"
            let migrations = get "migrations" value |> strings "migrations"
            if migrations.Length < 2 || migrations.Length > 16 then invalid kind "migrations must list 2 to 16 ordered SQL files"
            let secret =
                match value.TryGetProperty "passwordSecret" with
                | true, s ->
                    if s.ValueKind = JsonValueKind.String then
                        Some { Name = text "passwordSecret" s; Prefix = "" }
                    else
                        fields "passwordSecret" ["secret";"prefix"] ["secret"] s
                        Some { Name = get "secret" s |> text "secret"; Prefix = optional "prefix" "" (text "prefix") s }
                | _ -> None
            PostgresMigrate {
                ContainerEngine = engine
                ContainerId = get "containerId" value |> text "containerId"
                DatabaseName = get "database" value |> text "database"
                User = get "user" value |> text "user"
                Migrations = migrations
                PasswordSecret = secret
            }
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

    let private kindToString = function
        | Build -> "Build" | Install -> "Install" | Configure -> "Configure"
        | Migrate -> "Migrate" | Verify -> "Verify" | Release -> "Release"

    let private effectToString = function
        | Observation -> "Observation" | DesiredState -> "DesiredState" | Imperative -> "Imperative"

    let private repeatToString = function
        | Repetition.Reconcile -> "Reconcile" | ExecuteOnce -> "ExecuteOnce"

    let private retrySafetyToString = function
        | Manual -> "manual" | RetrySafety.Reconcile -> "reconcile" | ExternalLedger -> "external-ledger"

    let private writeEnvMap (writer: Utf8JsonWriter) (propName: string) (env: Map<string, EnvironmentValue>) =
        if not env.IsEmpty then
            writer.WriteStartObject(propName)
            for KeyValue(k, v) in env do
                match v with
                | Literal text -> writer.WriteString(k, text)
                | Secret s ->
                    writer.WriteStartObject(k)
                    writer.WriteString("secret", s.Name)
                    if not (String.IsNullOrEmpty s.Prefix) then
                        writer.WriteString("prefix", s.Prefix)
                    writer.WriteEndObject()
            writer.WriteEndObject()

    let toJson (workflow: Workflow) : string =
        use buffer = new MemoryStream()
        use writer = new Utf8JsonWriter(buffer)
        writer.WriteStartObject()
        writer.WriteNumber("schemaVersion", 1)
        writer.WriteString("name", workflow.Name)
        writer.WriteString("context", workflow.Context)
        writer.WriteStartArray("inputs")
        for input in workflow.Inputs do writer.WriteStringValue(input)
        writer.WriteEndArray()
        writer.WriteStartArray("allowedExecutables")
        for exe in workflow.AllowedExecutables do writer.WriteStringValue(exe)
        writer.WriteEndArray()
        writer.WriteStartArray("steps")
        for step in workflow.Steps do
            writer.WriteStartObject()
            writer.WriteString("id", step.Id)
            writer.WriteString("kind", kindToString step.Kind)
            writer.WriteString("effect", effectToString step.Effect)
            writer.WriteString("repeat", repeatToString step.Repetition)
            writer.WriteStartArray("needs")
            for need in step.Needs do writer.WriteStringValue(need)
            writer.WriteEndArray()
            writer.WriteStartObject("operation")
            match step.Operation with
            | Process p ->
                writer.WriteString("type", "process")
                writer.WriteString("executable", p.Executable)
                writer.WriteStartArray("arguments")
                for arg in p.Arguments do writer.WriteStringValue(arg)
                writer.WriteEndArray()
                if p.Directory <> "." then writer.WriteString("directory", p.Directory)
                writeEnvMap writer "environment" p.Environment
                match p.StandardInputFile with
                | Some f -> writer.WriteString("stdinFile", f)
                | None -> ()
                if p.ExpectedExit <> 0 then writer.WriteNumber("expectedExit", p.ExpectedExit)
            | WriteFile (path, content) ->
                writer.WriteString("type", "write-file")
                writer.WriteString("path", path)
                writer.WriteString("content", content)
            | Capture path ->
                writer.WriteString("type", "capture")
                writer.WriteString("path", path)
            | ComposeUp (file, project, env) ->
                writer.WriteString("type", "compose-up")
                writer.WriteString("file", file)
                writer.WriteString("project", project)
                writeEnvMap writer "environment" env
            | PostgresMigrate p ->
                writer.WriteString("type", "postgres-migrate")
                writer.WriteString("containerEngine", p.ContainerEngine)
                writer.WriteString("containerId", p.ContainerId)
                writer.WriteString("database", p.DatabaseName)
                writer.WriteString("user", p.User)
                writer.WriteStartArray("migrations")
                for m in p.Migrations do writer.WriteStringValue(m)
                writer.WriteEndArray()
                match p.PasswordSecret with
                | Some s ->
                    writer.WriteStartObject("passwordSecret")
                    writer.WriteString("secret", s.Name)
                    if not (String.IsNullOrEmpty s.Prefix) then
                        writer.WriteString("prefix", s.Prefix)
                    writer.WriteEndObject()
                | None -> ()
            | HttpCheck h ->
                writer.WriteString("type", "http")
                writer.WriteString("url", h.Url)
                if h.Method <> "GET" then writer.WriteString("method", h.Method)
                if not (String.IsNullOrEmpty h.Body) then writer.WriteString("body", h.Body)
                writeEnvMap writer "headers" h.Headers
                if h.Status <> 200 then writer.WriteNumber("status", h.Status)
                if not (String.IsNullOrEmpty h.Contains) then writer.WriteString("contains", h.Contains)
            | SealRelease version ->
                writer.WriteString("type", "release")
                writer.WriteString("version", version)
            writer.WriteEndObject()
            if step.TimeoutSeconds <> 120 then writer.WriteNumber("timeoutSeconds", step.TimeoutSeconds)
            if step.MaxOutputBytes <> 262144 then writer.WriteNumber("maxOutputBytes", step.MaxOutputBytes)
            writer.WriteStartArray("outputs")
            for out in step.Outputs do writer.WriteStringValue(out)
            writer.WriteEndArray()
            writer.WriteString("retrySafety", retrySafetyToString step.RetrySafety)
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(buffer.ToArray())

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
                | PostgresMigrate _ when step.Kind<>Migrate || step.Effect<>Imperative || step.RetrySafety<>ExternalLedger -> invalid step.Id "postgres-migrate adapter requires an imperative Migrate stage with external-ledger retrySafety"
                | WriteFile _ | ComposeUp _ when step.Effect<>DesiredState -> invalid step.Id "configuration/deployment adapters require DesiredState"
                | Capture _ | HttpCheck _ when step.Effect<>Observation -> invalid step.Id "capture/HTTP adapters require Observation"
                | HttpCheck p when p.Method<>"GET" && p.Method<>"HEAD" -> invalid step.Id "HTTP observation supports GET/HEAD; mutations require an explicit process boundary"
                | SealRelease _ when step.Kind<>Release || step.Effect<>Imperative -> invalid step.Id "release adapter requires an imperative Release stage"
                | _ -> ()
            let draftWorkflow = {
                Name = get "name" root |> text "name"; Context = get "context" root |> text "context"
                Inputs = get "inputs" root |> strings "inputs"; AllowedExecutables = allowed
                Steps = steps; CanonicalDefinition = ""
            }
            let canonical = toJson draftWorkflow
            Ok { draftWorkflow with CanonicalDefinition = canonical }
        with
        | :? JsonException as e -> Error ("Invalid JSON: " + e.Message)
        | :? InvalidOperationException as e -> Error e.Message
        | :? FormatException as e -> Error e.Message
        | :? Collections.Generic.KeyNotFoundException as e -> Error e.Message

type WorkflowBuilder() =
    member _.Yield(()) : Workflow =
        {
            Name = "hallmark-workflow"
            Context = "hallmark-context"
            Inputs = []
            AllowedExecutables = ["/bin/sh"]
            Steps = []
            CanonicalDefinition = ""
        }

    [<CustomOperation("name")>]
    member _.Name(w: Workflow, value: string) = { w with Name = value }

    [<CustomOperation("context")>]
    member _.Context(w: Workflow, value: string) = { w with Context = value }

    [<CustomOperation("inputs")>]
    member _.Inputs(w: Workflow, values: string list) = { w with Inputs = values }

    [<CustomOperation("allowedExecutables")>]
    member _.AllowedExecutables(w: Workflow, values: string list) = { w with AllowedExecutables = values }

    [<CustomOperation("step")>]
    member _.Step(w: Workflow, step: Step) = { w with Steps = w.Steps @ [step] }

    [<CustomOperation("buildProcess")>]
    member _.BuildProcess(w: Workflow, id: string, executable: string, arguments: string list, needs: string list, outputs: string list) =
        let s = {
            Id = id; Kind = Build; Effect = Imperative; Repetition = ExecuteOnce; Needs = needs
            Operation = Process { Executable = executable; Arguments = arguments; Directory = "."; Environment = Map.empty; StandardInputFile = None; ExpectedExit = 0 }
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = outputs; RetrySafety = Manual
        }
        { w with Steps = w.Steps @ [s] }

    [<CustomOperation("captureArtifact")>]
    member _.CaptureArtifact(w: Workflow, id: string, path: string, needs: string list) =
        let s = {
            Id = id; Kind = Build; Effect = Observation; Repetition = Repetition.Reconcile; Needs = needs
            Operation = Capture path
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = []; RetrySafety = RetrySafety.Reconcile
        }
        { w with Steps = w.Steps @ [s] }

    [<CustomOperation("writeFile")>]
    member _.WriteFile(w: Workflow, id: string, kind: StageKind, path: string, content: string, needs: string list) =
        let s = {
            Id = id; Kind = kind; Effect = DesiredState; Repetition = Repetition.Reconcile; Needs = needs
            Operation = WriteFile (path, content)
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = []; RetrySafety = RetrySafety.Reconcile
        }
        { w with Steps = w.Steps @ [s] }

    [<CustomOperation("postgresMigrate")>]
    member _.PostgresMigrate(w: Workflow, id: string, engine: string, containerId: string, database: string, user: string, migrations: string list, needs: string list, outputs: string list) =
        let s = {
            Id = id; Kind = Migrate; Effect = Imperative; Repetition = ExecuteOnce; Needs = needs
            Operation = PostgresMigrate { ContainerEngine = engine; ContainerId = containerId; DatabaseName = database; User = user; Migrations = migrations; PasswordSecret = None }
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = outputs; RetrySafety = ExternalLedger
        }
        { w with Steps = w.Steps @ [s] }

    [<CustomOperation("verifyProcess")>]
    member _.VerifyProcess(w: Workflow, id: string, executable: string, arguments: string list, needs: string list) =
        let s = {
            Id = id; Kind = Verify; Effect = Observation; Repetition = Repetition.Reconcile; Needs = needs
            Operation = Process { Executable = executable; Arguments = arguments; Directory = "."; Environment = Map.empty; StandardInputFile = None; ExpectedExit = 0 }
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = []; RetrySafety = RetrySafety.Reconcile
        }
        { w with Steps = w.Steps @ [s] }

    [<CustomOperation("sealRelease")>]
    member _.SealRelease(w: Workflow, id: string, version: string, needs: string list) =
        let s = {
            Id = id; Kind = Release; Effect = Imperative; Repetition = ExecuteOnce; Needs = needs
            Operation = SealRelease version
            TimeoutSeconds = 120; MaxOutputBytes = 262144; Outputs = []; RetrySafety = Manual
        }
        { w with Steps = w.Steps @ [s] }

    member _.Run(w: Workflow) : Result<Workflow, string> =
        let json = Wire.toJson w
        Wire.parse json

[<AutoOpen>]
module WorkflowDsl =
    let hallmarkWorkflow = WorkflowBuilder()
