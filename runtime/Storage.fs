namespace CanonForge.Runtime

open System
open System.IO
open System.Text
open System.Text.Json
open System.Security.Cryptography
open CanonForge.Lab.Serialization

type Property = { Key: string; Value: string }
type FileIdentity = { Path: string; Digest: string }
type StepRecord = { Id: string; Status: string; ExitCode: int; Started: string; Ended: string; Message: string; Values: Property array; Files: FileIdentity array }
type RunRecord = { SchemaVersion: int; RunId: string; Name: string; Context: string; PlanDigest: string; SourceDigest: string; Status: string; Started: string; Ended: string; Steps: StepRecord array; Checksum: string }
type HistoryEvent = { Sequence: int; Previous: string; RunId: string; Stage: string; Status: string; At: string; Hash: string }
type SecretEntry = { Name: string; Version: string; Nonce: string; Tag: string; Ciphertext: string }
type ReleaseRecord = { SchemaVersion: int; Name: string; Version: string; PlanDigest: string; RunId: string; Context: string; Artifacts: FileIdentity array; EvidenceDigest: string; PublicKey: string; Signature: string }

module Storage =
    let jsonOptions = JsonSerializerOptions(WriteIndented=false)
    let encode value = JsonSerializer.Serialize(value, jsonOptions)
    let hashBytes bytes = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes: byte[])).ToLowerInvariant()
    let hashFile path =
        use stream = File.OpenRead path
        "sha256:" + Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()
    let now () = DateTimeOffset.UtcNow.ToString("O")
    let name label value =
        if String.IsNullOrWhiteSpace value || not (System.Text.RegularExpressions.Regex.IsMatch(value,"\\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,95}\\z")) then invalidOp (label + " is not a bounded identifier")
        value
    let child root (relative: string) =
        if String.IsNullOrWhiteSpace relative || relative |> Seq.exists Char.IsControl then invalidOp "Empty/control-containing path"
        let root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
        let path = Path.GetFullPath(Path.Combine(root,relative))
        if path <> root && not (path.StartsWith(root + string Path.DirectorySeparatorChar,StringComparison.Ordinal)) then invalidOp "Path escapes its declared workspace"
        let mutable current = root
        if not (isNull (DirectoryInfo(root).LinkTarget)) then invalidOp "Workspace root must not be a symlink"
        for part in Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar) do
            if part <> "." then
                current <- Path.Combine(current,part)
                let info: FileSystemInfo = if Directory.Exists current then upcast DirectoryInfo(current) else upcast FileInfo(current)
                if not (isNull info.LinkTarget) then invalidOp "Symlinked paths are not admitted"
        path
    let createPrivateDirectory (directory: string) =
        let full = Path.GetFullPath directory
        let mode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
        if not (Directory.Exists full) then
            if OperatingSystem.IsWindows() then
                Directory.CreateDirectory full |> ignore
            else
                Directory.CreateDirectory(full, mode) |> ignore
        if not (OperatingSystem.IsWindows()) then
            try
                File.SetUnixFileMode(full, mode)
            with
            | :? IOException | :? UnauthorizedAccessException -> ()

    let private openPrivateFileStream (path: string) (mode: FileMode) (access: FileAccess) (share: FileShare) =
        if OperatingSystem.IsWindows() then
            new FileStream(path, mode, access, share)
        else
            let fileMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
            let opts = FileStreamOptions(Mode = mode, Access = access, Share = share, UnixCreateMode = fileMode)
            let stream = new FileStream(path, opts)
            try File.SetUnixFileMode(path, fileMode) with _ -> ()
            stream

    let private syncDirectory (directory: string) =
        if not (OperatingSystem.IsWindows()) then
            try
                use handle = File.OpenHandle(directory, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                RandomAccess.FlushToDisk handle
            with
            | :? IOException | :? UnauthorizedAccessException -> ()

    let atomic (path: string) (text: string) =
        let fullPath = Path.GetFullPath path
        let directory = Path.GetDirectoryName fullPath
        createPrivateDirectory directory
        let temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            do
                use stream = openPrivateFileStream temporary FileMode.CreateNew FileAccess.ReadWrite FileShare.None
                let bytes = UTF8Encoding(false).GetBytes(text)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush(true)
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.Move(temporary, fullPath, true)
            syncDirectory directory
        finally
            if File.Exists temporary then File.Delete temporary

    let atomicOutput (path: string) (text: string) =
        let fullPath = Path.GetFullPath path
        let directory = Path.GetDirectoryName fullPath
        if not (Directory.Exists directory) then
            Directory.CreateDirectory directory |> ignore
        let temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            do
                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
                let bytes = UTF8Encoding(false).GetBytes(text)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush(true)
            File.Move(temporary, fullPath, true)
            syncDirectory directory
        finally
            if File.Exists temporary then File.Delete temporary

    let readBounded path =
        if FileInfo(path).Length > 8L*1024L*1024L then invalidOp "Stored document exceeds 8 MiB"
        File.ReadAllText path
    let private decode<'T> path = JsonSerializer.Deserialize<'T>(readBounded path,jsonOptions)
    let context state context =
        createPrivateDirectory state
        let directory = child state (name "context" context)
        createPrivateDirectory directory
        directory
    let lockFile (path: string) =
        let fullPath = Path.GetFullPath path
        createPrivateDirectory (Path.GetDirectoryName fullPath)
        openPrivateFileStream fullPath FileMode.OpenOrCreate FileAccess.ReadWrite FileShare.None
    let lockContext directory =
        lockFile (Path.Combine(directory,"execution.lock"))
    let contextIdentity directory =
        let path = Path.Combine(directory,"identity")
        if not (File.Exists path) then atomic path (Guid.NewGuid().ToString("D"))
        readBounded path
    let sourceFiles root inputs =
        let excluded = set ["bin";"obj";".git";".canonforge";"node_modules"]
        let rec enumerate path = seq {
            if File.Exists path then yield path
            elif Directory.Exists path then
                for entry in Directory.EnumerateFileSystemEntries path |> Seq.sort do
                    if not (excluded.Contains(Path.GetFileName entry)) then
                        let checkedPath = child root entry
                        yield! enumerate checkedPath
            else invalidOp ("Missing declared input: " + Path.GetRelativePath(root,path))
        }
        let paths = inputs |> Seq.collect (child root >> enumerate) |> Seq.distinct |> Seq.sort |> Seq.truncate 10001 |> Seq.toArray
        if paths.Length > 10000 then invalidOp "Input closure exceeds 10000 files"
        if paths |> Array.sumBy (fun p -> FileInfo(p).Length) > 256L*1024L*1024L then invalidOp "Input closure exceeds 256 MiB"
        paths |> Array.map (fun p -> { Path=Path.GetRelativePath(root,p).Replace('\\','/'); Digest=hashFile p })
    let storeArtifact directory source =
        let digest = hashFile source
        let path = Path.Combine(directory,"artifacts",digest.Substring(7))
        createPrivateDirectory (Path.GetDirectoryName path)
        if not (File.Exists path) then
            let temp = path + "." + Guid.NewGuid().ToString("N")
            try
                do
                    use srcStream = File.OpenRead source
                    use dstStream = openPrivateFileStream temp FileMode.CreateNew FileAccess.ReadWrite FileShare.None
                    srcStream.CopyTo dstStream
                    dstStream.Flush(true)
                if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temp, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                File.Move(temp,path,false)
                syncDirectory (Path.GetDirectoryName path)
            finally if File.Exists temp then File.Delete temp
        if hashFile path <> digest then invalidOp "Artifact store integrity mismatch"
        path,digest
    let saveRun directory (run: RunRecord) =
        let protectedRecord = {run with Checksum=""}
        let record = {protectedRecord with Checksum=ContentDigest.ofText(encode protectedRecord)}
        let json = encode record
        if Encoding.UTF8.GetByteCount json > 8*1024*1024 then invalidOp "Run metadata exceeds 8 MiB; use captured files for large outputs"
        atomic (Path.Combine(directory,"runs",name "run" run.RunId + ".json")) json
        record
    let loadRun directory id =
        let record = decode<RunRecord> (Path.Combine(directory,"runs",name "run" id + ".json"))
        if record.SchemaVersion <> 1 || ContentDigest.ofText(encode {record with Checksum=""}) <> record.Checksum then invalidOp "Run record integrity mismatch"
        record
    let runs directory =
        let path = Path.Combine(directory,"runs")
        if Directory.Exists path then Directory.GetFiles(path,"*.json") |> Array.sort |> Array.map (fun p -> loadRun directory (Path.GetFileNameWithoutExtension p))
        else [||]
    let history directory =
        let path = Path.Combine(directory,"history.ndjson")
        if not (File.Exists path) then [||]
        else
            if FileInfo(path).Length>64L*1024L*1024L then invalidOp "History exceeds 64 MiB local read budget"
            let lines = File.ReadAllLines path
            if lines.Length > 100000 then invalidOp "History exceeds local read budget"
            lines |> Array.map (fun line -> JsonSerializer.Deserialize<HistoryEvent>(line,jsonOptions))
    let verifyHistory directory =
        let events = history directory
        let mutable previous = "genesis"
        for index,event in Array.indexed events do
            if event.Sequence <> index+1 || event.Previous <> previous || ContentDigest.ofText(encode {event with Hash=""}) <> event.Hash then invalidOp "History chain is invalid"
            previous <- event.Hash
        events.Length,previous
    let append directory run stage status =
        let sequence,previous = verifyHistory directory
        let item = { Sequence=sequence+1; Previous=previous; RunId=run; Stage=stage; Status=status; At=now(); Hash="" }
        let item = {item with Hash=ContentDigest.ofText(encode item)}
        let historyPath = Path.Combine(directory,"history.ndjson")
        createPrivateDirectory directory
        let lineBytes = UTF8Encoding(false).GetBytes(encode item + "\n")
        do
            use stream = openPrivateFileStream historyPath FileMode.Append FileAccess.Write FileShare.Read
            stream.Write(lineBytes, 0, lineBytes.Length)
            stream.Flush(true)
        if not (OperatingSystem.IsWindows()) then
            try File.SetUnixFileMode(historyPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite) with _ -> ()
    let private key directory =
        let path = Path.Combine(directory,"secret.key")
        if not (File.Exists path) then atomic path (Convert.ToBase64String(RandomNumberGenerator.GetBytes 32))
        Convert.FromBase64String(readBounded path)
    let secretSet directory secretName material =
        let secretName = name "secret" secretName
        if String.IsNullOrEmpty material || Encoding.UTF8.GetByteCount material > 65536 then invalidOp "Secret must contain 1 to 65536 UTF-8 bytes"
        let nonce = RandomNumberGenerator.GetBytes 12
        let plain = Encoding.UTF8.GetBytes material
        let encrypted = Array.zeroCreate<byte> plain.Length
        let tag = Array.zeroCreate<byte> 16
        use aes = new AesGcm(key directory,16)
        aes.Encrypt(nonce,plain,encrypted,tag,Encoding.UTF8.GetBytes secretName)
        let entry = {Name=secretName;Version=Guid.NewGuid().ToString("N");Nonce=Convert.ToBase64String nonce;Tag=Convert.ToBase64String tag;Ciphertext=Convert.ToBase64String encrypted}
        atomic (Path.Combine(directory,"secrets",secretName + ".json")) (encode entry)
        entry.Version
    let secretEntry directory secretName = decode<SecretEntry>(Path.Combine(directory,"secrets",name "secret" secretName + ".json"))
    let secretValue directory secretName =
        let entry = secretEntry directory secretName
        let encrypted = Convert.FromBase64String entry.Ciphertext
        let plain = Array.zeroCreate<byte> encrypted.Length
        use aes = new AesGcm(key directory,16)
        aes.Decrypt(Convert.FromBase64String entry.Nonce,encrypted,Convert.FromBase64String entry.Tag,plain,Encoding.UTF8.GetBytes entry.Name)
        Encoding.UTF8.GetString plain
    let signingKey directory =
        let path = Path.Combine(directory,"signing.key")
        if not (File.Exists path) then
            use generated = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            atomic path (generated.ExportECPrivateKeyPem())
        let key = ECDsa.Create()
        key.ImportFromPem(readBounded path)
        key
    let verifyReleaseSignature (record: ReleaseRecord) trustedPublicKey =
        if record.SchemaVersion<>1 then invalidOp "Unsupported release schema"
        if record.PublicKey <> trustedPublicKey then invalidOp "Release signer is not the explicitly trusted key"
        use key = ECDsa.Create()
        key.ImportFromPem trustedPublicKey
        if not (key.VerifyData(Encoding.UTF8.GetBytes(encode {record with Signature=""}),Convert.FromBase64String record.Signature,HashAlgorithmName.SHA256)) then invalidOp "Release signature is invalid"
    let seal directory releaseName version plan run context artifacts evidenceDigest =
        let version = name "version" version
        use key = signingKey directory
        let payload = {SchemaVersion=1;Name=releaseName;Version=version;PlanDigest=plan;RunId=run;Context=context;Artifacts=artifacts;EvidenceDigest=evidenceDigest;PublicKey=key.ExportSubjectPublicKeyInfoPem();Signature=""}
        let bytes = Encoding.UTF8.GetBytes(encode payload)
        let release = {payload with Signature=Convert.ToBase64String(key.SignData(bytes,HashAlgorithmName.SHA256))}
        let path = Path.Combine(directory,"releases",version + ".json")
        if File.Exists path then
            let prior = decode<ReleaseRecord> path
            verifyReleaseSignature prior (key.ExportSubjectPublicKeyInfoPem())
            if prior.Name<>releaseName || prior.Context<>context || prior.Version<>version || prior.PlanDigest <> plan || prior.Artifacts <> artifacts then invalidOp "Released versions are immutable; use a new version"
            prior,path
        else
            atomic path (encode release)
            release,path
    let verifyRelease path trustedPublicKey =
        let record = decode<ReleaseRecord> path
        verifyReleaseSignature record trustedPublicKey
        for artifact in record.Artifacts do
            if not (File.Exists artifact.Path) || hashFile artifact.Path <> artifact.Digest then invalidOp "Release artifact is missing or changed"
        record
    let promote directory version channel trustedPublicKey =
        let channel = name "channel" channel
        let path = Path.Combine(directory,"releases",name "version" version+".json")
        let release = verifyRelease path trustedPublicKey
        let target = Path.Combine(directory,"channels",channel+".json")
        let previous = if File.Exists target then readBounded target else ""
        atomic target (encode {|schemaVersion=1;channel=channel;version=release.Version;releaseDigest=hashFile path;previousDigest=ContentDigest.ofText previous;at=now()|})
        append directory release.RunId "" ("release-pointer:"+channel+":"+version)
        target
