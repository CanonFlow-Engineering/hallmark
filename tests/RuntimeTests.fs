module CanonForge.Tests.Runtime

open System
open System.IO
open System.Threading
open System.Text.Json.Nodes
open System.IO.Compression
open Xunit
open CanonForge.Runtime

type Workspace() =
    let root = Path.Combine(Path.GetTempPath(),"canonforge-tests-"+Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(root) |> ignore
    member _.Root = root
    member _.State = Path.Combine(root,"state")
    member _.Write(name: string,text: string) = let path = Path.Combine(root,name) in File.WriteAllText(path,text);path
    member this.Manifest(steps) =
        this.Write("source.txt","original") |> ignore
        this.Write("workflow.json","""{"schemaVersion":1,"name":"fixture","context":"fixture","inputs":["source.txt"],"allowedExecutables":["/bin/sh"],"steps":["""+steps+"]}")
    interface IDisposable with member _.Dispose() = Directory.Delete(root,true)

let step script =
    """{"id":"probe","kind":"Build","effect":"Imperative","repeat":"ExecuteOnce","operation":{"type":"process","executable":"/bin/sh","arguments":["-c",""" + System.Text.Json.JsonSerializer.Serialize(script) + "]}}"
let apply path state = Runner.apply (Runner.prepare path state) None CancellationToken.None |> _.GetAwaiter().GetResult()
let resume path state run = Runner.apply (Runner.prepare path state) (Some run) CancellationToken.None |> _.GetAwaiter().GetResult()
let modify (json: string) (change: JsonNode -> unit) = let node=JsonNode.Parse json in change node;node.ToJsonString()

[<Theory>]
[<InlineData("unknown")>]
[<InlineData("duplicate")>]
[<InlineData("schema")>]
[<InlineData("effect")>]
[<InlineData("executable")>]
let ``strict manifest rejects malformed or dishonest declarations`` mode =
    use w = new Workspace()
    let path = w.Manifest(step "true")
    let json = File.ReadAllText path
    let invalid =
        match mode with
        | "duplicate" -> json.Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1")
        | "unknown" -> modify json (fun n -> n["mystery"]<-JsonValue.Create true)
        | "schema" -> modify json (fun n -> n["schemaVersion"]<-JsonValue.Create 99)
        | "effect" -> modify json (fun n -> (n["steps"][0])["operation"]<-JsonNode.Parse("""{"type":"write-file","path":"x","content":"bad"}"""))
        | _ -> modify json (fun n -> n["allowedExecutables"]<-JsonArray())
    Assert.True(Wire.parse invalid |> Result.isError)

[<Fact>]
let ``path traversal and symlinks are rejected`` () =
    use w = new Workspace()
    Assert.Throws<InvalidOperationException>(fun () -> Storage.child w.Root "../escape" |> ignore) |> ignore
    let target=w.Write("source.txt","source")
    File.CreateSymbolicLink(Path.Combine(w.Root,"link"),target) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> Storage.child w.Root "link" |> ignore) |> ignore

[<Fact>]
let ``changed source prevents resume before any effect`` () =
    use w = new Workspace()
    let path=w.Manifest(step "echo invoked >> count")
    let run=apply path w.State
    w.Write("source.txt","changed") |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State run.RunId |> ignore) |> ignore
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root,"count"))) |> ignore

[<Fact>]
let ``manual failures retain exit code and refuse replay without altering status`` () =
    use w = new Workspace()
    let path=w.Manifest(step "echo invoked >> count; exit 7")
    let run=apply path w.State
    Assert.Equal("failed",run.Status)
    Assert.Equal(7,run.Steps[0].ExitCode)
    Assert.Equal("Process probe exited with code 7 (expected 0)",run.Steps[0].Message)
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State run.RunId |> ignore) |> ignore
    let stored=Storage.loadRun (Storage.context w.State "fixture") run.RunId
    Assert.Equal("failed",stored.Status)
    Assert.Equal(run.Steps[0].Message,stored.Steps[0].Message)
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root,"count"))) |> ignore

[<Fact>]
let ``completed outputs are reused and changed outputs refuse resume`` () =
    use w = new Workspace()
    let path=w.Manifest((step "echo invoked >> count; echo stable > result").Replace("\"operation\":","\"outputs\":[\"result\"],\"operation\":"))
    let first=apply path w.State
    Assert.Equal("succeeded",(resume path w.State first.RunId).Status)
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root,"count"))) |> ignore
    w.Write("result","drift") |> ignore
    Assert.Single(Runner.drift first) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State first.RunId |> ignore) |> ignore

[<Fact>]
let ``process output limit terminates and discards partial output`` () = task {
    use w = new Workspace()
    let! result=Processes.run "/bin/sh" ["-c";"while :; do printf '0123456789abcdef'; done"] w.Root [] None 5 128 [] CancellationToken.None
    Assert.Equal("output-limit",result.Status)
    Assert.DoesNotContain("0123456789",result.Stdout)
}

[<Fact>]
let ``process deadline and cancellation are distinct outcomes`` () = task {
    use w = new Workspace()
    let! timed=Processes.run "/bin/sh" ["-c";"sleep 30"] w.Root [] None 1 128 [] CancellationToken.None
    Assert.Equal("timeout",timed.Status)
    use cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds 100.)
    let! cancelled=Processes.run "/bin/sh" ["-c";"sleep 30"] w.Root [] None 5 128 [] cancel.Token
    Assert.Equal("cancelled",cancelled.Status)
}

[<Fact>]
let ``secrets are encrypted redacted and version bound to resume`` () =
    use w = new Workspace()
    let directory=Storage.context w.State "fixture"
    let material="fixture-secret-"+Guid.NewGuid().ToString("N")
    Storage.secretSet directory "token" material |> ignore
    let script="printf '%s' \"$TOKEN\"; printf '%s' \"$TOKEN\" >&2"
    let json=(step script).Replace("\"arguments\":","\"environment\":{\"TOKEN\":{\"secret\":\"token\"}},\"arguments\":")
    let path=w.Manifest json
    let run=apply path w.State
    Assert.Equal("succeeded",run.Status)
    Assert.DoesNotContain(material,Storage.encode run)
    Assert.Equal(material,Storage.secretValue directory "token")
    for file in Directory.GetFiles(directory,"*",SearchOption.AllDirectories) do
        Assert.DoesNotContain(material,File.ReadAllText file)
    Storage.secretSet directory "token" "rotated" |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State run.RunId |> ignore) |> ignore

[<Fact>]
let ``artifact and event tampering are detected`` () =
    use w = new Workspace()
    let directory=Storage.context w.State "fixture"
    let source=w.Write("artifact","original")
    let path,_=Storage.storeArtifact directory source
    File.WriteAllText(path,"tampered")
    Assert.Throws<InvalidOperationException>(fun () -> Storage.storeArtifact directory source |> ignore) |> ignore
    Storage.append directory "run" "step" "succeeded"
    let history=Path.Combine(directory,"history.ndjson")
    File.WriteAllText(history,(File.ReadAllText history).Replace("succeeded","failed"))
    Assert.Throws<InvalidOperationException>(fun () -> Storage.verifyHistory directory |> ignore) |> ignore

[<Fact>]
let ``release requires explicit trust and detects artifact drift`` () =
    use w = new Workspace()
    let directory=Storage.context w.State "fixture"
    let source=w.Write("artifact","original")
    let path,digest=Storage.storeArtifact directory source
    let release,file=Storage.seal directory "fixture" "1.0.0" "plan" "run" "fixture" [|{Path=path;Digest=digest}|] "evidence"
    Assert.Equal("1.0.0",(Storage.verifyRelease file release.PublicKey).Version)
    Assert.Throws<InvalidOperationException>(fun () -> Storage.verifyRelease file "untrusted" |> ignore) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> Storage.seal directory "fixture" "1.0.0" "different-plan" "run" "fixture" [|{Path=path;Digest=digest}|] "evidence" |> ignore) |> ignore
    File.WriteAllText(path,"changed")
    Assert.Throws<InvalidOperationException>(fun () -> Storage.verifyRelease file release.PublicKey |> ignore) |> ignore

[<Fact>]
let ``undeclared output references fail before execution`` () =
    use w = new Workspace()
    let path=w.Manifest(step "echo {{missing.stdout}}")
    Assert.Throws<InvalidOperationException>(fun () -> Runner.prepare path w.State |> ignore) |> ignore

[<Fact>]
let ``cross context file ownership conflicts are refused`` () =
    use w = new Workspace()
    let path=w.Manifest("""{"id":"config","kind":"Configure","effect":"DesiredState","repeat":"Reconcile","operation":{"type":"write-file","path":"shared","content":"first"}}""")
    Assert.Equal("succeeded",(apply path w.State).Status)
    File.WriteAllText(path,(File.ReadAllText path).Replace("\"context\":\"fixture\"","\"context\":\"other\""))
    Assert.Equal("failed",(apply path w.State).Status)
    Assert.Equal("first",File.ReadAllText(Path.Combine(w.Root,"shared")))

[<Fact>]
let ``source mutation during execution prevents signed release`` () =
    use w = new Workspace()
    let steps=step "echo changed > source.txt" + """,{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["probe"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","true"]}},{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"operation":{"type":"release","version":"1.0.0"}}"""
    let run=apply (w.Manifest steps) w.State
    Assert.Equal("failed",run.Status)
    Assert.Contains("Source inputs changed",run.Steps[2].Message)
    Assert.False(File.Exists(Path.Combine(w.State,"fixture/releases/1.0.0.json")))

[<Fact>]
let ``convergence repairs desired state without rerunning imperative build`` () =
    use w = new Workspace()
    let steps=step "echo built >> build-count" + """,{"id":"config","kind":"Configure","effect":"DesiredState","repeat":"Reconcile","needs":["probe"],"operation":{"type":"write-file","path":"configuration","content":"desired"}},{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["config"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test \"$(cat configuration)\" = desired; echo checked >> verify-count"]}}"""
    let path=w.Manifest steps
    let prior=apply path w.State
    Assert.Equal("succeeded",prior.Status)
    w.Write("configuration","external drift") |> ignore
    let repaired=Runner.converge (Runner.prepare path w.State) prior.RunId CancellationToken.None |> _.GetAwaiter().GetResult()
    Assert.Equal("succeeded",repaired.Status)
    Assert.NotEqual<string>(prior.RunId,repaired.RunId)
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root,"build-count"))) |> ignore
    Assert.Equal(2,File.ReadAllLines(Path.Combine(w.Root,"verify-count")).Length)
    Assert.Equal("desired",File.ReadAllText(Path.Combine(w.Root,"configuration")))

[<Fact>]
let ``portable release verifies after removing original artifact paths and rejects tampering`` () =
    use w = new Workspace()
    let steps=step "printf artifact > result" + """,{"id":"capture","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":["probe"],"operation":{"type":"capture","path":"result"}},{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["capture"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test -s result"]}},{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"operation":{"type":"release","version":"1.0.0"}}"""
    let run=apply (w.Manifest steps) w.State
    Assert.Equal("succeeded",run.Status)
    let directory=Storage.context w.State "fixture"
    use key=Storage.signingKey directory
    let publicKey=key.ExportSubjectPublicKeyInfoPem()
    let output=Bundles.create directory run.RunId (Path.Combine(w.Root,"release.zip")) publicKey
    Directory.Delete(Path.Combine(directory,"artifacts"),true)
    Assert.Equal("1.0.0",(Bundles.verify output publicKey).Version)
    do
        use zip=ZipFile.Open(output,ZipArchiveMode.Update)
        let original=zip.Entries |> Seq.find (fun entry -> entry.FullName.StartsWith("artifacts/",StringComparison.Ordinal))
        let name=original.FullName
        original.Delete()
        use writer=new StreamWriter(zip.CreateEntry(name).Open())
        writer.Write("tampered")
    Assert.Throws<InvalidOperationException>(fun () -> Bundles.verify output publicKey |> ignore) |> ignore

[<Fact>]
let ``an existing corrupt release is refused when sealing the same version`` () =
    use w = new Workspace()
    let directory=Storage.context w.State "fixture"
    let source=w.Write("artifact","original")
    let path,digest=Storage.storeArtifact directory source
    let artifacts=[|{Path=path;Digest=digest}|]
    let release,file=Storage.seal directory "fixture" "1.0.0" "plan" "run" "fixture" artifacts "evidence"
    File.WriteAllText(file,Storage.encode {release with EvidenceDigest="corrupt"})
    Assert.Throws<InvalidOperationException>(fun () -> Storage.seal directory "fixture" "1.0.0" "plan" "run" "fixture" artifacts "evidence" |> ignore) |> ignore

[<Fact>]
let ``ambient credentials are not forwarded to subprocesses`` () = task {
    use w = new Workspace()
    let key="CF_TEST_AMBIENT_"+Guid.NewGuid().ToString("N")
    let material="ambient-material-"+Guid.NewGuid().ToString("N")
    Environment.SetEnvironmentVariable(key,material)
    try
        let! result=Processes.run "/bin/sh" ["-c";"env"] w.Root [] None 5 65536 [] CancellationToken.None
        Assert.Equal(0,result.ExitCode)
        Assert.DoesNotContain(material,result.Stdout)
        Assert.DoesNotContain(key,result.Stdout)
    finally Environment.SetEnvironmentVariable(key,null)
}

[<Fact>]
let ``oversized run metadata is rejected before replacing durable state`` () =
    use w = new Workspace()
    let run=apply (w.Manifest(step "true")) w.State
    let directory=Storage.context w.State "fixture"
    let oversized={run with Steps=[|{run.Steps[0] with Values=[|{Key="stdout";Value=String('x',9*1024*1024)}|]}|]}
    Assert.Throws<InvalidOperationException>(fun () -> Storage.saveRun directory oversized |> ignore) |> ignore
    Assert.Equal<string>(run.Checksum,(Storage.loadRun directory run.RunId).Checksum)

[<Fact>]
let ``repeated bundle creation produces identical byte-for-byte SHA-256 digests`` () =
    use w = new Workspace()
    let steps=step "printf deterministic-payload > result" + """,{"id":"capture","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":["probe"],"operation":{"type":"capture","path":"result"}},{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["capture"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test -s result"]}},{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"operation":{"type":"release","version":"1.0.0"}}"""
    let run=apply (w.Manifest steps) w.State
    Assert.Equal("succeeded",run.Status)
    let directory=Storage.context w.State "fixture"
    use key=Storage.signingKey directory
    let publicKey=key.ExportSubjectPublicKeyInfoPem()
    let bundleA=Bundles.create directory run.RunId (Path.Combine(w.Root,"release-a.zip")) publicKey
    let bundleB=Bundles.create directory run.RunId (Path.Combine(w.Root,"release-b.zip")) publicKey
    Assert.Equal(Storage.hashFile bundleA, Storage.hashFile bundleB)

[<Fact>]
let ``two-branch workflow seals release when its own verification ancestor passes before a later unrelated verify branch`` () =
    use w = new Workspace()
    let steps =
        step "printf release-branch > result"
        + """,{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["probe"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test -s result"]}}"""
        + """,{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"operation":{"type":"release","version":"1.0.0"}}"""
        + """,{"id":"aux-0","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":[],"operation":{"type":"capture","path":"source.txt"}}"""
        + """,{"id":"aux-1","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":["aux-0"],"operation":{"type":"capture","path":"source.txt"}}"""
        + """,{"id":"aux-2","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":["aux-1"],"operation":{"type":"capture","path":"source.txt"}}"""
        + """,{"id":"aux-verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["aux-2"],"operation":{"type":"capture","path":"source.txt"}}"""
    let run = apply (w.Manifest steps) w.State
    Assert.Equal("succeeded", run.Status)
    Assert.True(File.Exists(Path.Combine(w.State, "fixture/releases/1.0.0.json")))

[<Fact>]
let ``ExternalLedger recovery requires ledger readback and reconciles committed step without re-dispatching`` () =
    use w = new Workspace()
    let migrateStep =
        """{"id":"migrate","kind":"Migrate","effect":"Imperative","repeat":"ExecuteOnce","outputs":["schema-v1"],"retrySafety":"external-ledger","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","echo dispatched >> dispatch-count; printf 1 > schema-v1; exit 9"]}}"""
    let path = w.Manifest migrateStep
    let failedRun = apply path w.State
    Assert.Equal("failed", failedRun.Status)
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root, "dispatch-count"))) |> ignore

    // 1. Without ledger readback evidence, resume refuses replay
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State failedRun.RunId |> ignore) |> ignore
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root, "dispatch-count"))) |> ignore

    // 2. When target ledger readback confirms the migration committed before losing response, resume reconciles without re-dispatching
    let contextDir = Storage.context w.State "fixture"
    let ledgerDir = Path.Combine(contextDir, "ledger")
    Directory.CreateDirectory ledgerDir |> ignore
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"committed"}"""
    let recovered = resume path w.State failedRun.RunId
    Assert.Equal("succeeded", recovered.Status)
    Assert.Equal("reconciled-from-external-ledger", recovered.Steps[0].Message)
    Assert.Single(File.ReadAllLines(Path.Combine(w.Root, "dispatch-count"))) |> ignore

[<Fact>]
let ``F# hallmarkWorkflow DSL emission and JSON manifest produce identical frozen plan digests`` () =
    use w = new Workspace()
    w.Write("source.txt", "hello from hallmark") |> ignore
    let dslResult =
        hallmarkWorkflow {
            name "local-example"
            context "local-example"
            inputs ["source.txt"]
            allowedExecutables ["/bin/sh"]
            buildProcess "build" "/bin/sh" ["-eu"; "-c"; "mkdir -p build\ntr '[:lower:]' '[:upper:]' < source.txt > build/message.txt"] [] ["build/message.txt"]
            captureArtifact "artifact" "build/message.txt" ["build"]
            writeFile "install" CanonForge.Lab.Install "installed.txt" "{{artifact.digest}}\n" ["artifact"]
            writeFile "configure" CanonForge.Lab.Configure "service.json" "{\"artifact\":\"{{artifact.digest}}\"}\n" ["install"]
            buildProcess "migrate" "/bin/sh" ["-eu"; "-c"; "printf '1\\n' > schema-v1"] ["configure"] ["schema-v1"]
            verifyProcess "verify" "/bin/sh" ["-eu"; "-c"; "test -s schema-v1"] ["migrate"]
            sealRelease "release" "0.1.0" ["verify"]
        }
    let dslWorkflow =
        match dslResult with
        | Ok wf -> wf
        | Error err -> failwith ("DSL validation failed: " + err)
    let dslManifestPath = w.Write("dsl-workflow.json", Wire.toJson dslWorkflow)

    // Hand-authored JSON with different property order and formatting
    let rawJson = """{
  "context": "local-example",
  "name": "local-example",
  "schemaVersion": 1,
  "allowedExecutables": ["/bin/sh"],
  "inputs": ["source.txt"],
  "steps": [
    {"id":"build","kind":"Build","effect":"Imperative","repeat":"ExecuteOnce","operation":{"type":"process","executable":"/bin/sh","arguments":["-eu","-c","mkdir -p build\ntr '[:lower:]' '[:upper:]' < source.txt > build/message.txt"]},"outputs":["build/message.txt"],"retrySafety":"manual"},
    {"id":"artifact","kind":"Build","effect":"Observation","repeat":"Reconcile","needs":["build"],"operation":{"type":"capture","path":"build/message.txt"},"retrySafety":"reconcile"},
    {"id":"install","kind":"Install","effect":"DesiredState","repeat":"Reconcile","needs":["artifact"],"operation":{"type":"write-file","path":"installed.txt","content":"{{artifact.digest}}\n"},"retrySafety":"reconcile"},
    {"id":"configure","kind":"Configure","effect":"DesiredState","repeat":"Reconcile","needs":["install"],"operation":{"type":"write-file","path":"service.json","content":"{\"artifact\":\"{{artifact.digest}}\"}\n"},"retrySafety":"reconcile"},
    {"id":"migrate","kind":"Migrate","effect":"Imperative","repeat":"ExecuteOnce","needs":["configure"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-eu","-c","printf '1\\n' > schema-v1"]},"outputs":["schema-v1"],"retrySafety":"manual"},
    {"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["migrate"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-eu","-c","test -s schema-v1"]},"retrySafety":"reconcile"},
    {"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"operation":{"type":"release","version":"0.1.0"},"retrySafety":"manual"}
  ]
}"""
    // Adjust migrate kind in dslWorkflow to Migrate so both have the exact 6-stage kinds
    let migrateFixedDsl =
        { dslWorkflow with
            Steps =
                dslWorkflow.Steps
                |> List.map (fun s -> if s.Id = "migrate" then { s with Kind = CanonForge.Lab.Migrate } else s) }
    let dslFixedPath = w.Write("dsl-fixed.json", Wire.toJson migrateFixedDsl)
    let jsonManifestPath = w.Write("hand-authored.json", rawJson)

    let preparedFromDsl = Runner.prepare dslFixedPath w.State
    let preparedFromJson = Runner.prepare jsonManifestPath w.State
    Assert.Equal(preparedFromDsl.Digest, preparedFromJson.Digest)
    Assert.Equal(preparedFromDsl.SourceDigest, preparedFromJson.SourceDigest)
    Assert.Equal(preparedFromDsl.Workflow.CanonicalDefinition, preparedFromJson.Workflow.CanonicalDefinition)

[<Fact>]
let ``local stdio MCP server matches CLI plan digest Gate B decisions and enforces allow-effects`` () =
    use w = new Workspace()
    let manifestPath = w.Manifest(step "printf 'mcp-ok' > result")
    let cliPrepared = Runner.prepare manifestPath w.State

    // 1. initialize & tools/list
    let initResp = Mcp.handleJsonRpcLine w.State false """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}"""
    Assert.True(initResp.IsSome)
    Assert.Contains("hallmark-mcp", initResp.Value)

    let listResp = Mcp.handleJsonRpcLine w.State false """{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""
    Assert.True(listResp.IsSome)
    Assert.Contains("hallmark_plan", listResp.Value)
    Assert.Contains("hallmark_gate_b_evaluate", listResp.Value)

    // 2. hallmark_plan over MCP returns the exact same planDigest as CLI
    let planCall =
        sprintf """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"hallmark_plan","arguments":{"manifest":%s,"stateDir":%s}}}"""
            (System.Text.Json.JsonSerializer.Serialize manifestPath)
            (System.Text.Json.JsonSerializer.Serialize w.State)
    let planResp = Mcp.handleJsonRpcLine w.State false planCall
    Assert.True(planResp.IsSome)
    use planDoc = System.Text.Json.JsonDocument.Parse(planResp.Value)
    let mcpPlanDigest =
        planDoc.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("planDigest").GetString()
    Assert.Equal(cliPrepared.Digest, mcpPlanDigest)

    // 3. hallmark_gate_b_evaluate over MCP matches Admission.evaluateBoundReleaseGate
    let dummyDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    let gateCallMissing =
        sprintf """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"hallmark_gate_b_evaluate","arguments":{"requiredAssays":["FsAssay","PostgresAssay"],"lineage":"Active","receipts":[{"assay":"FsAssay","outcome":"Pass","receiptDigest":"%s"}]}}}"""
            dummyDigest
    let gateResp = Mcp.handleJsonRpcLine w.State false gateCallMissing
    use gateDoc = System.Text.Json.JsonDocument.Parse(gateResp.Value)
    let gateStruct = gateDoc.RootElement.GetProperty("result").GetProperty("structuredContent")
    Assert.False(gateStruct.GetProperty("admitted").GetBoolean())
    Assert.Equal("Inconclusive", gateStruct.GetProperty("aggregateOutcome").GetString())

    // 4. hallmark_apply is refused when allowEffects = false, and succeeds when allowEffects = true
    let applyCall =
        sprintf """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"hallmark_apply","arguments":{"manifest":%s,"planDigest":%s,"stateDir":%s}}}"""
            (System.Text.Json.JsonSerializer.Serialize manifestPath)
            (System.Text.Json.JsonSerializer.Serialize cliPrepared.Digest)
            (System.Text.Json.JsonSerializer.Serialize w.State)
    let refusedApply = Mcp.handleJsonRpcLine w.State false applyCall
    use refusedDoc = System.Text.Json.JsonDocument.Parse(refusedApply.Value)
    Assert.True(refusedDoc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean())
    Assert.Contains("MCP_EFFECTS_DISABLED", refusedApply.Value)

    let allowedApply = Mcp.handleJsonRpcLine w.State true applyCall
    use allowedDoc = System.Text.Json.JsonDocument.Parse(allowedApply.Value)
    Assert.False(allowedDoc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean())
    Assert.Equal("succeeded", allowedDoc.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("Status").GetString())

[<Fact>]
let ``three-owner release fixture evaluates identically across CLI evaluator and MCP server`` () =
    use w = new Workspace()
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let fixturePath = Path.Combine(repoRoot, "examples", "three-owner", "fixture.json")
    let verdict, cliJson = Mcp.evaluateThreeOwnerFixtureFile fixturePath
    Assert.True(verdict.Admitted)
    Assert.Equal<string list>(["backoffice-reporting"], verdict.ExcludedUnrelatedPackages)

    let mcpCall =
        sprintf """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"hallmark_three_owner_evaluate","arguments":{"fixturePath":%s}}}"""
            (System.Text.Json.JsonSerializer.Serialize fixturePath)
    let mcpResp = Mcp.handleJsonRpcLine w.State false mcpCall
    Assert.True(mcpResp.IsSome)
    use mcpDoc = System.Text.Json.JsonDocument.Parse(mcpResp.Value)
    let structured = mcpDoc.RootElement.GetProperty("result").GetProperty("structuredContent")
    Assert.True(structured.GetProperty("admitted").GetBoolean())
    let contentArr = mcpDoc.RootElement.GetProperty("result").GetProperty("content")
    Assert.Equal(cliJson, contentArr[0].GetProperty("text").GetString())

[<Fact>]
let ``operator triage identifies committed effect blocking owner and safe resume action across CLI and MCP`` () =
    use w = new Workspace()
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let baseFixture = Path.Combine(repoRoot, "examples", "three-owner", "fixture.json")
    let incidentFixture =
        Storage.readBounded baseFixture
        |> fun txt ->
            txt.Replace(
                "\"corrections\": []",
                """ "corrections": [{"correctionId":"VERA-CORR-2026-09","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Strictness drift on settlement_trace","effectiveAt":"2026-11-24T15:30:00Z","kind":"Withdrawn"}] """
            )
    let incidentPath = w.Write("incident-fixture.json", incidentFixture)
    let migrateStep =
        """{"id":"migrate","kind":"Migrate","effect":"Imperative","repeat":"ExecuteOnce","outputs":["schema-v1"],"retrySafety":"external-ledger","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","printf 1 > schema-v1; exit 9"]}}"""
    let manifestPath = w.Manifest migrateStep
    let failedRun = apply manifestPath w.State
    Assert.Equal("failed", failedRun.Status)

    let contextDir = Storage.context w.State "fixture"
    let ledgerDir = Path.Combine(contextDir, "ledger")
    Directory.CreateDirectory ledgerDir |> ignore
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"committed","version":"2","script":"V2__index_wallet_operations.sql","checksum":"-1934977602"}"""

    let triageJson = Mcp.evaluateOperatorTriage w.State "fixture" (Some incidentPath)
    use doc = System.Text.Json.JsonDocument.Parse(triageJson)
    let root = doc.RootElement
    let effects = root.GetProperty("committedEffects")
    let owners = root.GetProperty("responsibleBlockerOwners")
    Assert.Equal(failedRun.RunId, root.GetProperty("runId").GetString())
    Assert.Equal("migrate", effects[0].GetProperty("stepId").GetString())
    Assert.Equal("2", effects[0].GetProperty("version").GetString())
    Assert.Equal("opensearch-projection-team", owners[0].GetString())
    Assert.Contains("hallmark engine resume", root.GetProperty("safeNextAction").GetString())

[<Fact>]
let ``operator triage selects the latest run by start time`` () =
    use w = new Workspace()
    let directory = Storage.context w.State "fixture"
    let save id started status =
        Storage.saveRun directory {
            SchemaVersion = 1
            RunId = id
            Name = "fixture"
            Context = "fixture"
            PlanDigest = ""
            SourceDigest = ""
            Status = status
            Started = started
            Ended = started
            Steps = [||]
            Checksum = ""
        }
    save "ffffffffffffffffffffffffffffffff" "2026-11-24T12:00:00.0000000+00:00" "succeeded" |> ignore
    let latest = save "00000000000000000000000000000000" "2026-11-24T12:01:00.0000000+00:00" "failed"

    use report = System.Text.Json.JsonDocument.Parse(Mcp.evaluateOperatorTriage w.State "fixture" None)
    Assert.Equal(latest.RunId, report.RootElement.GetProperty("runId").GetString())
    Assert.Equal(latest.Status, report.RootElement.GetProperty("runStatus").GetString())

[<Fact>]
let ``completed ExternalLedger step refuses resume and surfaces conflict in triage when target ledger conflicts`` () =
    use w = new Workspace()
    let contextDir = Storage.context w.State "fixture"
    let ledgerDir = Path.Combine(contextDir, "ledger")
    Directory.CreateDirectory ledgerDir |> ignore
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"committed","version":"2","script":"V2__upgrade.sql","checksum":"-1934977602"}"""
    let steps =
        String.concat "," [
            """{"id":"migrate","kind":"Migrate","effect":"Imperative","repeat":"ExecuteOnce","outputs":["schema-v1"],"retrySafety":"external-ledger","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","printf 1 > schema-v1"]}}"""
            """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["migrate"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test -f gate-ok"]}}"""
            """{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"retrySafety":"external-ledger","operation":{"type":"release","version":"0.2.0"}}"""
        ]
    let path = w.Manifest steps
    let first = apply path w.State
    Assert.Equal("failed", first.Status)
    Assert.Equal("succeeded", first.Steps[0].Status)

    let _ = w.Write("gate-ok", "ok")
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"conflict","reason":"post-commit target ledger tamper"}"""

    use triageDoc = System.Text.Json.JsonDocument.Parse(Mcp.evaluateOperatorTriage w.State "fixture" None)
    let unresolved = triageDoc.RootElement.GetProperty("unresolvedSteps")
    Assert.Equal("migrate", unresolved[0].GetProperty("stepId").GetString())
    Assert.Equal("conflict", unresolved[0].GetProperty("status").GetString())

    let ex = Assert.Throws<InvalidOperationException>(fun () -> resume path w.State first.RunId |> ignore)
    Assert.Contains("External ledger conflicts with the frozen operation: post-commit target ledger tamper", ex.Message)

[<Fact>]
let ``release Gate B refuses active lineage when bound threeOwnerFixture has unresolved owner withdrawal`` () =
    use w = new Workspace()
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let baseFixture = Path.Combine(repoRoot, "examples", "three-owner", "fixture.json")
    let withdrawnFixture =
        Storage.readBounded baseFixture
        |> fun txt ->
            txt.Replace(
                "\"corrections\": []",
                """ "corrections": [{"correctionId":"VERA-CORR-2026-09","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Strictness drift","effectiveAt":"2026-11-24T15:30:00Z","kind":"Withdrawn"}] """
            )
    let fixturePath = w.Write("withdrawn-owner.json", withdrawnFixture)
    let policyJson =
        Storage.encode {|
            requiredAssays = ([||] : string array)
            lineage = "Active"
            threeOwnerFixture = fixturePath
            receipts = ([||] : obj array)
        |}
    let _ = w.Write("gate-b-policy.json", policyJson)
    let steps =
        String.concat "," [
            """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","true"]}}"""
            """{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"retrySafety":"external-ledger","operation":{"type":"release","version":"0.2.0"}}"""
        ]
    let path = w.Manifest steps
    let run = apply path w.State
    Assert.Equal("failed", run.Status)
    Assert.Contains("ReleaseWithdrawn", run.Steps[1].Message)
    Assert.Contains("VERA-CORR-2026-09", run.Steps[1].Message)

[<Fact>]
let ``completed ExternalLedger step requires observed ledger readback and refuses unobserved or absent ledger`` () =
    let unobserved = CanonForge.Execution.Recovery.decideWithLedger true true CanonForge.Execution.ExternalLedger CanonForge.Execution.Completed CanonForge.Execution.Unobserved
    let absent = CanonForge.Execution.Recovery.decideWithLedger true true CanonForge.Execution.ExternalLedger CanonForge.Execution.Completed CanonForge.Execution.AbsentFromLedger
    let committed = CanonForge.Execution.Recovery.decideWithLedger true true CanonForge.Execution.ExternalLedger CanonForge.Execution.Completed (CanonForge.Execution.CommittedInLedger true)
    match unobserved with
    | CanonForge.Execution.Refuse msg -> Assert.Contains("External ledger readback is required", msg)
    | other -> failwithf "Expected Refuse for Unobserved ExternalLedger step, got %A" other
    match absent with
    | CanonForge.Execution.Refuse msg -> Assert.Contains("External ledger no longer records", msg)
    | other -> failwithf "Expected Refuse for AbsentFromLedger ExternalLedger step, got %A" other
    Assert.Equal(CanonForge.Execution.Reuse, committed)

[<Fact>]
let ``governed release attests raw Assay receipt files in bundle and refuses promotion after approval withdrawal`` () =
    use w = new Workspace()
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let baseFixture = Path.Combine(repoRoot, "examples", "three-owner", "fixture.json")
    let policyLockPath = Path.Combine(repoRoot, "fsassay-policy.lock.json")
    let activeOwnerPath = w.Write("three-owner.json", Storage.readBounded baseFixture)
    let commitSha = "50ab9bbdf403d22fa920133741fba2cc3296612b"
    let treeSha = "c20747a38f7fbdd4c79ea0225003bb38a7378d8c"
    let fsReceiptJson, policySha = Runner.createFsAssayReceiptJson commitSha treeSha policyLockPath
    let rawAssayPath = w.Write("fsassay.json", fsReceiptJson)
    let rawAssayDigest = Storage.hashFile rawAssayPath

    let expectedProducers = [|
        {|
            assay = "FsAssay"
            tool = "FsAssay"
            toolVersion = "1.0.4"
            commitSha = commitSha
            treeSha = treeSha
            policySha256 = policySha
        |}
    |]
    let initialPolicyJson =
        Storage.encode {|
            requiredAssays = [| "FsAssay" |]
            expectedProducers = expectedProducers
            lineage = "Active"
            threeOwnerFixture = activeOwnerPath
            receipts = ([||] : obj array)
        |}
    let _ = w.Write("gate-b-policy.json", initialPolicyJson)

    let steps =
        String.concat "," [
            """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","true"]}}"""
            """{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"retrySafety":"external-ledger","operation":{"type":"release","version":"0.2.0"}}"""
        ]
    let manifestPath = w.Manifest steps
    let prepared = Runner.prepare manifestPath w.State
    let policyDigest = prepared.PolicyDigest
    let targetDigest = prepared.TargetDigest
    let policyJson =
        Storage.encode {|
            requiredAssays = [| "FsAssay" |]
            expectedProducers = expectedProducers
            lineage = "Active"
            threeOwnerFixture = activeOwnerPath
            receipts = [|
                {|
                    assay = "FsAssay"
                    outcome = "Pass"
                    receiptDigest = rawAssayDigest
                    receiptPath = rawAssayPath
                    planDigest = prepared.Digest
                    sourceDigest = prepared.SourceDigest
                    policyDigest = policyDigest
                    targetDigest = targetDigest
                |}
            |]
        |}
    let _ = w.Write("gate-b-policy.json", policyJson)
    let run = apply manifestPath w.State
    Assert.Equal("succeeded", run.Status)

    let contextDir = Storage.context w.State "fixture"
    use key = Storage.signingKey contextDir
    let publicKey = key.ExportSubjectPublicKeyInfoPem()
    let bundlePath = Bundles.create contextDir run.RunId (Path.Combine(w.Root, "governed.zip")) publicKey
    let verifiedRelease = Bundles.verify bundlePath publicKey
    // gate-b-policy.json, fsassay.json, and three-owner.json are attested inside the bundle
    Assert.Equal(3, verifiedRelease.Artifacts.Length)

    // Post-seal withdrawal in three-owner.json blocks both promotion and convergence
    let withdrawnText =
        (Storage.readBounded baseFixture).Replace(
            "\"corrections\": []",
            """ "corrections": [{"correctionId":"VERA-CORR-POST-SEAL","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Post-seal regression","effectiveAt":"2026-11-25T00:00:00Z","kind":"Withdrawn"}] """
        )
    File.WriteAllText(activeOwnerPath, withdrawnText)
    let promoEx = Assert.Throws<InvalidOperationException>(fun () -> Runner.verifyPromotionGate contextDir "0.2.0" publicKey None)
    Assert.Contains("VERA-CORR-POST-SEAL", promoEx.Message)
    let convEx = Assert.Throws<InvalidOperationException>(fun () ->
        Runner.converge (Runner.prepare manifestPath w.State) run.RunId CancellationToken.None |> _.GetAwaiter().GetResult() |> ignore)
    Assert.Contains("VERA-CORR-POST-SEAL", convEx.Message)

    // Re-authorizing the owner fixture allows promotion
    File.WriteAllText(activeOwnerPath, Storage.readBounded baseFixture)
    Runner.verifyPromotionGate contextDir "0.2.0" publicKey None
    let promoted = Storage.promote contextDir "0.2.0" "prod" publicKey
    Assert.True(File.Exists promoted)

    // A release signed by a different runId cannot be claimed during release recovery of an interrupted release step
    let interruptedRun =
        Storage.saveRun contextDir {
            run with
                RunId = "22222222222222222222222222222222"
                Status = "running"
                Steps = [|
                    run.Steps[0]
                    { run.Steps[1] with Status = "running"; Files = [||]; Values = [||] }
                |]
        }
    let mismatchRunEx = Assert.Throws<InvalidOperationException>(fun () ->
        resume manifestPath w.State interruptedRun.RunId |> ignore)
    Assert.Contains("does not match the originating run", mismatchRunEx.Message)

[<Fact>]
let ``state directories are created with 0700 mode and triage refuses promotion when a ledger conflict exists on a succeeded run`` () =
    use w = new Workspace()
    let contextDir = Storage.context w.State "fixture"
    if not (OperatingSystem.IsWindows()) then
        let userOnly = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
        Assert.Equal(userOnly, File.GetUnixFileMode w.State)
        Assert.Equal(userOnly, File.GetUnixFileMode contextDir)

    let _ =
        Storage.saveRun contextDir {
            SchemaVersion = 1
            RunId = "11111111111111111111111111111111"
            Name = "fixture"
            Context = "fixture"
            PlanDigest = ""
            SourceDigest = ""
            Status = "succeeded"
            Started = "2026-11-25T00:00:00Z"
            Ended = "2026-11-25T00:01:00Z"
            Steps = [||]
            Checksum = ""
        }
    let ledgerDir = Path.Combine(contextDir, "ledger")
    Storage.createPrivateDirectory ledgerDir
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"conflict","reason":"rolled back after completion"}"""
    use doc = System.Text.Json.JsonDocument.Parse(Mcp.evaluateOperatorTriage w.State "fixture" None)
    let action = doc.RootElement.GetProperty("safeNextAction").GetString()
    Assert.Contains("do not promote or resume", action)
    Assert.DoesNotContain("before promotion", action)

[<Fact>]
let ``ticket 1 release recovery succeeds across apply converge converge and resumes post-signing interruption into verifiable bundle`` () =
    use w = new Workspace()
    let contextDir = Storage.context w.State "fixture"
    let ledgerDir = Path.Combine(contextDir, "ledger")
    Storage.createPrivateDirectory ledgerDir
    Storage.atomic (Path.Combine(ledgerDir, "migrate.json")) """{"status":"committed","version":"2","script":"V2__wallet.sql","checksum":"12345"}"""
    let steps =
        String.concat "," [
            """{"id":"migrate","kind":"Migrate","effect":"Imperative","repeat":"ExecuteOnce","outputs":["schema-v1"],"retrySafety":"external-ledger","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","printf 'dispatch\n' >> dispatches.log; printf 1 > schema-v1"]}}"""
            """{"id":"configure","kind":"Configure","effect":"DesiredState","repeat":"Reconcile","needs":["migrate"],"operation":{"type":"write-file","path":"service.cfg","content":"ready\n"}}"""
            """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","needs":["configure"],"operation":{"type":"process","executable":"/bin/sh","arguments":["-c","test -s schema-v1 && test -s service.cfg"]}}"""
            """{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"retrySafety":"external-ledger","operation":{"type":"release","version":"0.2.0"}}"""
        ]
    let manifestPath = w.Manifest steps
    let firstRun = apply manifestPath w.State
    Assert.Equal("succeeded", firstRun.Status)
    let releasePath = Path.Combine(contextDir, "releases", "0.2.0.json")
    let originalReleaseDigest = Storage.hashFile releasePath

    // apply -> converge -> converge against the same signed release
    let firstConverge = Runner.converge (Runner.prepare manifestPath w.State) firstRun.RunId CancellationToken.None |> _.GetAwaiter().GetResult()
    Assert.Equal("succeeded", firstConverge.Status)
    let secondConverge = Runner.converge (Runner.prepare manifestPath w.State) firstConverge.RunId CancellationToken.None |> _.GetAwaiter().GetResult()
    Assert.Equal("succeeded", secondConverge.Status)
    Assert.Equal(originalReleaseDigest, Storage.hashFile releasePath)
    Assert.Equal(1, File.ReadAllLines(Path.Combine(w.Root, "dispatches.log")).Length)

    use key = Storage.signingKey contextDir
    let publicKey = key.ExportSubjectPublicKeyInfoPem()
    let convergedBundle = Bundles.create contextDir secondConverge.RunId (Path.Combine(w.Root, "converged-2.zip")) publicKey
    let verifiedConverged = Bundles.verify convergedBundle publicKey
    Assert.Equal(firstRun.RunId, verifiedConverged.RunId)

    // Simulate a crash right after signing on the original run (release step left running with no recorded Files/Values)
    let crashedAfterSeal =
        Storage.saveRun contextDir {
            firstRun with
                Status = "running"
                Ended = ""
                Steps = [|
                    firstRun.Steps[0]
                    firstRun.Steps[1]
                    firstRun.Steps[2]
                    { firstRun.Steps[3] with Status = "running"; Ended = ""; Message = ""; Files = [||]; Values = [||] }
                |]
        }
    let resumedAfterCrash = resume manifestPath w.State crashedAfterSeal.RunId
    Assert.Equal("succeeded", resumedAfterCrash.Status)
    Assert.Equal("reconciled-from-external-ledger", resumedAfterCrash.Steps[3].Message)
    Assert.Equal(originalReleaseDigest, Storage.hashFile releasePath)
    Assert.Equal(1, File.ReadAllLines(Path.Combine(w.Root, "dispatches.log")).Length)

    let resumedBundle = Bundles.create contextDir resumedAfterCrash.RunId (Path.Combine(w.Root, "resumed-crash.zip")) publicKey
    let verifiedResumed = Bundles.verify resumedBundle publicKey
    Assert.Equal(firstRun.RunId, verifiedResumed.RunId)

[<Fact>]
let ``ticket 2 frozen Gate B requirements change plan digest refuse mismatched FsAssay revision and bind promotion to release approvals`` () =
    use w = new Workspace()
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let baseFixture = Path.Combine(repoRoot, "examples", "three-owner", "fixture.json")
    let policyLockPath = Path.Combine(repoRoot, "fsassay-policy.lock.json")
    let ownerPath = w.Write("three-owner.json", Storage.readBounded baseFixture)
    let commitA = "1111111111111111111111111111111111111111"
    let treeA = "2222222222222222222222222222222222222222"
    let commitB = "9999999999999999999999999999999999999999"
    let receiptJsonA, policySha = Runner.createFsAssayReceiptJson commitA treeA policyLockPath
    let receiptJsonB, _ = Runner.createFsAssayReceiptJson commitB treeA policyLockPath
    let receiptPathA = w.Write("fsassay-a.json", receiptJsonA)
    let receiptPathB = w.Write("fsassay-b.json", receiptJsonB)

    let steps =
        String.concat "," [
            """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","true"]}}"""
            """{"id":"release","kind":"Release","effect":"Imperative","repeat":"ExecuteOnce","needs":["verify"],"retrySafety":"external-ledger","operation":{"type":"release","version":"0.2.0"}}"""
        ]
    let manifestPath = w.Manifest steps

    let writePolicy (assays: string array) (expectedCommit: string) (receipts: obj array) =
        let json =
            Storage.encode {|
                requiredAssays = assays
                expectedProducers = [|
                    {|
                        assay = "FsAssay"
                        tool = "FsAssay"
                        toolVersion = "1.0.4"
                        commitSha = expectedCommit
                        treeSha = treeA
                        policySha256 = policySha
                    |}
                |]
                lineage = "Active"
                threeOwnerFixture = ownerPath
                receipts = receipts
            |}
        w.Write("gate-b-policy.json", json) |> ignore

    writePolicy [| "FsAssay" |] commitA [||]
    let basePrepared = Runner.prepare manifestPath w.State

    // Changing requiredAssays changes PlanDigest
    writePolicy [| "FsAssay"; "PostgresAssay" |] commitA [||]
    let changedAssaysPrepared = Runner.prepare manifestPath w.State
    Assert.NotEqual<string>(basePrepared.Digest, changedAssaysPrepared.Digest)

    // Changing expectedProducers commitSha changes PlanDigest
    writePolicy [| "FsAssay" |] commitB [||]
    let changedProducerPrepared = Runner.prepare manifestPath w.State
    Assert.NotEqual<string>(basePrepared.Digest, changedProducerPrepared.Digest)

    // Changing an approval subject in threeOwnerFixture changes PlanDigest
    writePolicy [| "FsAssay" |] commitA [||]
    File.WriteAllText(ownerPath, (Storage.readBounded baseFixture).Replace("\"candidateVersion\": \"1.2.0\"", "\"candidateVersion\": \"1.9.0\""))
    let changedSubjectPrepared = Runner.prepare manifestPath w.State
    Assert.NotEqual<string>(basePrepared.Digest, changedSubjectPrepared.Digest)

    // Restore original approval subjects; adding receipts keeps PlanDigest identical to basePrepared
    File.WriteAllText(ownerPath, Storage.readBounded baseFixture)
    writePolicy [| "FsAssay" |] commitA [|
        {|
            assay = "FsAssay"
            outcome = "Pass"
            receiptDigest = Storage.hashFile receiptPathB
            receiptPath = receiptPathB
            planDigest = basePrepared.Digest
            sourceDigest = basePrepared.SourceDigest
            policyDigest = basePrepared.PolicyDigest
            targetDigest = basePrepared.TargetDigest
        |}
    |]
    let withWrongRevisionReceipt = Runner.prepare manifestPath w.State
    Assert.Equal(basePrepared.Digest, withWrongRevisionReceipt.Digest)

    // Receipt for another revision (commitB != commitA) is refused by FsAssay's context validator
    let refusedRun = apply manifestPath w.State
    Assert.Equal("failed", refusedRun.Status)
    Assert.Contains("does not match expected", refusedRun.Steps[1].Message)

    // Matching revision receipt (commitA) is admitted and seals release
    writePolicy [| "FsAssay" |] commitA [|
        {|
            assay = "FsAssay"
            outcome = "Pass"
            receiptDigest = Storage.hashFile receiptPathA
            receiptPath = receiptPathA
            planDigest = basePrepared.Digest
            sourceDigest = basePrepared.SourceDigest
            policyDigest = basePrepared.PolicyDigest
            targetDigest = basePrepared.TargetDigest
        |}
    |]
    let admittedRun = resume manifestPath w.State refusedRun.RunId
    Assert.Equal("succeeded", admittedRun.Status)

    // At promotion: withdrawal leaves channel unchanged; valid replacement allows promotion
    let contextDir = Storage.context w.State "fixture"
    use key = Storage.signingKey contextDir
    let publicKey = key.ExportSubjectPublicKeyInfoPem()
    let channelPath = Path.Combine(contextDir, "channels", "prod.json")
    let withdrawnText =
        (Storage.readBounded baseFixture).Replace(
            "\"corrections\": []",
            """ "corrections": [{"correctionId":"VERA-CORR-T2","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Withdrawn approval","effectiveAt":"2026-11-25T00:00:00Z","kind":"Withdrawn"}] """
        )
    File.WriteAllText(ownerPath, withdrawnText)
    Assert.Throws<InvalidOperationException>(fun () -> Runner.verifyPromotionGate contextDir "0.2.0" publicKey None) |> ignore
    Assert.False(File.Exists channelPath)

    let replacementFixture =
        Storage.readBounded baseFixture
        |> fun s ->
            s.Replace(
                "\"corrections\": []",
                """ "corrections": [{"correctionId":"VERA-CORR-T2","affectedReceiptDigest":"sha256:cccc333333333333333333333333333333333333333333333333333333333333","actor":"opensearch-projection-team","reason":"Withdrawn approval","effectiveAt":"2026-11-25T00:00:00Z","kind":"Withdrawn"}] """
            ).Replace(
                "\"receipts\": [",
                """ "receipts": [{"packageId":"wallet-search-os","ownerTeam":"opensearch-projection-team","assay":"OpenSearchAssay","outcome":"Pass","receiptDigest":"sha256:dddd444444444444444444444444444444444444444444444444444444444444","sourceDigest":"sha256:3333333333333333333333333333333333333333333333333333333333333333"},"""
            )
    File.WriteAllText(ownerPath, replacementFixture)
    Runner.verifyPromotionGate contextDir "0.2.0" publicKey None
    let promoted = Storage.promote contextDir "0.2.0" "prod" publicKey
    Assert.True(File.Exists promoted)

[<Fact>]
let ``ticket 3 MCP hallmark_plan returns identical digests to Runner prepare and CLI freeze`` () =
    use w = new Workspace()
    let _ = w.Write("gate-b-policy.json", """{"requiredAssays":["FsAssay"],"lineage":"Active","receipts":[]}""")
    let manifestPath = w.Manifest """{"id":"verify","kind":"Verify","effect":"Observation","repeat":"Reconcile","operation":{"type":"process","executable":"/bin/sh","arguments":["-c","true"]}}"""
    let prepared = Runner.prepare manifestPath w.State
    let req =
        sprintf """{"jsonrpc":"2.0","id":42,"method":"tools/call","params":{"name":"hallmark_plan","arguments":{"manifest":%s,"stateDir":%s}}}"""
            (System.Text.Json.JsonSerializer.Serialize manifestPath)
            (System.Text.Json.JsonSerializer.Serialize w.State)
    let resp = Mcp.handleJsonRpcLine w.State false req
    Assert.True(resp.IsSome)
    use doc = System.Text.Json.JsonDocument.Parse(resp.Value)
    let structured = doc.RootElement.GetProperty("result").GetProperty("structuredContent")
    Assert.Equal(prepared.Digest, structured.GetProperty("planDigest").GetString())
    Assert.Equal(prepared.SourceDigest, structured.GetProperty("sourceDigest").GetString())
    Assert.Equal(prepared.PolicyDigest, structured.GetProperty("policyDigest").GetString())
    Assert.Equal(prepared.TargetDigest, structured.GetProperty("targetDigest").GetString())

[<Fact>]
let ``ticket 4 private state uses 0700 and 0600 permissions while atomicOutput preserves existing 0755 output directory`` () =
    use w = new Workspace()
    if not (OperatingSystem.IsWindows()) then
        let freshState = Path.Combine(w.Root, "fresh-private-state")
        let publicOutDir = Path.Combine(w.Root, "public-out")
        let mode755 =
            UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute
            ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherExecute
        let mode700 = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
        let mode600 = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
        Directory.CreateDirectory(publicOutDir) |> ignore
        File.SetUnixFileMode(publicOutDir, mode755)

        let manifestPath = w.Manifest """{"id":"write","kind":"Configure","effect":"DesiredState","repeat":"Reconcile","operation":{"type":"write-file","path":"public-out/configured.txt","content":"hello"}}"""
        let prepared = Runner.prepare manifestPath freshState
        let outFile = Path.Combine(publicOutDir, "frozen-plan.json")
        Storage.atomicOutput outFile (CanonForge.Lab.Serialization.Plan.previewJson prepared.Plan)
        let run = Runner.apply prepared None CancellationToken.None |> _.GetAwaiter().GetResult()
        Assert.Equal("succeeded", run.Status)

        Assert.Equal(mode755, File.GetUnixFileMode publicOutDir)
        Assert.Equal(mode700, File.GetUnixFileMode freshState)
        for entry in Directory.EnumerateFileSystemEntries(freshState, "*", SearchOption.AllDirectories) do
            if Directory.Exists entry then
                Assert.Equal(mode700, File.GetUnixFileMode entry)
            else
                Assert.Equal(mode600, File.GetUnixFileMode entry)

[<Fact>]
let ``ticket 5 persisted PostgreSQL ledger conflict surfaces in CLI and MCP triage and resolves on committed readback`` () =
    use w = new Workspace()
    let contextDir = Storage.context w.State "wallet-release"
    let run =
        Storage.saveRun contextDir {
            SchemaVersion = 1
            RunId = "aaaa1111222233334444555566667777"
            Name = "wallet-release"
            Context = "wallet-release"
            PlanDigest = "sha256:1111"
            SourceDigest = "sha256:2222"
            Status = "running"
            Started = "2026-11-25T00:00:00Z"
            Ended = ""
            Steps = [|
                { Id = "migrate"; Status = "running"; ExitCode = 0; Started = "2026-11-25T00:00:00Z"; Ended = ""; Message = ""; Values = [||]; Files = [||] }
            |]
            Checksum = ""
        }
    let ledgerFile = Path.Combine(contextDir, "ledger", "migrate.json")
    Storage.atomic ledgerFile (
        Storage.encode {|
            status = "conflict"
            runId = run.RunId
            stepId = "migrate"
            systemIdentifier = "7711743786825170983"
            databaseOid = 16384L
            version = "2"
            script = "V2__index_wallet_operations.sql"
            reason = "Flyway checksum conflicts with the frozen migration (expected -1934977602, observed Some 0)"
        |}
    )
    let cliTriage = Mcp.evaluateOperatorTriage w.State "wallet-release" None
    let mcpReq =
        sprintf """{"jsonrpc":"2.0","id":99,"method":"tools/call","params":{"name":"hallmark_triage","arguments":{"context":"wallet-release","stateDir":%s}}}"""
            (System.Text.Json.JsonSerializer.Serialize w.State)
    let mcpResp = Mcp.handleJsonRpcLine w.State false mcpReq
    use cliDoc = System.Text.Json.JsonDocument.Parse(cliTriage)
    use mcpDoc = System.Text.Json.JsonDocument.Parse(mcpResp.Value)
    let mcpStructured = mcpDoc.RootElement.GetProperty("result").GetProperty("structuredContent")
    Assert.Equal(cliTriage, Storage.encode mcpStructured)

    let unresolved = cliDoc.RootElement.GetProperty("unresolvedSteps")
    Assert.Equal(1, unresolved.GetArrayLength())
    Assert.Equal(run.RunId, unresolved[0].GetProperty("runId").GetString())
    Assert.Equal("migrate", unresolved[0].GetProperty("stepId").GetString())
    Assert.Equal("conflict", unresolved[0].GetProperty("status").GetString())
    Assert.Equal("7711743786825170983", unresolved[0].GetProperty("systemIdentifier").GetString())
    Assert.Equal(16384L, unresolved[0].GetProperty("databaseOid").GetInt64())
    Assert.Equal("2", unresolved[0].GetProperty("version").GetString())
    Assert.Contains("do not promote or resume", cliDoc.RootElement.GetProperty("safeNextAction").GetString())

    // Resolve conflict on committed readback
    Storage.atomic ledgerFile (
        Storage.encode {|
            status = "committed"
            runId = run.RunId
            stepId = "migrate"
            systemIdentifier = "7711743786825170983"
            databaseOid = 16384L
            version = "2"
            script = "V2__index_wallet_operations.sql"
            checksum = "-1934977602"
            ledgerEntries = 2
        |}
    )
    use resolvedDoc = System.Text.Json.JsonDocument.Parse(Mcp.evaluateOperatorTriage w.State "wallet-release" None)
    Assert.Equal(1, resolvedDoc.RootElement.GetProperty("committedEffects").GetArrayLength())
    Assert.DoesNotContain("External ledger conflict detected", resolvedDoc.RootElement.GetProperty("safeNextAction").GetString())
