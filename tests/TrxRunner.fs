module CanonForge.Tests.TrxRunner

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open System.Xml.Linq
open Xunit

type private ObservedTest = {
    Id: string
    Name: string
    Passed: bool
    Message: string
}

let private deterministicGuid (name: string) =
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes name)
    Guid(hash[0..15]).ToString()

let private runAllFacts () =
    let assembly = Assembly.GetExecutingAssembly()
    let methods =
        assembly.GetTypes()
        |> Array.collect (fun t ->
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.Static)
            |> Array.filter (fun m -> not (isNull (m.GetCustomAttribute<FactAttribute>())))
            |> Array.map (fun m -> t, m))
        |> Array.sortBy (fun (t, m) -> t.FullName + "." + m.Name)

    methods
    |> Array.collect (fun (declaringType, methodInfo) ->
        let baseName = declaringType.FullName + "." + methodInfo.Name
        let inlineRows =
            methodInfo.GetCustomAttributes<InlineDataAttribute>()
            |> Seq.collect (fun attr -> attr.GetData methodInfo)
            |> Seq.toArray
        let invocations =
            if inlineRows.Length > 0 then
                inlineRows |> Array.mapi (fun idx args -> sprintf "%s[%d]" baseName idx, args)
            else
                [| baseName, [||] |]
        invocations
        |> Array.map (fun (fullName, args) ->
            let testId = deterministicGuid fullName
            try
                let instance =
                    if methodInfo.IsStatic then null
                    else Activator.CreateInstance declaringType
                let rawResult = methodInfo.Invoke(instance, args)
                match rawResult with
                | :? Task as t -> t.GetAwaiter().GetResult()
                | _ -> ()
                { Id = testId; Name = fullName; Passed = true; Message = "" }
            with ex ->
                let actual =
                    match ex with
                    | :? TargetInvocationException as tie when not (isNull tie.InnerException) -> tie.InnerException
                    | other -> other
                { Id = testId; Name = fullName; Passed = false; Message = actual.ToString() }))

let private writeTrx (path: string) (results: ObservedTest array) =
    let ns = XNamespace.Get "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
    let total = results.Length
    let passed = results |> Array.filter _.Passed |> Array.length
    let failed = total - passed
    let outcome = if failed = 0 then "Completed" else "Failed"

    let unitTestResults =
        results
        |> Array.map (fun r ->
            let elem =
                XElement(
                    ns + "UnitTestResult",
                    XAttribute(XName.Get "testId", r.Id),
                    XAttribute(XName.Get "testName", r.Name),
                    XAttribute(XName.Get "outcome", if r.Passed then "Passed" else "Failed")
                )
            if not r.Passed then
                elem.Add(XElement(ns + "Output", XElement(ns + "ErrorInfo", XElement(ns + "Message", r.Message))))
            elem :> obj)

    let doc =
        XDocument(
            XElement(
                ns + "TestRun",
                XAttribute(XName.Get "id", Guid.NewGuid().ToString()),
                XAttribute(XName.Get "name", "Hallmark.Tests"),
                XElement(
                    ns + "ResultSummary",
                    XAttribute(XName.Get "outcome", outcome),
                    XElement(
                        ns + "Counters",
                        XAttribute(XName.Get "total", string total),
                        XAttribute(XName.Get "executed", string total),
                        XAttribute(XName.Get "passed", string passed),
                        XAttribute(XName.Get "failed", string failed)
                    )
                ),
                XElement(ns + "Results", unitTestResults)
            )
        )
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
    doc.Save path

[<EntryPoint>]
let main argv =
    let rec parseArgs (args: string list) (resultsDir: string option) (trxFile: string option) =
        match args with
        | [] -> resultsDir, trxFile
        | "--results-directory" :: dir :: rest -> parseArgs rest (Some dir) trxFile
        | "--report-trx-filename" :: file :: rest -> parseArgs rest resultsDir (Some file)
        | _ :: rest -> parseArgs rest resultsDir trxFile

    let resultsDir, trxFile = parseArgs (Array.toList argv) None None
    let results = runAllFacts ()
    let passed = results |> Array.filter _.Passed |> Array.length
    let failed = results.Length - passed

    for r in results |> Array.filter (_.Passed >> not) do
        eprintfn "FAIL: %s\n%s" r.Name r.Message

    printfn "Executed %d tests: %d passed, %d failed." results.Length passed failed

    match resultsDir, trxFile with
    | Some dir, Some file -> writeTrx (Path.Combine(dir, file)) results
    | Some dir, None -> writeTrx (Path.Combine(dir, "results.trx")) results
    | None, Some file -> writeTrx file results
    | None, None -> ()

    if failed = 0 then 0 else 1
