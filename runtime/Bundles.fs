namespace CanonForge.Runtime

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open System.Security.Cryptography
open CanonForge.Lab.Serialization

module Bundles =
    let private artifactName (digest: string) =
        if not (System.Text.RegularExpressions.Regex.IsMatch(digest,"\\Asha256:[a-f0-9]{64}\\z")) then invalidOp "Invalid artifact identity"
        "artifacts/"+digest.Substring(7)
    let create directory runId output trustedPublicKey =
        let run = Storage.loadRun directory runId
        if run.Status<>"succeeded" then invalidOp "Only completed release runs can be bundled"
        let releaseSteps =
            run.Steps
            |> Array.choose (fun step ->
                step.Files
                |> Array.tryFind (fun f -> Path.GetDirectoryName(f.Path)=Path.Combine(directory,"releases"))
                |> Option.map (fun file -> step.Id, file))
        if releaseSteps.Length<>1 then invalidOp "Bundle requires exactly one recorded release"
        let releaseStageId, releaseFile = releaseSteps[0]
        let release = Storage.verifyRelease releaseFile.Path trustedPublicKey
        let origin = Storage.loadRun directory release.RunId
        let steps = origin.Steps |> Array.takeWhile (fun step -> step.Id <> releaseStageId)
        if ContentDigest.ofText(Storage.encode steps)<>release.EvidenceDigest then invalidOp "Release evidence is not available under its recorded identity"
        Storage.verifyHistory directory |> ignore
        let output = Path.GetFullPath output
        Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore
        let temporary = output+"."+Guid.NewGuid().ToString("N")+".tmp"
        try
            use stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)
            use archive = new ZipArchive(stream,ZipArchiveMode.Create,true)
            let fixedEpoch = DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero)
            let writeText name text =
                let entry = archive.CreateEntry(name,CompressionLevel.NoCompression)
                entry.LastWriteTime <- fixedEpoch
                use writer = new StreamWriter(entry.Open(),UTF8Encoding(false))
                writer.Write(text: string)
            writeText "release.json" (Storage.readBounded releaseFile.Path)
            writeText "attested-steps.json" (Storage.encode steps)
            writeText "run.json" (Storage.encode run)
            writeText "history.ndjson" (Storage.history directory |> Array.map Storage.encode |> String.concat "\n")
            for artifact in release.Artifacts |> Array.distinctBy _.Digest |> Array.sortBy _.Digest do
                let entry = archive.CreateEntry(artifactName artifact.Digest,CompressionLevel.NoCompression)
                entry.LastWriteTime <- fixedEpoch
                use source = File.OpenRead artifact.Path
                use target = entry.Open()
                source.CopyTo target
            archive.Dispose()
            stream.Dispose()
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.Move(temporary,output,false)
        finally if File.Exists temporary then File.Delete temporary
        output
    let verify path trustedPublicKey =
        use stream = File.OpenRead path
        if stream.Length>512L*1024L*1024L then invalidOp "Bundle exceeds 512 MiB"
        use archive = new ZipArchive(stream,ZipArchiveMode.Read)
        if archive.Entries.Count>10004 then invalidOp "Bundle entry count exceeds budget"
        let names = archive.Entries |> Seq.map _.FullName |> Seq.toArray
        if (Array.distinct names).Length<>names.Length then invalidOp "Duplicate bundle entry"
        let read name =
            let entry = archive.GetEntry name
            if isNull entry || entry.Length>8L*1024L*1024L then invalidOp "Required bounded bundle metadata is missing"
            use input = entry.Open()
            use reader = new StreamReader(input,Encoding.UTF8)
            let buffer = Array.zeroCreate<char> 4096
            let text = StringBuilder()
            let mutable count = reader.Read(buffer,0,buffer.Length)
            while count>0 do
                if text.Length+count>8*1024*1024 then invalidOp "Bundle metadata exceeded budget"
                text.Append(buffer,0,count) |> ignore
                count <- reader.Read(buffer,0,buffer.Length)
            text.ToString()
        let release = JsonSerializer.Deserialize<ReleaseRecord>(read "release.json")
        Storage.verifyReleaseSignature release trustedPublicKey
        if ContentDigest.ofText(read "attested-steps.json")<>release.EvidenceDigest then invalidOp "Attested execution evidence changed"
        let allowed = Set.ofArray(Array.append [|"release.json";"attested-steps.json";"run.json";"history.ndjson"|] (release.Artifacts |> Array.map (fun f -> artifactName f.Digest)))
        if names |> Array.exists (fun name -> not (allowed.Contains name)) then invalidOp "Unexpected bundle entry"
        let mutable total = 0L
        for artifact in release.Artifacts |> Array.distinctBy _.Digest do
            let entry = archive.GetEntry(artifactName artifact.Digest)
            if isNull entry then invalidOp "Released artifact is missing from bundle"
            use input = entry.Open()
            use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            let buffer = Array.zeroCreate<byte> 65536
            let mutable count = input.Read(buffer,0,buffer.Length)
            while count>0 do
                total <- total+int64 count
                if total>512L*1024L*1024L then invalidOp "Expanded bundle exceeds budget"
                hash.AppendData(buffer,0,count)
                count <- input.Read(buffer,0,buffer.Length)
            let digest = "sha256:"+Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
            if digest<>artifact.Digest then invalidOp "Released artifact bytes changed"
        release
