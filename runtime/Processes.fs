namespace Hallmark.Runtime

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Threading
open System.Threading.Tasks

type ProcessResult = { ExitCode: int; Status: string; Stdout: string; Stderr: string; DurationMs: int64 }

module Processes =
    let inheritedEnvironment () =
        ["PATH";"HOME";"TMPDIR";"LANG";"LC_ALL";"SystemRoot";"ComSpec";"DOCKER_HOST";"DOCKER_CONTEXT";"DOCKER_CONFIG";"SSL_CERT_FILE";"SSL_CERT_DIR"]
        |> List.choose (fun key -> Environment.GetEnvironmentVariable key |> Option.ofObj |> Option.map (fun value -> key,value))
    let executable (value: string) =
        let paths =
            if Path.IsPathRooted value then [value]
            else (Environment.GetEnvironmentVariable("PATH") |> Option.ofObj |> Option.defaultValue "").Split(Path.PathSeparator) |> Array.map (fun directory -> Path.Combine(directory,value)) |> Array.toList
        paths |> List.tryFind File.Exists |> Option.defaultWith (fun () -> invalidOp ("Executable not found: " + value)) |> Path.GetFullPath
    let redact (secrets: string list) text =
        secrets |> List.filter (String.IsNullOrEmpty >> not) |> List.sortByDescending _.Length |> List.fold (fun (output: string) secret -> output.Replace(secret,"[REDACTED]",StringComparison.Ordinal)) text
    let run (executable: string) (arguments: string list) (directory: string) (environment: (string * string) list) (stdin: string option) (timeoutSeconds: int) (maxBytes: int) secrets (cancellation: CancellationToken) = task {
        let started = Stopwatch.StartNew()
        use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellation
        deadline.CancelAfter(TimeSpan.FromSeconds(float timeoutSeconds))
        let start = ProcessStartInfo(FileName=executable,WorkingDirectory=directory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true)
        start.Environment.Clear()
        for key,value in inheritedEnvironment() do start.Environment[key] <- value
        for argument in arguments do start.ArgumentList.Add argument
        for key,value in environment do start.Environment[key] <- value
        use childProcess = new Process(StartInfo=start)
        let mutable total = 0
        let mutable exceeded = false
        let capture (reader: StreamReader) = task {
            let text = StringBuilder()
            let buffer = Array.zeroCreate<char> 1024
            let mutable reading = true
            try
                while reading do
                    let! count = reader.ReadAsync(buffer.AsMemory(),deadline.Token)
                    if count = 0 then reading <- false
                    elif Interlocked.Add(&total,Encoding.UTF8.GetByteCount(buffer,0,count)) > maxBytes then
                        exceeded <- true
                        deadline.Cancel()
                        reading <- false
                    else text.Append(buffer,0,count) |> ignore
            with :? OperationCanceledException -> ()
            return text.ToString()
        }
        if not (childProcess.Start()) then invalidOp "Process did not start"
        let stdout = capture childProcess.StandardOutput
        let stderr = capture childProcess.StandardError
        let input = task {
            try
                match stdin with
                | Some (text: string) -> do! childProcess.StandardInput.WriteAsync(text.AsMemory(),deadline.Token)
                | None -> ()
                childProcess.StandardInput.Close()
            with
            | :? IOException | :? OperationCanceledException -> ()
        }
        let mutable interrupted = false
        try do! childProcess.WaitForExitAsync(deadline.Token)
        with :? OperationCanceledException ->
            interrupted <- true
            if not childProcess.HasExited then
                try childProcess.Kill(true) with :? InvalidOperationException -> ()
            do! childProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        do! input
        let! output = stdout.WaitAsync(TimeSpan.FromSeconds 10.)
        let! error = stderr.WaitAsync(TimeSpan.FromSeconds 10.)
        let status = if exceeded then "output-limit" elif interrupted && cancellation.IsCancellationRequested then "cancelled" elif interrupted then "timeout" else "completed"
        return {
            ExitCode = childProcess.ExitCode; Status = status
            Stdout = if exceeded then "[output discarded: limit exceeded]" else redact secrets output
            Stderr = if exceeded then "" else redact secrets error
            DurationMs = started.ElapsedMilliseconds
        }
    }
