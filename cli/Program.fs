module CanonForge.Cli.Program

open System
open System.IO
open System.Text.Json
open System.Threading
open CanonForge.Runtime
open CanonForge.Lab.Serialization

let emit value = printfn "%s" (Storage.encode value)
let help = """CanonForge
Typed local build and delivery with explicit evidence.

canonforge capabilities
canonforge schema
canonforge plan MANIFEST [--state-dir DIR]
canonforge apply MANIFEST [--state-dir DIR]
canonforge resume MANIFEST --run ID [--state-dir DIR]
canonforge converge MANIFEST --run ID [--state-dir DIR]
canonforge freeze MANIFEST FILE [--state-dir DIR]
canonforge status --context NAME [--state-dir DIR]
canonforge history --context NAME [--state-dir DIR]
canonforge drift --context NAME --run ID [--state-dir DIR]
canonforge secret-set NAME --context NAME [--state-dir DIR] < material
canonforge secret-list --context NAME [--state-dir DIR]
canonforge public-key --context NAME [--state-dir DIR]
canonforge release-verify FILE --public-key FILE
canonforge bundle FILE --context NAME --run ID --public-key FILE [--state-dir DIR]
canonforge bundle-verify FILE --public-key FILE
canonforge promote VERSION --channel NAME --context NAME --public-key FILE [--state-dir DIR]
canonforge channels --context NAME [--state-dir DIR]
canonforge destroy --context NAME --run ID [--state-dir DIR]
canonforge export FILE --context NAME --run ID [--state-dir DIR]
canonforge doctor

Plans and state directories are trusted local execution inputs.
Secrets enter through stdin and are referenced by name in the manifest.
Use --plan-digest SHA256 on apply/resume/converge to bind a reviewed plan.
Converge reuses build/migration/release evidence and reruns desired state and verification.
Exit: 0 success, 1 failed run, 2 observed drift, 3 unavailable/invalid runtime evidence, 64 invalid invocation.
"""

let options (arguments: string array) =
    let allowed = set ["--state-dir";"--context";"--run";"--public-key";"--plan-digest";"--channel";"--fixture"]
    let flags = set ["--allow-effects"]
    let rec parse remaining positional values =
        match remaining with
        | [] -> List.rev positional,values
        | "--"::rest -> List.rev positional @ rest,values
        | flag::rest when flags.Contains flag ->
            if Map.containsKey flag values then invalidArg "arguments" ("Duplicate option " + flag)
            parse rest positional (Map.add flag "true" values)
        | key::value::rest when allowed.Contains key ->
            if Map.containsKey key values then invalidArg "arguments" ("Duplicate option " + key)
            parse rest positional (Map.add key value values)
        | key::_ when key.StartsWith("--",StringComparison.Ordinal) -> invalidArg "arguments" ("Unknown or incomplete option " + key)
        | value::rest -> parse rest (value::positional) values
    parse (Array.toList arguments) [] Map.empty

[<EntryPoint>]
let main argv =
    use cancel = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun e -> e.Cancel<-true;cancel.Cancel())
    try
        if argv.Length=0 || argv=[|"--help"|] || argv=[|"help"|] then
            printf "%s" help
            0
        else
            let positional,values = options argv[1..]
            let require name = Map.tryFind name values |> Option.defaultWith (fun () -> invalidArg "arguments" ("Missing " + name))
            let state = Map.tryFind "--state-dir" values |> Option.defaultValue (Path.Combine(Environment.CurrentDirectory,".canonforge")) |> Path.GetFullPath
            let context () = Storage.context state (require "--context")
            match argv[0],positional with
            | "capabilities",[] ->
                emit {|schemaVersion=1;version="0.2.0-local";stages=[|"Build";"Install";"Configure";"Migrate";"Verify";"Release"|];operations=[|"process";"write-file";"capture";"compose-up";"postgres-migrate";"http";"release"|];commands=[|"capabilities";"schema";"doctor";"plan";"freeze";"emit-json";"mcp";"three-owner-check";"triage";"apply";"resume";"converge";"status";"history";"drift";"secret-set";"secret-list";"public-key";"release-verify";"bundle";"bundle-verify";"promote";"channels";"export";"destroy"|];execution="trusted-local";qualifiedPlatform="linux-x86_64";ruleAuthority="external-producer-contract"|}
                0
            | "three-owner-check",[fixturePath] ->
                let verdict, json = Mcp.evaluateThreeOwnerFixtureFile fixturePath
                printfn "%s" json
                if verdict.Admitted then 0 else 3
            | "triage",[] ->
                let ctxName = require "--context"
                let fixtureOpt = Map.tryFind "--fixture" values
                printfn "%s" (Mcp.evaluateOperatorTriage state ctxName fixtureOpt)
                0
            | "emit-json",[manifest] ->
                match Wire.parse(Storage.readBounded manifest) with
                | Ok w -> printfn "%s" (Wire.toJson w); 0
                | Error e -> invalidOp e
            | "mcp",[] ->
                let allowEffects =
                    Map.tryFind "--allow-effects" values = Some "true"
                    || Environment.GetEnvironmentVariable("HALLMARK_MCP_ALLOW_EFFECTS") = "true"
                Mcp.runStdio state allowEffects Console.In Console.Out
                0
            | "schema",[] ->
                printfn "%s" (Storage.readBounded(Path.Combine(AppContext.BaseDirectory,"schema","workflow-v1.schema.json")))
                0
            | "doctor",[] ->
                emit {|schemaVersion=1;runtime=Environment.Version.ToString();os=System.Runtime.InteropServices.RuntimeInformation.OSDescription;producer=Storage.hashFile(typeof<Prepared>.Assembly.Location);dockerCliAvailable=(Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator) |> Array.exists (fun p -> File.Exists(Path.Combine(p,"docker"))))|}
                0
            | "plan",[manifest] ->
                let prepared = Runner.prepare manifest state
                printfn "%s" (Plan.previewJson prepared.Plan)
                0
            | "freeze",[manifest;path] ->
                let prepared = Runner.prepare manifest state
                use preview = JsonDocument.Parse(Plan.previewJson prepared.Plan)
                let policyDigest = prepared.PolicyDigest
                let targetDigest = prepared.TargetDigest
                Storage.atomicOutput path (Storage.encode {|schemaVersion=1;planDigest=prepared.Digest;sourceDigest=prepared.SourceDigest;policyDigest=policyDigest;targetDigest=targetDigest;sourceFiles=prepared.Sources;secretVersions=prepared.SecretVersions;preview=preview.RootElement.Clone()|})
                emit {|schemaVersion=1;path=Path.GetFullPath path;planDigest=prepared.Digest;sourceDigest=prepared.SourceDigest;policyDigest=policyDigest;targetDigest=targetDigest;digest=Storage.hashFile path|}
                0
            | ("apply" | "resume" | "converge") as command,[manifest] ->
                let prepared = Runner.prepare manifest state
                match Map.tryFind "--plan-digest" values with
                | Some expected when expected<>prepared.Digest -> invalidOp "Reviewed plan digest does not match the resolved plan"
                | _ -> ()
                let prior = if command="resume" then Some(require "--run") else None
                let record = (if command="converge" then Runner.converge prepared (require "--run") cancel.Token else Runner.apply prepared prior cancel.Token) |> _.GetAwaiter().GetResult()
                emit record
                if record.Status="succeeded" then 0 else 1
            | "status",[] -> emit (Storage.runs(context()));0
            | "history",[] ->
                let directory = context()
                let count,head = Storage.verifyHistory directory
                emit {|schemaVersion=1;events=count;head=head;valid=true|}
                0
            | "drift",[] ->
                let record = Storage.loadRun (context()) (require "--run")
                let inspection = Runner.inspectDrift record cancel.Token |> _.GetAwaiter().GetResult()
                emit {|schemaVersion=1;runId=record.RunId;changed=inspection.Changed;unavailable=inspection.Unavailable;scope=inspection.Scope;current=inspection.Changed.Length=0 && inspection.Unavailable.Length=0|}
                if inspection.Unavailable.Length>0 then 3 elif inspection.Changed.Length=0 then 0 else 2
            | "secret-set",[name] ->
                let directory = context()
                use executionLock = Storage.lockContext directory
                use input = Console.OpenStandardInput()
                use buffer = new MemoryStream()
                let bytes = Array.zeroCreate<byte> 4096
                let mutable reading = true
                while reading do
                    let count = input.Read(bytes,0,bytes.Length)
                    if count=0 then reading<-false
                    elif buffer.Length+int64 count>65536L then invalidArg "stdin" "Secret exceeds 64 KiB"
                    else buffer.Write(bytes,0,count)
                let material = System.Text.UTF8Encoding(false,true).GetString(buffer.ToArray())
                let version = Storage.secretSet directory name material
                emit {|schemaVersion=1;name=name;version=version|}
                0
            | "secret-list",[] ->
                let path = Path.Combine(context(),"secrets")
                let entries = if Directory.Exists path then Directory.GetFiles(path,"*.json") |> Array.map (fun p -> Storage.secretEntry (context()) (Path.GetFileNameWithoutExtension p)) |> Array.map (fun p -> {|name=p.Name;version=p.Version|}) else [||]
                emit entries
                0
            | "public-key",[] ->
                let directory = context()
                use executionLock = Storage.lockContext directory
                use key = Storage.signingKey directory
                printfn "%s" (key.ExportSubjectPublicKeyInfoPem())
                0
            | "release-verify",[path] ->
                let record = Storage.verifyRelease path (File.ReadAllText(require "--public-key").Trim())
                emit {|schemaVersion=1;valid=true;name=record.Name;version=record.Version;artifactCount=record.Artifacts.Length|}
                0
            | "bundle",[path] ->
                let directory = context()
                use executionLock = Storage.lockContext directory
                let path = Bundles.create directory (require "--run") path (File.ReadAllText(require "--public-key").Trim())
                emit {|schemaVersion=1;path=path;digest=Storage.hashFile path|}
                0
            | "bundle-verify",[path] ->
                let release = Bundles.verify path (File.ReadAllText(require "--public-key").Trim())
                emit {|schemaVersion=1;valid=true;name=release.Name;version=release.Version;artifactCount=release.Artifacts.Length;authenticatedScope=[|"release";"attested-steps";"artifact-bytes"|];supplementalScope=[|"run";"history"|]|}
                0
            | "promote",[version] ->
                let directory = context()
                use executionLock = Storage.lockContext directory
                let trustedPublicKey = File.ReadAllText(require "--public-key").Trim()
                Runner.verifyPromotionGate directory version trustedPublicKey (Map.tryFind "--fixture" values)
                let path = Storage.promote directory version (require "--channel") trustedPublicKey
                emit {|schemaVersion=1;path=path;version=version;operation="local-release-pointer-only"|}
                0
            | "channels",[] ->
                let path = Path.Combine(context(),"channels")
                let channels = if Directory.Exists path then Directory.GetFiles(path,"*.json") |> Array.sort |> Array.map (fun file -> use doc=JsonDocument.Parse(Storage.readBounded file) in doc.RootElement.Clone()) else [||]
                emit channels
                0
            | "export",[path] ->
                let directory = context()
                let run = Storage.loadRun directory (require "--run")
                let count,head = Storage.verifyHistory directory
                Storage.atomicOutput path (Storage.encode {|schemaVersion=1;run=run;historyEvents=count;historyHead=head|})
                emit {|schemaVersion=1;path=Path.GetFullPath path;digest=Storage.hashFile path|}
                0
            | "destroy",[] ->
                let directory = context()
                use executionLock = Storage.lockContext directory
                let record = Storage.loadRun directory (require "--run")
                for step in Array.rev record.Steps do
                    let value key = step.Values |> Array.tryFind (fun p -> p.Key=key) |> Option.map _.Value
                    match value "project",value "file" with
                    | Some project,Some file when project.StartsWith("cf-",StringComparison.Ordinal) ->
                        Runner.requireOwnership state record.Context record.Name ("compose:"+project)
                        if not (step.Files |> Array.exists (fun f -> f.Path=file && File.Exists file && Storage.hashFile file=f.Digest)) then invalidOp "Teardown refused: compose definition is missing or changed"
                        let result = Processes.run (Processes.executable "docker") ["compose";"-p";project;"-f";file;"down";"--remove-orphans"] (Path.GetDirectoryName file) [] None 120 262144 [] cancel.Token |> _.GetAwaiter().GetResult()
                        if result.ExitCode<>0 then invalidOp "Owned compose teardown failed"
                    | _ -> ()
                Storage.append directory record.RunId "" "destroyed"
                emit {|schemaVersion=1;runId=record.RunId;status="destroyed"|}
                0
            | _ -> invalidArg "arguments" "Unknown command or wrong argument count; use --help"
    with
    | :? ArgumentException as e -> eprintfn "%s" (Storage.encode {|error=e.Message;stage="invocation"|});64
    | :? OperationCanceledException -> eprintfn "%s" (Storage.encode {|error="Cancelled";stage="execution"|});130
    | e -> eprintfn "%s" (Storage.encode {|error=e.Message;stage="execution"|});3
