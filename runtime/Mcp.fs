namespace CanonForge.Runtime

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open CanonForge.Lab
open CanonForge.Lab.Serialization

module Mcp =
    let private parseAssayKind (name: string) =
        match name.Trim() with
        | "FsAssay" -> FsAssay
        | "PostgresAssay" -> PostgresAssay
        | "OpenSearchAssay" -> OpenSearchAssay
        | "DuckDbAssay" -> DuckDbAssay
        | "OracleAssay" -> OracleAssay
        | "RustAssay" -> RustAssay
        | "PythonAssay" -> PythonAssay
        | "CSharpAssay" -> CSharpAssay
        | other -> invalidOp ("Unknown AssayKind: " + other)

    let private parseGateOutcome (name: string) =
        match name.Trim() with
        | "Pass" -> Pass
        | "Inconclusive" -> Inconclusive
        | "Fail" -> Fail
        | "ToolFailure" -> ToolFailure
        | other -> invalidOp ("Unknown GateOutcome: " + other)

    let private parseLineage (value: string) =
        let trimmed = value.Trim()
        if trimmed = "" || trimmed = "Active" then Active
        elif trimmed.StartsWith("Superseded:", StringComparison.Ordinal) then Superseded (trimmed.Substring(11))
        elif trimmed.StartsWith("Withdrawn:", StringComparison.Ordinal) then Withdrawn (trimmed.Substring(10))
        else invalidOp ("Unknown lineage status: " + trimmed)

    let private outcomeToString = function
        | Pass -> "Pass"
        | Inconclusive -> "Inconclusive"
        | Fail -> "Fail"
        | ToolFailure -> "ToolFailure"

    let private rejectionToDto = function
        | MissingRequiredAssays kinds ->
            {| code = "GATE_B_MISSING_ASSAYS"; detail = sprintf "Missing required assays: %A" kinds |}
        | InvalidReceiptDigest kind ->
            {| code = "GATE_B_INVALID_DIGEST"; detail = sprintf "Invalid receipt digest for %A" kind |}
        | MismatchedReceiptIdentity (kind, field) ->
            {| code = "GATE_B_BINDING_MISMATCH"; detail = sprintf "Mismatched %s binding on %A receipt" field kind |}
        | NonPassingOutcome outcome ->
            {| code = "GATE_B_NON_PASSING"; detail = "Aggregate outcome is " + outcomeToString outcome |}
        | ReleaseSuperseded byId ->
            {| code = "VERA_RELEASE_SUPERSEDED"; detail = "Superseded by " + byId |}
        | ReleaseWithdrawn reason ->
            {| code = "VERA_RELEASE_WITHDRAWN"; detail = "Withdrawn: " + reason |}

    let private toolDefinitionsJson = """[
  {
    "name": "hallmark_capabilities",
    "description": "Return Hallmark Lifecycle Engine stages, operations, commands, and platform capabilities.",
    "inputSchema": {
      "type": "object",
      "properties": {},
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_plan",
    "description": "Validate a workflow manifest and return its frozen planDigest, sourceDigest, policyDigest, targetDigest, and ordered stage IDs.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "manifest": { "type": "string" },
        "stateDir": { "type": "string" }
      },
      "required": ["manifest"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_status",
    "description": "Read checksummed run records from the run journal for a context.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "context": { "type": "string" },
        "stateDir": { "type": "string" }
      },
      "required": ["context"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_gate_b_evaluate",
    "description": "Evaluate the Gate B four-state dominance semilattice (ToolFailure > Fail > Inconclusive > Pass) and optional release identity bindings.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "requiredAssays": { "type": "array", "items": { "type": "string" } },
        "lineage": { "type": "string" },
        "expectedBinding": {
          "type": "object",
          "properties": {
            "planDigest": { "type": "string" },
            "sourceDigest": { "type": "string" },
            "policyDigest": { "type": "string" },
            "targetDigest": { "type": "string" }
          },
          "required": ["planDigest", "sourceDigest", "policyDigest", "targetDigest"]
        },
        "receipts": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "assay": { "type": "string" },
              "outcome": { "type": "string" },
              "receiptDigest": { "type": "string" },
              "planDigest": { "type": "string" },
              "sourceDigest": { "type": "string" },
              "policyDigest": { "type": "string" },
              "targetDigest": { "type": "string" }
            },
            "required": ["assay", "outcome", "receiptDigest"]
          }
        }
      },
      "required": ["requiredAssays", "receipts"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_bundle_verify",
    "description": "Verify a signed release bundle archive against an explicit trusted public key PEM.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "bundlePath": { "type": "string" },
        "publicKeyPath": { "type": "string" }
      },
      "required": ["bundlePath", "publicKeyPath"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_apply",
    "description": "Apply a reviewed workflow plan (requires --allow-effects on the MCP server).",
    "inputSchema": {
      "type": "object",
      "properties": {
        "manifest": { "type": "string" },
        "planDigest": { "type": "string" },
        "stateDir": { "type": "string" }
      },
      "required": ["manifest", "planDigest"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_resume",
    "description": "Resume an interrupted or failed workflow run by runId (requires --allow-effects on the MCP server).",
    "inputSchema": {
      "type": "object",
      "properties": {
        "manifest": { "type": "string" },
        "runId": { "type": "string" },
        "stateDir": { "type": "string" }
      },
      "required": ["manifest", "runId"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_three_owner_evaluate",
    "description": "Evaluate a multi-owner release fixture (affected package graph, deduplicated dependency paths, VERA evidence corrections/withdrawals, release notes, and four role views).",
    "inputSchema": {
      "type": "object",
      "properties": {
        "fixturePath": { "type": "string" }
      },
      "required": ["fixturePath"],
      "additionalProperties": false
    }
  },
  {
    "name": "hallmark_triage",
    "description": "Produce an operator incident triage report identifying committed imperative effects, the blocking stage/owner, and the safe next recovery action.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "context": { "type": "string" },
        "stateDir": { "type": "string" },
        "fixturePath": { "type": "string" }
      },
      "required": ["context"],
      "additionalProperties": false
    }
  }
]"""

    let private tryGetString (prop: string) (elem: JsonElement) =
        match elem.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString())
        | _ -> None

    let private requireString (prop: string) (elem: JsonElement) =
        tryGetString prop elem |> Option.defaultWith (fun () -> invalidOp ("Missing required string parameter: " + prop))

    let evaluateThreeOwnerFixtureFile (fixturePath: string) : ThreeOwnerCutoverVerdict * string =
        use doc = JsonDocument.Parse(Storage.readBounded fixturePath)
        let root = doc.RootElement
        let releaseVersion = requireString "releaseVersion" root
        let packages =
            root.GetProperty("packages").EnumerateArray()
            |> Seq.map (fun p -> {
                PackageId = requireString "packageId" p
                ComponentName = requireString "componentName" p
                OwnerTeam = requireString "ownerTeam" p
                CurrentVersion = requireString "currentVersion" p
                CandidateVersion = requireString "candidateVersion" p
                SourceDigest = requireString "sourceDigest" p
                DependsOn =
                    match p.TryGetProperty "dependsOn" with
                    | true, arr when arr.ValueKind = JsonValueKind.Array ->
                        arr.EnumerateArray() |> Seq.map (fun x -> x.GetString()) |> Seq.toList
                    | _ -> []
            })
            |> Seq.toList
        let changeSets =
            root.GetProperty("changeSets").EnumerateArray()
            |> Seq.map (fun c ->
                let kindStr = requireString "classification" c
                let migVer = tryGetString "migrationVersion" c |> Option.defaultValue "V2"
                let classification =
                    match kindStr with
                    | "BackwardCompatible" -> BackwardCompatible
                    | "AdditiveMigration" -> AdditiveMigration migVer
                    | "BreakingContract" -> BreakingContract migVer
                    | _ -> Unclassified
                {
                    ChangeSetId = requireString "changeSetId" c
                    PackageId = requireString "packageId" c
                    Classification = classification
                    Summary = requireString "summary" c
                })
            |> Seq.toList
        let receipts =
            root.GetProperty("receipts").EnumerateArray()
            |> Seq.map (fun r -> {
                PackageId = requireString "packageId" r
                OwnerTeam = requireString "ownerTeam" r
                Assay = parseAssayKind (requireString "assay" r)
                Outcome = parseGateOutcome (requireString "outcome" r)
                ReceiptDigest = requireString "receiptDigest" r
                EvaluatedAgainstSourceDigest = requireString "sourceDigest" r
            })
            |> Seq.toList
        let corrections =
            match root.TryGetProperty "corrections" with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                arr.EnumerateArray()
                |> Seq.map (fun c ->
                    let kind =
                        match requireString "kind" c with
                        | "Superseded" -> ReceiptSuperseded (requireString "replacementReceiptDigest" c)
                        | _ -> ReceiptWithdrawn
                    {
                        CorrectionId = requireString "correctionId" c
                        AffectedReceiptDigest = requireString "affectedReceiptDigest" c
                        Actor = requireString "actor" c
                        Reason = requireString "reason" c
                        EffectiveAt = requireString "effectiveAt" c
                        Kind = kind
                    })
                |> Seq.toList
            | _ -> []

        let verdict = Admission.evaluateThreeOwnerCutover releaseVersion packages changeSets receipts corrections
        let handoffDtos =
            verdict.Handoffs
            |> List.map (fun h ->
                let statusKind, statusDetail =
                    match h.Status with
                    | CompatibilitySatisfied digest -> "satisfied", digest
                    | CompatibilityPending reason -> "pending", reason
                    | CompatibilityCorrectionRequired (cid, reason) -> "correction-required", (cid + ": " + reason)
                {|
                    originatingPackageId = h.OriginatingPackageId
                    originatingVersion = h.OriginatingVersion
                    consumingPackageId = h.ConsumingPackageId
                    consumingVersion = h.ConsumingVersion
                    responsibleOwner = h.ResponsibleOwner
                    dependencyPaths = h.DependencyPaths |> List.toArray
                    owedAction = h.OwedAction
                    status = statusKind
                    statusDetail = statusDetail
                    upstreamNote = h.UpstreamNote
                |})
            |> List.toArray
        let json =
            Storage.encode {|
                schemaVersion = 1
                releaseVersion = verdict.ReleaseVersion
                admitted = verdict.Admitted
                affectedPackages = verdict.AffectedPackages |> List.toArray
                excludedUnrelatedPackages = verdict.ExcludedUnrelatedPackages |> List.toArray
                handoffs = handoffDtos
                blockers = verdict.Blockers |> List.toArray
                releaseNotes = verdict.ReleaseNotes |> List.toArray
                roleViews = {|
                    developer = verdict.RoleViews.DeveloperView |> List.toArray
                    qa = verdict.RoleViews.QaView |> List.toArray
                    operator = verdict.RoleViews.OperatorView |> List.toArray
                    product = verdict.RoleViews.ProductView |> List.toArray
                |}
            |}
        verdict, json

    let evaluateOperatorTriage (stateDir: string) (contextName: string) (fixturePath: string option) : string =
        let dir = Storage.context stateDir contextName
        let latestRun =
            Storage.runs dir |> Array.sortBy (fun run -> run.Started, run.RunId) |> Array.tryLast
        let committedEffects, ledgerConflicts =
            let ledgerDir = Path.Combine(dir, "ledger")
            if Directory.Exists ledgerDir then
                let files =
                    Directory.GetFiles(ledgerDir, "*.json")
                    |> Array.filter (fun p -> not (p.EndsWith(".target.json", StringComparison.Ordinal)))
                    |> Array.sort
                let committed =
                    files
                    |> Array.choose (fun p ->
                        let stepId = Path.GetFileNameWithoutExtension p
                        use doc = JsonDocument.Parse(Storage.readBounded p)
                        let root = doc.RootElement
                        match tryGetString "status" root with
                        | Some "committed" ->
                            Some {|
                                stepId = stepId
                                status = "committed"
                                version = tryGetString "version" root |> Option.defaultValue ""
                                script = tryGetString "script" root |> Option.defaultValue ""
                                checksum = tryGetString "checksum" root |> Option.defaultValue ""
                            |}
                        | _ -> None)
                let conflicts =
                    files
                    |> Array.choose (fun p ->
                        let stepId = Path.GetFileNameWithoutExtension p
                        use doc = JsonDocument.Parse(Storage.readBounded p)
                        let root = doc.RootElement
                        match tryGetString "status" root with
                        | Some "conflict" ->
                            let reason = tryGetString "reason" root |> Option.defaultValue "conflicting target ledger state"
                            let recordedRunId =
                                match tryGetString "runId" root with
                                | Some r when not (String.IsNullOrWhiteSpace r) -> r
                                | _ -> latestRun |> Option.map _.RunId |> Option.defaultValue ""
                            let sysId = tryGetString "systemIdentifier" root |> Option.defaultValue ""
                            let dbOid =
                                match root.TryGetProperty "databaseOid" with
                                | true, v when v.ValueKind = JsonValueKind.Number -> v.GetInt64()
                                | true, v when v.ValueKind = JsonValueKind.String ->
                                    match Int64.TryParse(v.GetString()) with true, n -> n | _ -> 0L
                                | _ -> 0L
                            let ver = tryGetString "version" root |> Option.defaultValue ""
                            let scr = tryGetString "script" root |> Option.defaultValue ""
                            Some {|
                                runId = recordedRunId
                                stepId = stepId
                                status = "conflict"
                                exitCode = 3
                                systemIdentifier = sysId
                                databaseOid = dbOid
                                version = ver
                                script = scr
                                reason = reason
                                message = "External ledger conflict: " + reason
                            |}
                        | _ -> None)
                committed, conflicts
            else [||], [||]

        let runId, runStatus, failedSteps =
            match latestRun with
            | None -> "", "no-runs-recorded", ledgerConflicts
            | Some r ->
                let conflictIds = ledgerConflicts |> Array.map _.stepId |> Set.ofArray
                let nonSucceeded =
                    r.Steps
                    |> Array.filter (fun s -> s.Status <> "succeeded" && not (Set.contains s.Id conflictIds))
                    |> Array.map (fun s -> {|
                        runId = r.RunId
                        stepId = s.Id
                        status = s.Status
                        exitCode = s.ExitCode
                        systemIdentifier = ""
                        databaseOid = 0L
                        version = ""
                        script = ""
                        reason = s.Message
                        message = s.Message
                    |})
                r.RunId, r.Status, Array.append ledgerConflicts nonSucceeded

        let ownerBlockers, responsibleOwners =
            match fixturePath with
            | Some path when File.Exists path ->
                let verdict, _ = evaluateThreeOwnerFixtureFile path
                let owners =
                    verdict.Handoffs
                    |> List.choose (fun h ->
                        match h.Status with
                        | CompatibilitySatisfied _ -> None
                        | _ -> Some h.ResponsibleOwner)
                    |> List.distinct
                    |> List.toArray
                verdict.Blockers |> List.toArray, owners
            | _ -> [||], [||]

        let safeNextAction =
            if ledgerConflicts.Length > 0 then
                let conflictIds = ledgerConflicts |> Array.map _.stepId |> String.concat ", "
                "External ledger conflict detected for [" + conflictIds + "]; do not promote or resume until the target ledger conflict is reconciled."
            elif runId = "" then
                "No active or failed run found; run `hallmark engine plan <manifest>` to preview a new delivery."
            elif runStatus = "succeeded" && ownerBlockers.Length = 0 && failedSteps.Length = 0 then
                "Run " + runId + " succeeded. Verify the signed release bundle and review the applicable owner decisions before promotion."
            else
                let committedNote =
                    if committedEffects.Length > 0 then
                        let ids = committedEffects |> Array.map (fun e -> sprintf "%s (V%s %s)" e.stepId e.version e.script) |> String.concat ", "
                        "Committed effect(s) [" + ids + "] are recorded in the target ledger and will be reused without re-dispatch. "
                    else ""
                let ownerNote =
                    if responsibleOwners.Length > 0 then
                        "Resolve blocking compatibility evidence with owner(s) [" + String.Join(", ", responsibleOwners) + "], then run: "
                    else "Reconcile the blocking gate or target ledger, then run: "
                committedNote + ownerNote + sprintf "hallmark engine resume <manifest> --run %s --state-dir %s" runId stateDir

        Storage.encode {|
            schemaVersion = 1
            context = contextName
            runId = runId
            runStatus = runStatus
            committedEffects = committedEffects
            unresolvedSteps = failedSteps
            responsibleBlockerOwners = responsibleOwners
            ownerBlockers = ownerBlockers
            safeNextAction = safeNextAction
        |}

    let private callTool (defaultStateDir: string) (allowEffects: bool) (toolName: string) (args: JsonElement) =
        let resolveState () =
            tryGetString "stateDir" args
            |> Option.defaultValue defaultStateDir
            |> Path.GetFullPath

        match toolName with
        | "hallmark_capabilities" ->
            let payload =
                Storage.encode {|
                    schemaVersion = 1
                    version = "0.2.0-local"
                    stages = [|"Build";"Install";"Configure";"Migrate";"Verify";"Release"|]
                    operations = [|"process";"write-file";"capture";"compose-up";"postgres-migrate";"http";"release"|]
                    commands = [|"capabilities";"schema";"doctor";"plan";"freeze";"emit-json";"mcp";"three-owner-check";"triage";"apply";"resume";"converge";"status";"history";"drift";"secret-set";"secret-list";"public-key";"release-verify";"bundle";"bundle-verify";"promote";"channels";"export";"destroy"|]
                    execution = "trusted-local"
                    qualifiedPlatform = "linux-x86_64"
                    ruleAuthority = "external-producer-contract"
                    mcpEffectsEnabled = allowEffects
                |}
            false, payload

        | "hallmark_plan" ->
            let manifest = requireString "manifest" args
            let state = resolveState ()
            let prepared = Runner.prepare manifest state
            let policyDigest = prepared.PolicyDigest
            let targetDigest = prepared.TargetDigest
            let payload =
                Storage.encode {|
                    schemaVersion = 1
                    evidenceStage = "Preview"
                    planDigest = prepared.Digest
                    sourceDigest = prepared.SourceDigest
                    policyDigest = policyDigest
                    targetDigest = targetDigest
                    stages = Plan.orderedStageIds prepared.Plan |> List.toArray
                |}
            false, payload

        | "hallmark_status" ->
            let ctxName = requireString "context" args
            let state = resolveState ()
            let dir = Storage.context state ctxName
            let runs = Storage.runs dir
            false, Storage.encode runs

        | "hallmark_gate_b_evaluate" ->
            let requiredAssays =
                match args.TryGetProperty "requiredAssays" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray() |> Seq.map (fun e -> parseAssayKind (e.GetString())) |> Seq.toList
                | _ -> invalidOp "Missing requiredAssays array"
            let lineage =
                tryGetString "lineage" args |> Option.defaultValue "Active" |> parseLineage
            let receiptsElem =
                match args.TryGetProperty "receipts" with
                | true, arr when arr.ValueKind = JsonValueKind.Array -> arr
                | _ -> invalidOp "Missing receipts array"

            let verdict =
                match args.TryGetProperty "expectedBinding" with
                | true, bindElem when bindElem.ValueKind = JsonValueKind.Object ->
                    let expected = {
                        PlanDigest = requireString "planDigest" bindElem
                        SourceDigest = requireString "sourceDigest" bindElem
                        PolicyDigest = requireString "policyDigest" bindElem
                        TargetDigest = requireString "targetDigest" bindElem
                    }
                    let boundReceipts =
                        receiptsElem.EnumerateArray()
                        |> Seq.map (fun r -> {
                            Assay = parseAssayKind (requireString "assay" r)
                            Outcome = parseGateOutcome (requireString "outcome" r)
                            ReceiptDigest = requireString "receiptDigest" r
                            PlanDigest = tryGetString "planDigest" r |> Option.defaultValue ""
                            SourceDigest = tryGetString "sourceDigest" r |> Option.defaultValue ""
                            PolicyDigest = tryGetString "policyDigest" r |> Option.defaultValue ""
                            TargetDigest = tryGetString "targetDigest" r |> Option.defaultValue ""
                        })
                        |> Seq.toList
                    Admission.evaluateBoundReleaseGate expected requiredAssays boundReceipts lineage
                | _ ->
                    let plainReceipts =
                        receiptsElem.EnumerateArray()
                        |> Seq.map (fun r -> {
                            Assay = parseAssayKind (requireString "assay" r)
                            Outcome = parseGateOutcome (requireString "outcome" r)
                            ReceiptDigest = requireString "receiptDigest" r
                        })
                        |> Seq.toList
                    Admission.evaluateGateB requiredAssays plainReceipts lineage

            let payload =
                Storage.encode {|
                    admitted = verdict.Admitted
                    aggregateOutcome = outcomeToString verdict.AggregateOutcome
                    rejections = verdict.Rejections |> List.map rejectionToDto |> List.toArray
                |}
            false, payload

        | "hallmark_bundle_verify" ->
            let bundlePath = requireString "bundlePath" args
            let publicKeyPath = requireString "publicKeyPath" args
            let release = Bundles.verify bundlePath (File.ReadAllText(publicKeyPath).Trim())
            let payload =
                Storage.encode {|
                    schemaVersion = 1
                    valid = true
                    name = release.Name
                    version = release.Version
                    planDigest = release.PlanDigest
                    runId = release.RunId
                    artifactCount = release.Artifacts.Length
                    authenticatedScope = [|"release";"attested-steps";"artifact-bytes"|]
                    supplementalScope = [|"run";"history"|]
                |}
            false, payload

        | "hallmark_apply" ->
            if not allowEffects then
                true, Storage.encode {| code = "MCP_EFFECTS_DISABLED"; error = "Protected execution effects are disabled on this MCP server; pass --allow-effects to enable local apply/resume." |}
            else
                let manifest = requireString "manifest" args
                let expectedDigest = requireString "planDigest" args
                let state = resolveState ()
                let prepared = Runner.prepare manifest state
                if prepared.Digest <> expectedDigest then
                    true, Storage.encode {| code = "PLAN_DIGEST_MISMATCH"; error = "Reviewed plan digest does not match the resolved plan" |}
                else
                    let record = Runner.apply prepared None CancellationToken.None |> _.GetAwaiter().GetResult()
                    (record.Status <> "succeeded"), Storage.encode record

        | "hallmark_resume" ->
            if not allowEffects then
                true, Storage.encode {| code = "MCP_EFFECTS_DISABLED"; error = "Protected execution effects are disabled on this MCP server; pass --allow-effects to enable local apply/resume." |}
            else
                let manifest = requireString "manifest" args
                let runId = requireString "runId" args
                let state = resolveState ()
                let prepared = Runner.prepare manifest state
                let record = Runner.apply prepared (Some runId) CancellationToken.None |> _.GetAwaiter().GetResult()
                (record.Status <> "succeeded"), Storage.encode record

        | "hallmark_three_owner_evaluate" ->
            let fixturePath = requireString "fixturePath" args
            let _, json = evaluateThreeOwnerFixtureFile fixturePath
            false, json

        | "hallmark_triage" ->
            let ctxName = requireString "context" args
            let state = resolveState ()
            let fixtureOpt = tryGetString "fixturePath" args
            false, evaluateOperatorTriage state ctxName fixtureOpt

        | other ->
            invalidArg "name" ("Unknown MCP tool: " + other)

    let handleJsonRpcLine (defaultStateDir: string) (allowEffects: bool) (line: string) : string option =
        if String.IsNullOrWhiteSpace line then None
        else
            use doc = JsonDocument.Parse(line)
            let root = doc.RootElement
            let hasId, idElem = root.TryGetProperty "id"
            let methodName =
                match root.TryGetProperty "method" with
                | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
                | _ -> ""

            if not hasId || methodName.StartsWith("notifications/", StringComparison.Ordinal) then
                None
            else
                let replyResult (resultJson: string) =
                    use resDoc = JsonDocument.Parse(resultJson)
                    Storage.encode {|
                        jsonrpc = "2.0"
                        id = idElem.Clone()
                        result = resDoc.RootElement.Clone()
                    |}

                let replyError (code: int) (message: string) =
                    Storage.encode {|
                        jsonrpc = "2.0"
                        id = idElem.Clone()
                        error = {| code = code; message = message |}
                    |}

                try
                    match methodName with
                    | "initialize" ->
                        let requestedVersion =
                            match root.TryGetProperty "params" with
                            | true, p -> tryGetString "protocolVersion" p |> Option.defaultValue "2025-11-25"
                            | _ -> "2025-11-25"
                        let initJson =
                            Storage.encode {|
                                protocolVersion = requestedVersion
                                capabilities = {| tools = {| listChanged = false |} |}
                                serverInfo = {| name = "hallmark-mcp"; version = "0.2.0-local" |}
                            |}
                        Some (replyResult initJson)

                    | "ping" ->
                        Some (replyResult "{}")

                    | "tools/list" ->
                        use toolsDoc = JsonDocument.Parse(toolDefinitionsJson)
                        let listJson = Storage.encode {| tools = toolsDoc.RootElement.Clone() |}
                        Some (replyResult listJson)

                    | "tools/call" ->
                        let paramsElem =
                            match root.TryGetProperty "params" with
                            | true, p when p.ValueKind = JsonValueKind.Object -> p
                            | _ -> invalidArg "params" "Missing params object for tools/call"
                        let toolName = requireString "name" paramsElem
                        let argsElem =
                            match paramsElem.TryGetProperty "arguments" with
                            | true, a when a.ValueKind = JsonValueKind.Object -> a
                            | _ ->
                                use emptyDoc = JsonDocument.Parse("{}")
                                emptyDoc.RootElement.Clone()
                        let isErr, payloadText =
                            try callTool defaultStateDir allowEffects toolName argsElem
                            with
                            | :? ArgumentException as ex -> raise ex
                            | ex ->
                                true, Storage.encode {| code = "EXECUTION_REFUSED"; error = ex.Message |}
                        use structuredDoc = JsonDocument.Parse(payloadText)
                        let callResult =
                            Storage.encode {|
                                content = [| {| ``type`` = "text"; text = payloadText |} |]
                                structuredContent = structuredDoc.RootElement.Clone()
                                isError = isErr
                            |}
                        Some (replyResult callResult)

                    | other ->
                        Some (replyError -32601 ("Method not found: " + other))
                with
                | :? ArgumentException as ex ->
                    Some (replyError -32602 ex.Message)
                | ex ->
                    Some (replyError -32603 ex.Message)

    let runStdio (defaultStateDir: string) (allowEffects: bool) (input: TextReader) (output: TextWriter) =
        let mutable line = input.ReadLine()
        while not (isNull line) do
            match handleJsonRpcLine defaultStateDir allowEffects line with
            | Some response ->
                output.WriteLine(response)
                output.Flush()
            | None -> ()
            line <- input.ReadLine()
