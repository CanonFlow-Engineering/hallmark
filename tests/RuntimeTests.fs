module Hallmark.Tests.Runtime

open System
open System.IO
open System.Threading
open System.Text.Json.Nodes
open System.IO.Compression
open Xunit
open Hallmark.Runtime

type Workspace() =
    let root = Path.Combine(Path.GetTempPath(),"hallmark-tests-"+Guid.NewGuid().ToString("N"))
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
    Assert.Throws<InvalidOperationException>(fun () -> resume path w.State run.RunId |> ignore) |> ignore
    let stored=Storage.loadRun (Storage.context w.State "fixture") run.RunId
    Assert.Equal("failed",stored.Status)
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
