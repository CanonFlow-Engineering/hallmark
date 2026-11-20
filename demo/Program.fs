open Hallmark.Lab
open Hallmark.Lab.Serialization

[<EntryPoint>]
let main _ =
    match Plan.validate Example.sixStagePlan with
    | Ok plan ->
        printfn "%s" (Plan.previewJson plan)
        0
    | Error errors ->
        eprintfn "%A" errors
        1
