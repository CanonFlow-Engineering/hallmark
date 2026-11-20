namespace Hallmark.Runtime

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Hallmark.Lab
open Hallmark.Lab.Serialization
open Hallmark.Execution

type Prepared = { Root: string; State: string; Directory: string; Workflow: Workflow; Plan: ValidatedPlan; Digest: string; SourceDigest: string; Sources: FileIdentity array; SecretVersions: Property array; Executables: Map<string,string>; ExecutionIdentity: string }
type Drift = { Kind: string; Resource: string; Reason: string }
type DriftInspection = { Changed: Drift array; Unavailable: string array; Scope: string array }

exception AdapterFailure of status: string * exitCode: int * detail: string

module Runner =
    let private executionIdentity executables =
        let tools = executables |> Map.toArray |> Array.map (fun (key,path) -> {Key=key;Value=path+":"+Storage.hashFile path})
        let environment = Processes.inheritedEnvironment() |> List.map (fun (key,value) -> {Key=key;Value=ContentDigest.ofText value}) |> List.toArray
        ContentDigest.ofText(Storage.encode {|tools=tools;environment=environment|})
    let private secretRefs operation =
        let values = match operation with Process p -> p.Environment | HttpCheck p -> p.Headers | ComposeUp(_,_,env) -> env | _ -> Map.empty
        values |> Map.toList |> List.choose (fun (_,v) -> match v with Secret r -> Some r.Name | Literal _ -> None)
    let private strings operation =
        let literals values = values |> Map.toList |> List.choose (fun (_,v) -> match v with Literal text -> Some text | Secret _ -> None)
        match operation with
        | Process p -> p.Executable :: p.Directory :: (p.Arguments @ literals p.Environment @ Option.toList p.StandardInputFile)
        | WriteFile (path,text) -> [path;text]
        | Capture path -> [path]
        | ComposeUp (file,project,env) -> [file;project] @ literals env
        | HttpCheck p -> [p.Url;p.Body;p.Contains] @ literals p.Headers
        | SealRelease version -> [version]
    let private outputKeys = function
        | Process _ -> set ["stdout";"stderr";"exitCode"]
        | WriteFile _ | Capture _ -> set ["path";"digest"]
        | ComposeUp _ -> set ["project";"file"]
        | HttpCheck _ -> set ["status";"body"]
        | SealRelease _ -> set ["path";"digest";"version"]
    let private refs = Regex("\\{\\{([a-z][a-z0-9-]*)\\.([a-zA-Z0-9_-]+)\\}\\}",RegexOptions.CultureInvariant)
    let prepare manifest state =
        let root = Path.GetDirectoryName(Path.GetFullPath manifest)
        let workflow = match Wire.parse(Storage.readBounded manifest) with Ok w -> w | Error e -> invalidOp e
        let directory = Storage.context state workflow.Context
        use contextLock = Storage.lockContext directory
        let identity = Storage.contextIdentity directory
        let sources = Storage.sourceFiles root workflow.Inputs
        let sourceDigest = ContentDigest.ofText(Storage.encode sources)
        let secrets = workflow.Steps |> List.collect (fun step -> secretRefs step.Operation) |> List.distinct |> List.sort
        let secretVersions = secrets |> List.map (fun key -> {Key=key;Value=(Storage.secretEntry directory key).Version}) |> List.toArray
        let executables = workflow.AllowedExecutables |> List.map (fun key -> key,Processes.executable key) |> Map.ofList
        let adapterIdentity = executionIdentity executables
        let producer =
            [yield typeof<Prepared>.Assembly.Location
             yield typeof<StageDraft>.Assembly.Location
             match System.Reflection.Assembly.GetEntryAssembly() with null -> () | entry -> yield entry.Location]
            |> List.filter File.Exists
            |> List.distinct
            |> List.map Storage.hashFile
            |> fun hashes -> ContentDigest.ofText(Storage.encode (Environment.Version.ToString() :: hashes))
        let draft: PlanDraft = {
            SchemaVersion=1;Name=workflow.Name;ProducerDigest=producer
            TargetDigest=ContentDigest.ofText(identity + workflow.Context)
            ConfigurationDigest=ContentDigest.ofText(workflow.CanonicalDefinition + Storage.encode secretVersions)
            PolicyDigest=ContentDigest.ofText(Storage.encode workflow.AllowedExecutables)
            Stages=workflow.Steps |> List.map (fun step -> {
                Id=step.Id;Kind=step.Kind;Effect=step.Effect;Repetition=step.Repetition;DependsOn=step.Needs
                AdapterDigest=adapterIdentity
                ActionDigest=ContentDigest.ofText(workflow.CanonicalDefinition + step.Id)
                Inputs=[{Name="source-closure";Digest=sourceDigest}]
            })
        }
        let plan = match Planning.validate draft with Ok p -> p | Error e -> invalidOp (sprintf "Plan rejected: %A" e)
        let byId = workflow.Steps |> List.map (fun step -> step.Id,step) |> Map.ofList
        let rec ancestors seen id =
            if Set.contains id seen then seen
            else byId[id].Needs |> List.fold ancestors (Set.add id seen)
        for step in workflow.Steps do
            let allowed = step.Needs |> List.fold ancestors Set.empty
            for value in strings step.Operation @ step.Outputs do
                for m in refs.Matches(value) |> Seq.cast<Match> do
                    let dependency,key = m.Groups[1].Value,m.Groups[2].Value
                    if not (Set.contains dependency allowed) || not ((outputKeys byId[dependency].Operation).Contains key) then invalidOp (step.Id + " references an undeclared dependency/output")
        {Root=root;State=state;Directory=directory;Workflow=workflow;Plan=plan;Digest=Plan.digest plan;SourceDigest=sourceDigest;Sources=sources;SecretVersions=secretVersions;Executables=executables;ExecutionIdentity=adapterIdentity}
    let private environment directory resolve values =
        values |> Map.toList |> List.map (fun (key,value) ->
            key,match value with Literal text -> resolve text | Secret r -> r.Prefix + Storage.secretValue directory r.Name)
    let private substitute (completed: StepRecord array) text =
        refs.Replace(text,MatchEvaluator(fun m ->
            completed |> Array.tryFind (fun step -> step.Id=m.Groups[1].Value && step.Status="succeeded")
            |> Option.bind (fun step -> step.Values |> Array.tryFind (fun p -> p.Key=m.Groups[2].Value))
            |> Option.map _.Value
            |> Option.defaultWith (fun () -> invalidOp "Required runtime output is unavailable")))
    let private filesCurrent (files: FileIdentity array) = files |> Array.forall (fun f -> File.Exists f.Path && Storage.hashFile f.Path=f.Digest)
    let drift (record: RunRecord) = record.Steps |> Array.collect _.Files |> Array.filter (fun f -> not (File.Exists f.Path) || Storage.hashFile f.Path<>f.Digest)
    let inspectDrift (record: RunRecord) (cancel: CancellationToken) = task {
        let changed = ResizeArray<Drift>(drift record |> Array.map (fun f -> {Kind="file";Resource=f.Path;Reason="Missing or changed bytes"}))
        let unavailable = ResizeArray<string>()
        for step in record.Steps do
            let value key = step.Values |> Array.tryFind (fun p -> p.Key=key) |> Option.map _.Value
            match value "project",value "file",value "services" with
            | Some project,Some file,Some expected ->
                try
                    let! result = Processes.run (Processes.executable "docker") ["compose";"-p";project;"-f";file;"ps";"--all";"--format";"json"] (Path.GetDirectoryName file) [] None 30 262144 [] cancel
                    if result.Status<>"completed" || result.ExitCode<>0 then unavailable.Add("Compose state unavailable for " + project)
                    else
                        let lines = result.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries)
                        let rows =
                            if result.Stdout.TrimStart().StartsWith("[",StringComparison.Ordinal) then
                                use doc = System.Text.Json.JsonDocument.Parse(result.Stdout)
                                doc.RootElement.EnumerateArray() |> Seq.map _.Clone() |> Seq.toArray
                            else lines |> Array.map (fun line -> use doc = System.Text.Json.JsonDocument.Parse(line) in doc.RootElement.Clone())
                        for service in expected.Split(',',StringSplitOptions.RemoveEmptyEntries) do
                            let members = rows |> Array.filter (fun row -> row.GetProperty("Service").GetString()=service)
                            if members.Length=0 || members |> Array.exists (fun row -> row.GetProperty("State").GetString()<>"running" || (let h=row.GetProperty("Health").GetString() in h<>"" && h<>"healthy")) then
                                changed.Add {Kind="compose-service";Resource=project+"/"+service;Reason="Missing, stopped or unhealthy"}
                with
                | :? OperationCanceledException -> raise (OperationCanceledException(cancel))
                | e -> unavailable.Add("Compose inspection unavailable for " + project + ": " + e.GetType().Name)
            | Some project,_,_ -> unavailable.Add("No recorded service inventory for " + project)
            | _ -> ()
        return {Changed=changed.ToArray() |> Array.distinct;Unavailable=unavailable.ToArray();Scope=[|"recorded-files";"compose-service-state-and-health"|]}
    }
    let requireOwnership state context workflow resource =
        let path = Path.Combine(state,"ownership.json")
        let rows = if File.Exists path then System.Text.Json.JsonSerializer.Deserialize<Property array>(Storage.readBounded path) else [||]
        if not (rows |> Array.exists (fun p -> p.Key=resource && p.Value=context+"/"+workflow)) then invalidOp "Resource ownership does not match this run"
    let private claim (prepared: Prepared) resource =
        use ownershipLock = new FileStream(Path.Combine(prepared.State,"ownership.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)
        let path = Path.Combine(prepared.State,"ownership.json")
        let current = if File.Exists path then System.Text.Json.JsonSerializer.Deserialize<Property array>(Storage.readBounded path) else [||]
        let owner = prepared.Workflow.Context + "/" + prepared.Workflow.Name
        match current |> Array.tryFind (fun p -> p.Key=resource) with
        | Some row when row.Value<>owner -> invalidOp ("Resource is already owned by " + row.Value)
        | Some _ -> ()
        | None -> Storage.atomic path (Storage.encode(Array.append current [|{Key=resource;Value=owner}|]))
    let private execute (prepared: Prepared) run (completed: StepRecord array) (step: Step) (cancel: CancellationToken) = task {
        let resolve = substitute completed
        let secrets = prepared.SecretVersions |> Array.map (fun r -> Storage.secretValue prepared.Directory r.Key) |> Array.toList
        let redact = Processes.redact secrets
        let command executable args directory env stdin =
            if executionIdentity prepared.Executables<>prepared.ExecutionIdentity then invalidOp "Executor environment or executable bytes changed after planning"
            Processes.run prepared.Executables[executable] args directory env stdin step.TimeoutSeconds step.MaxOutputBytes secrets cancel
        let mutable values: Property array = [||]
        let mutable automaticFiles: FileIdentity array = [||]
        let mutable message = ""
        let mutable exitCode = 0
        match step.Operation with
        | Process p ->
            let input = p.StandardInputFile |> Option.map (resolve >> Storage.child prepared.Root >> Storage.readBounded)
            let! result = command p.Executable (List.map resolve p.Arguments) (Storage.child prepared.Root (resolve p.Directory)) (environment prepared.Directory resolve p.Environment) input
            message <- result.Status
            exitCode <- result.ExitCode
            Storage.atomic (Path.Combine(prepared.Directory,"logs",run,step.Id+".stdout.log")) result.Stdout
            Storage.atomic (Path.Combine(prepared.Directory,"logs",run,step.Id+".stderr.log")) result.Stderr
            if result.Status<>"completed" || result.ExitCode<>p.ExpectedExit then
                raise (AdapterFailure((if result.Status="completed" then "failed" else result.Status),result.ExitCode,"Process " + step.Id + ": " + result.Status))
            values <- [|{Key="stdout";Value=result.Stdout.Trim()};{Key="stderr";Value=result.Stderr.Trim()};{Key="exitCode";Value=string result.ExitCode}|]
        | WriteFile (path,content) ->
            let path = Storage.child prepared.Root (resolve path)
            claim prepared ("file:"+path)
            Storage.atomic path (resolve content)
            let digest = Storage.hashFile path
            values <- [|{Key="path";Value=path};{Key="digest";Value=digest}|]
            automaticFiles <- [|{Path=path;Digest=digest}|]
        | Capture path ->
            let source = Storage.child prepared.Root (resolve path)
            let path,digest = Storage.storeArtifact prepared.Directory source
            values <- [|{Key="path";Value=path};{Key="digest";Value=digest}|]
            automaticFiles <- [|{Path=path;Digest=digest}|]
        | ComposeUp (file,project,env) ->
            claim prepared ("compose:"+project)
            let path = Storage.child prepared.Root (resolve file)
            let! result = command "docker" ["compose";"-p";project;"-f";path;"up";"-d";"--wait";"--wait-timeout";string step.TimeoutSeconds] prepared.Root (environment prepared.Directory resolve env) None
            if result.Status<>"completed" || result.ExitCode<>0 then
                raise (AdapterFailure((if result.Status="completed" then "failed" else result.Status),result.ExitCode,"Compose reconciliation: " + redact result.Stderr))
            let! inventory = command "docker" ["compose";"-p";project;"-f";path;"config";"--services"] prepared.Root (environment prepared.Directory resolve env) None
            if inventory.Status<>"completed" || inventory.ExitCode<>0 then invalidOp "Compose service inventory unavailable after reconciliation"
            values <- [|{Key="project";Value=project};{Key="file";Value=path};{Key="services";Value=String.Join(",",inventory.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries) |> Array.map _.Trim())}|]
            automaticFiles <- [|{Path=path;Digest=Storage.hashFile path}|]
        | HttpCheck p ->
            use deadline = CancellationTokenSource.CreateLinkedTokenSource cancel
            deadline.CancelAfter(TimeSpan.FromSeconds(float step.TimeoutSeconds))
            use client = new HttpClient(Timeout=TimeSpan.FromSeconds(float step.TimeoutSeconds))
            use request = new HttpRequestMessage(HttpMethod(p.Method),resolve p.Url)
            for key,value in environment prepared.Directory resolve p.Headers do request.Headers.TryAddWithoutValidation(key,value) |> ignore
            if p.Body<>"" then request.Content <- new StringContent(resolve p.Body,Encoding.UTF8,"application/json")
            use! response = client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token)
            use! stream = response.Content.ReadAsStreamAsync(deadline.Token)
            use memory = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 4096
            let mutable reading = true
            while reading do
                let! count = stream.ReadAsync(buffer.AsMemory(),deadline.Token)
                if count=0 then reading<-false
                elif memory.Length+int64 count > int64 step.MaxOutputBytes then invalidOp "HTTP output budget exceeded"
                else memory.Write(buffer,0,count)
            let body = Encoding.UTF8.GetString(memory.ToArray())
            if int response.StatusCode<>p.Status || not (body.Contains(resolve p.Contains,StringComparison.Ordinal)) then invalidOp ("HTTP assertion failed for " + step.Id + "; status " + string(int response.StatusCode))
            values <- [|{Key="status";Value=string(int response.StatusCode)};{Key="body";Value=redact body}|]
        | SealRelease version ->
            if Storage.sourceFiles prepared.Root prepared.Workflow.Inputs |> Storage.encode |> ContentDigest.ofText <> prepared.SourceDigest then invalidOp "Source inputs changed during execution; release refused"
            if not (completed |> Array.forall (fun s -> filesCurrent s.Files)) then invalidOp "Recorded outputs changed during execution; release refused"
            let artifacts =
                completed |> Array.collect _.Files |> Array.distinct
                |> Array.map (fun file -> let path,digest = Storage.storeArtifact prepared.Directory file.Path in {Path=path;Digest=digest})
                |> Array.distinct
            let release,path = Storage.seal prepared.Directory prepared.Workflow.Name (resolve version) prepared.Digest run prepared.Workflow.Context artifacts (ContentDigest.ofText(Storage.encode completed))
            values <- [|{Key="path";Value=path};{Key="digest";Value=Storage.hashFile path};{Key="version";Value=release.Version}|]
            automaticFiles <- [|{Path=path;Digest=Storage.hashFile path}|]
        let declared = step.Outputs |> List.map (resolve >> Storage.child prepared.Root) |> List.map (fun path -> {Path=path;Digest=Storage.hashFile path}) |> List.toArray
        return values,Array.append automaticFiles declared |> Array.distinct,message,exitCode
    }
    let private runPlan prepared resume converging (cancel: CancellationToken) = task {
        use executionLock = Storage.lockContext prepared.Directory
        if Storage.sourceFiles prepared.Root prepared.Workflow.Inputs |> Storage.encode |> ContentDigest.ofText <> prepared.SourceDigest then invalidOp "Inputs changed after planning"
        for secret in prepared.SecretVersions do
            if (Storage.secretEntry prepared.Directory secret.Key).Version<>secret.Value then invalidOp "A secret rotated after planning"
        let previous = resume |> Option.map (Storage.loadRun prepared.Directory)
        match previous with
        | Some prior when prior.PlanDigest<>prepared.Digest || prior.SourceDigest<>prepared.SourceDigest -> invalidOp "Resume refused: the plan/input identity changed"
        | Some prior when converging && prior.Status<>"succeeded" -> invalidOp "Convergence requires a completed run; resume failures explicitly"
        | _ -> ()
        // Validate every recorded decision before changing the durable run status.
        for step in prepared.Workflow.Steps do
            match previous |> Option.bind (fun r -> r.Steps |> Array.tryFind (fun p -> p.Id=step.Id)) with
            | Some p ->
                let outcome = if p.Status="succeeded" then Completed elif p.Status="running" then Interrupted else Failed
                let decision = if converging && (step.Effect=DesiredState || step.Kind=Verify) then Hallmark.Execution.Execute else Recovery.decide true (filesCurrent p.Files) step.RetrySafety outcome
                match decision with
                | Refuse reason -> invalidOp (step.Id + ": " + reason)
                | Reuse | Hallmark.Execution.Execute -> ()
            | None -> ()
        let runId = if converging then Guid.NewGuid().ToString("N") else previous |> Option.map _.RunId |> Option.defaultWith (fun () -> Guid.NewGuid().ToString("N"))
        let mutable record = previous |> Option.defaultValue {SchemaVersion=1;RunId=runId;Name=prepared.Workflow.Name;Context=prepared.Workflow.Context;PlanDigest=prepared.Digest;SourceDigest=prepared.SourceDigest;Status="running";Started=Storage.now();Ended="";Steps=[||];Checksum=""}
        if converging then record <- {record with RunId=runId;Started=Storage.now();Ended=""}
        record <- Storage.saveRun prepared.Directory {record with Status="running";Ended=""}
        Storage.append prepared.Directory runId "" (if converging then "converging-from:"+previous.Value.RunId else "running")
        let mutable completed: StepRecord array = [||]
        let mutable failed = false
        let byId = prepared.Workflow.Steps |> List.map (fun s -> s.Id,s) |> Map.ofList
        for id in Planning.orderedStageIds prepared.Plan do
            if not failed then
                let step = byId[id]
                let prior = previous |> Option.bind (fun r -> r.Steps |> Array.tryFind (fun s -> s.Id=id))
                let reuse =
                    match prior with
                    | None -> Hallmark.Execution.Execute
                    | Some _ when converging && (step.Effect=DesiredState || step.Kind=Verify) -> Hallmark.Execution.Execute
                    | Some p ->
                        let outcome = if p.Status="succeeded" then Completed elif p.Status="running" then Interrupted else Failed
                        Recovery.decide true (filesCurrent p.Files) step.RetrySafety outcome
                match reuse with
                | Refuse reason -> invalidOp (id + ": " + reason)
                | Reuse -> completed <- Array.append completed [|prior.Value|]
                | Hallmark.Execution.Execute ->
                    let started = {Id=id;Status="running";ExitCode=0;Started=Storage.now();Ended="";Message="";Values=[||];Files=[||]}
                    record <- Storage.saveRun prepared.Directory {record with Steps=Array.append completed [|started|]}
                    Storage.append prepared.Directory runId id "running"
                    try
                        cancel.ThrowIfCancellationRequested()
                        let! values,files,message,exitCode = execute prepared runId completed step cancel
                        let finished = {started with Status="succeeded";Ended=Storage.now();Values=values;Files=files;Message=message;ExitCode=exitCode}
                        completed <- Array.append completed [|finished|]
                        record <- Storage.saveRun prepared.Directory {record with Steps=completed}
                        Storage.append prepared.Directory runId id "succeeded"
                    with e ->
                        failed <- true
                        let status,exitCode,detail =
                            match e with
                            | AdapterFailure(status,code,detail) -> status,code,detail
                            | :? OperationCanceledException when cancel.IsCancellationRequested -> "cancelled",130,"Cancelled; external outcome may require reconciliation"
                            | :? OperationCanceledException -> "timeout",124,"Deadline expired; external outcome may require reconciliation"
                            | _ -> "failed",1,e.Message
                        let message = prepared.SecretVersions |> Array.map (fun p -> Storage.secretValue prepared.Directory p.Key) |> Array.toList |> fun secrets -> Processes.redact secrets detail
                        let finished = {started with Status=status;Ended=Storage.now();Message=message;ExitCode=exitCode}
                        completed <- Array.append completed [|finished|]
                        record <- Storage.saveRun prepared.Directory {record with Steps=completed;Status=status}
                        Storage.append prepared.Directory runId id status
        let status = if failed then record.Status else "succeeded"
        record <- Storage.saveRun prepared.Directory {record with Steps=completed;Status=status;Ended=Storage.now()}
        Storage.append prepared.Directory runId "" status
        return record
    }
    let apply prepared resume cancel = runPlan prepared resume false cancel
    let converge prepared previous cancel = runPlan prepared (Some previous) true cancel
