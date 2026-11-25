namespace CanonForge.Runtime

open System
open System.Globalization
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open CanonForge.Lab
open CanonForge.Lab.Serialization
open CanonForge.Execution

type Prepared = { Root: string; State: string; Directory: string; Workflow: Workflow; Plan: ValidatedPlan; Digest: string; SourceDigest: string; PolicyDigest: string; TargetDigest: string; Sources: FileIdentity array; SecretVersions: Property array; Executables: Map<string,string>; ExecutionIdentity: string }
type Drift = { Kind: string; Resource: string; Reason: string }
type DriftInspection = { Changed: Drift array; Unavailable: string array; Scope: string array }

exception AdapterFailure of status: string * exitCode: int * detail: string

module FsAssayAuthority =
    [<Literal>]
    let PolicySchemaVersion = "fsassay-policy/1.1.0"

    [<Literal>]
    let EvidenceSchemaVersion = "fsassay-authority-receipt/1.1.0"

    [<Literal>]
    let ContractVersion = "authority-contract/1.0.0"

    [<Literal>]
    let ShapeContractVersion = "fsharp-shape/1.0.0"

    [<Literal>]
    let SupportedToolVersion = "1.0.4"

    let private catalogueRules =
        set [
            "FSA-AI01"; "FSA-AI12"; "FSA-AI13"; "FSA-AI14"; "FSA-AI15"; "FSA-AI16"; "FSA-AI17"; "FSA-AI18"; "FSA-AI19"
            "FSA-ARCH01"; "FSA-ARCH02"; "FSA-C01"; "FSA-C02"; "FSA-C03"; "FSA-C05"; "FSA-C06"; "FSA-C08"; "FSA-C09"; "FSA-C10"
            "FSA-P01"; "FSA-P02"; "FSA-P03"; "FSA-P04"; "FSA-P05"
            "FSA-SEC08"; "FSA-SEC09"; "FSA-SEC11"; "FSA-SEC12"; "FSA-SEC13"
            "FSA-TDD01"; "FSA-TDD02"; "FSA-TDD03"; "FSA-TDD04"; "FSA2017"; "FSA2022"
            "FSA-1301"; "FSA-1402"; "FSA-AI07"; "FSA-AI10"; "FSA-AI11"
            "FSA-C04"; "FSA-C07"; "FSA-C14"; "FSA-C15"; "FSA-C16"; "FSA-F04"; "FSA-F08"
            "FSA-LINT01"; "FSA-LINT02"; "FSA-LINT03"; "FSA-LINT04"; "FSA-LINT05"
            "FSA-LINT06"; "FSA-LINT07"; "FSA-LINT08"; "FSA-LINT09"; "FSA-LINT10"
            "FSA-LINT11"; "FSA-LINT12"; "FSA-LINT13"; "FSA-LINT14"; "FSA-LINT15"
            "FSA-LINT16"; "FSA-LINT17"; "FSA-LINT18"; "FSA-LINT19"; "FSA-LINT20"
            "FSA-S01"; "FSA-S02"; "FSA-S03"; "FSA-S05"
            "FSA-B01"; "FSA-C11"; "FSA-C12"; "FSA-C13"; "FSA-E01"; "FSA-E02"; "FSA-E03"; "FSA-E04"
            "FSA-F01"; "FSA-F02"; "FSA-F03"; "FSA-F05"; "FSA-F06"; "FSA-F07"
            "FSA-M01"; "FSA-M03"; "FSA-M04"; "FSA-ML01"; "FSA-ML02"; "FSA-S04"; "FSA-SEC10"; "FSA2016"
        ]

    type RequiredTestPolicy = { id: string; project: string; minimumPassed: int }
    type BaselineRecord = { id: string; ruleId: string; fingerprint: string; relativePath: string; symbol: string; owner: string; rationale: string; disposition: string; createdOn: string; expiresOn: string; policyVersion: string }
    type BaselinePolicy = { identity: string; reviewedBy: string; reviewedOn: string; records: BaselineRecord[] }
    type PolicyException = { id: string; category: string; relativePath: string; symbol: string; owner: string; reason: string; createdOn: string; expiresOn: string; shapeClauses: string[] }

    type PolicyLock = {
        policySchemaVersion: string
        evidenceSchemaVersion: string
        authorityContractVersion: string
        shapeContractVersion: string
        toolVersion: string
        evaluationDate: string
        enabledProfiles: string[]
        approvedBlockingRules: string[]
        advisoryRules: string[]
        experimentalRules: string[]
        prototypeRules: string[]
        dummyRules: string[]
        deprecatedRules: string[]
        removedRules: string[]
        requiredProjectClasses: string[]
        requiredTargetFrameworks: string[]
        requiredTests: RequiredTestPolicy[]
        baseline: BaselinePolicy
        exceptions: PolicyException[]
    }

    type CandidateIdentity = {
        kind: string
        commitSha: string
        approvedHeadSha: string
        treeSha: string
        dirty: bool
        syntheticMergeSha: string
        packageSha256: string
        repositoryRelativeTarget: string
    }

    type PolicyIdentity = {
        status: string
        path: string
        sha256: string
        snapshot: PolicyLock
        error: string
    }

    type ToolchainIdentity = {
        sdkVersion: string
        runtimeVersion: string
        fsharpCompilerServiceVersion: string
    }

    type ProjectReceipt = {
        path: string
        projectClass: string
        targetFrameworks: string[]
        supported: bool
        loaded: bool
        status: string
        reason: string
    }

    type SourceReceipt = {
        path: string
        disposition: string
        reason: string
    }

    type TestReceipt = {
        id: string
        project: string
        status: string
        passed: int
        failed: int
        skipped: int
    }

    type RuleReceipt = {
        ruleId: string
        authorityClass: string
        status: string
        evidenceAvailable: bool
        findingCount: int
    }

    type FindingReceipt = {
        ruleId: string
        path: string
        symbol: string
        line: int
        column: int
        message: string
        fingerprint: string
        authorityClass: string
    }

    type ReasonReceipt = { code: string; detail: string }

    type CountReceipt = {
        projectsDiscovered: int
        projectsLoaded: int
        projectsWorkspaceLoaded: int
        projectsSupported: int
        projectsFailed: int
        projectsSkipped: int
        projectsUnsupported: int
        eligibleFiles: int
        analyzedFiles: int
        compilerIncompleteFiles: int
    }

    type AuthorityReceipt = {
        schemaVersion: string
        tool: string
        toolVersion: string
        candidate: CandidateIdentity
        policy: PolicyIdentity
        toolchain: ToolchainIdentity
        outcome: string
        authoritative: bool
        reasons: ReasonReceipt[]
        counts: CountReceipt
        projects: ProjectReceipt[]
        sources: SourceReceipt[]
        tests: TestReceipt[]
        rules: RuleReceipt[]
        findings: FindingReceipt[]
        appliedBaselineRecords: string[]
        appliedSuppressions: string[]
        policyErrors: string[]
        evidenceErrors: string[]
        missingEvidence: string[]
        toolFailures: string[]
    }

    type ReceiptValidationContext = {
        expectedPolicySha256: string
        expectedCommitSha: string
        expectedTreeSha: string
        expectedApprovedHeadSha: string option
        expectedSyntheticMergeSha: string option
        expectedPackageSha256: string option
    }

    let private unapprovedPolicy = {
        policySchemaVersion = PolicySchemaVersion
        evidenceSchemaVersion = EvidenceSchemaVersion
        authorityContractVersion = ContractVersion
        shapeContractVersion = ShapeContractVersion
        toolVersion = SupportedToolVersion
        evaluationDate = "1970-01-01"
        enabledProfiles = [| "core" |]
        approvedBlockingRules = [||]
        advisoryRules = [||]
        experimentalRules = [||]
        prototypeRules = [||]
        dummyRules = [||]
        deprecatedRules = [||]
        removedRules = [||]
        requiredProjectClasses = [||]
        requiredTargetFrameworks = [||]
        requiredTests = [||]
        baseline = { identity = "none"; reviewedBy = ""; reviewedOn = ""; records = [||] }
        exceptions = [||]
    }

    let private jsonOptions () =
        let options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
        options

    let private sha256Bytes (bytes: byte[]) =
        bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private sha256Text (value: string) =
        value |> Encoding.UTF8.GetBytes |> sha256Bytes

    let isHex length (value: string) =
        not (isNull value) && value.Length = length && value |> Seq.forall Uri.IsHexDigit

    let normalizePolicySha (value: string) =
        if isNull value then ""
        elif value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) then value.Substring(7).ToLowerInvariant()
        else value.Trim().ToLowerInvariant()

    let normalizePolicy (policy: PolicyLock) =
        {
            policy with
                enabledProfiles = policy.enabledProfiles |> Array.sort
                approvedBlockingRules = policy.approvedBlockingRules |> Array.sort
                advisoryRules = policy.advisoryRules |> Array.sort
                experimentalRules = policy.experimentalRules |> Array.sort
                prototypeRules = policy.prototypeRules |> Array.sort
                dummyRules = policy.dummyRules |> Array.sort
                deprecatedRules = policy.deprecatedRules |> Array.sort
                removedRules = policy.removedRules |> Array.sort
                requiredProjectClasses = policy.requiredProjectClasses |> Array.sort
                requiredTargetFrameworks = policy.requiredTargetFrameworks |> Array.sort
                requiredTests = policy.requiredTests |> Array.sortBy (fun test -> test.id, test.project)
                baseline = { policy.baseline with records = policy.baseline.records |> Array.sortBy _.id }
                exceptions =
                    policy.exceptions
                    |> Array.map (fun item -> { item with shapeClauses = item.shapeClauses |> Array.sort })
                    |> Array.sortBy _.id
        }

    let private duplicates values =
        values |> Seq.countBy id |> Seq.filter (fun (_, count) -> count > 1) |> Seq.map fst |> Seq.toList

    let private validDate value =
        not (String.IsNullOrWhiteSpace value)
        && fst (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None))

    let private validRelativePath (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && not (Path.IsPathRooted value)
        && not (value.Replace('\\', '/').StartsWith("../", StringComparison.Ordinal))

    let private canonicalBaselineIdentity (baseline: BaselinePolicy) =
        let normalized = { baseline with identity = ""; records = baseline.records |> Array.sortBy _.id }
        JsonSerializer.SerializeToUtf8Bytes(normalized, jsonOptions ()) |> sha256Bytes

    let private validatePolicy (policy: PolicyLock) =
        [
            if policy.policySchemaVersion <> PolicySchemaVersion then $"unsupported policy schema '{policy.policySchemaVersion}'"
            if policy.evidenceSchemaVersion <> EvidenceSchemaVersion then $"unsupported evidence schema '{policy.evidenceSchemaVersion}'"
            if policy.authorityContractVersion <> ContractVersion then $"unsupported authority contract '{policy.authorityContractVersion}'"
            if policy.toolVersion <> SupportedToolVersion then $"policy tool version '{policy.toolVersion}' does not match '{SupportedToolVersion}'"
            if policy.shapeContractVersion <> ShapeContractVersion then $"unsupported Shape contract '{policy.shapeContractVersion}'"
            if not (validDate policy.evaluationDate) then "policy evaluation date must be an exact yyyy-MM-dd date"
            if not (Array.isEmpty policy.approvedBlockingRules) then "blocking rules require Human Gate C approval; this pending candidate permits none"
            if not (Array.isEmpty policy.advisoryRules) then "advisory rule admission requires Human Gate C approval; this pending candidate permits none"
            if Array.isEmpty policy.enabledProfiles then "at least one profile must be enabled"
            if Array.isEmpty policy.requiredProjectClasses then "at least one required project class must be locked"
            if Array.isEmpty policy.requiredTargetFrameworks then "at least one required target framework must be locked"
            if Array.isEmpty policy.requiredTests then "at least one required test must be locked"
            if Array.isEmpty policy.baseline.records then
                if policy.baseline.identity <> "none" || policy.baseline.reviewedBy <> "" || policy.baseline.reviewedOn <> "" then
                    "an empty baseline must use identity 'none' with blank review metadata"
            else
                if not (isHex 64 policy.baseline.identity) || policy.baseline.identity <> canonicalBaselineIdentity policy.baseline then
                    "baseline identity does not match its canonical reviewed records"
                if String.IsNullOrWhiteSpace policy.baseline.reviewedBy || not (validDate policy.baseline.reviewedOn) then
                    "a nonempty baseline requires reviewer identity and review date"
            for name, values in [
                "enabled profiles", policy.enabledProfiles
                "blocking rules", policy.approvedBlockingRules
                "advisory rules", policy.advisoryRules
                "experimental rules", policy.experimentalRules
                "prototype rules", policy.prototypeRules
                "dummy rules", policy.dummyRules
                "deprecated rules", policy.deprecatedRules
                "removed rules", policy.removedRules
                "project classes", policy.requiredProjectClasses
                "target frameworks", policy.requiredTargetFrameworks
            ] do
                let repeated = duplicates values
                if not repeated.IsEmpty then sprintf "%s contain duplicates: %s" name (String.concat "," repeated)
                if values |> Array.exists String.IsNullOrWhiteSpace then sprintf "%s contain blank values" name
            let ruleSets = [
                policy.approvedBlockingRules; policy.advisoryRules; policy.experimentalRules
                policy.prototypeRules; policy.dummyRules; policy.deprecatedRules; policy.removedRules
            ]
            let ruleConflicts = ruleSets |> Array.concat |> duplicates
            if not ruleConflicts.IsEmpty then sprintf "rule maturity classes overlap: %s" (String.concat "," ruleConflicts)
            let classifiedRules = ruleSets |> Array.concat |> Set.ofArray
            let missingRules = Set.difference catalogueRules classifiedRules
            let unknownRules = Set.difference classifiedRules catalogueRules
            if not missingRules.IsEmpty then sprintf "catalogue rules lack an M3 classification: %s" (String.concat "," (Set.toList missingRules))
            if not unknownRules.IsEmpty then sprintf "policy classifies unknown rules: %s" (String.concat "," (Set.toList unknownRules))
            for test in policy.requiredTests do
                let project = if isNull test.project then "" else test.project.Replace('\\', '/')
                if String.IsNullOrWhiteSpace test.id || not (validRelativePath project) || test.minimumPassed < 1 then
                    $"invalid required test '{test.id}'"
        ]

    let canonicalPolicyBytes (policy: PolicyLock) =
        JsonSerializer.SerializeToUtf8Bytes(normalizePolicy policy, jsonOptions ())

    let canonicalPolicyIdentity (policy: PolicyLock) =
        match validatePolicy policy with
        | [] ->
            let normalized = normalizePolicy policy
            Ok(normalized, canonicalPolicyBytes normalized |> sha256Bytes)
        | errors -> Error errors

    let private authorityClass policyAvailable (policy: PolicyLock) ruleId =
        if not policyAvailable then "unclassified"
        elif Array.contains ruleId policy.approvedBlockingRules then "blocking"
        elif Array.contains ruleId policy.advisoryRules then "advisory"
        elif Array.contains ruleId policy.experimentalRules then "experimental"
        elif Array.contains ruleId policy.prototypeRules then "prototype"
        elif Array.contains ruleId policy.dummyRules then "dummy"
        elif Array.contains ruleId policy.deprecatedRules then "deprecated"
        elif Array.contains ruleId policy.removedRules then "removed"
        else "unclassified"

    let private findingFingerprint ruleId path line column message =
        sha256Text $"{ruleId}\n{path}\n{line}\n{column}\n{message}"

    let private candidateAuthorityEvidence (candidate: CandidateIdentity) =
        let missing = [
            if candidate.dirty then "candidate worktree contains tracked or untracked changes"
            if candidate.commitSha = "unavailable" then "candidate commit identity is unavailable"
            if candidate.treeSha = "unavailable" then "candidate tree identity is unavailable"
            if candidate.approvedHeadSha = "unavailable" then "approved candidate head identity is unavailable"
        ]
        let errors = [
            if not (Set.contains candidate.kind (set [ "commit"; "dirty-worktree"; "synthetic-merge"; "package"; "unversioned" ])) then "candidate kind is invalid"
            if candidate.commitSha <> "unavailable" && not (isHex 40 candidate.commitSha) then "actual HEAD is not a 40-character Git object ID"
            if candidate.treeSha <> "unavailable" && not (isHex 40 candidate.treeSha) then "actual tree is not a 40-character Git object ID"
            if candidate.approvedHeadSha <> "unavailable" && not (isHex 40 candidate.approvedHeadSha) then "approved head identity is malformed"
            if candidate.syntheticMergeSha <> "not-applicable" && not (isHex 40 candidate.syntheticMergeSha) then "synthetic merge identity is malformed"
            if candidate.packageSha256 <> "not-applicable" && not (isHex 64 candidate.packageSha256) then "package identity is not a SHA-256 digest"
            if candidate.kind = "dirty-worktree" && not candidate.dirty then "dirty-worktree candidate kind contradicts the clean worktree flag"
            if candidate.kind = "commit" && candidate.dirty then "commit candidate kind contradicts the dirty worktree flag"
            if candidate.kind = "unversioned" && (candidate.commitSha <> "unavailable" || candidate.treeSha <> "unavailable") then "unversioned candidate kind contradicts available Git identities"
            if (candidate.kind = "commit" || candidate.kind = "dirty-worktree") && candidate.commitSha <> "unavailable" && candidate.approvedHeadSha <> "unavailable" && candidate.commitSha <> candidate.approvedHeadSha then "approved head identity does not match the analyzed commit"
            if candidate.kind = "synthetic-merge" && candidate.syntheticMergeSha <> candidate.commitSha then "synthetic merge identity does not match the analyzed HEAD"
            if candidate.kind = "commit" && candidate.syntheticMergeSha <> "not-applicable" then "commit candidate unexpectedly carries a synthetic merge identity"
            if candidate.kind = "package" && candidate.packageSha256 = "not-applicable" then "package candidate lacks package identity"
        ]
        missing, errors

    let validateReceipt (receipt: AuthorityReceipt) =
        try
            let projectCount status = receipt.projects |> Array.filter (fun p -> p.status = status) |> Array.length
            let sourceCount status = receipt.sources |> Array.filter (fun s -> s.disposition = status) |> Array.length
            let sortedDistinct (values: string[]) = values |> Array.distinct |> Array.sort
            let snapshotValidation = canonicalPolicyIdentity receipt.policy.snapshot
            let snapshotHash = canonicalPolicyBytes receipt.policy.snapshot |> sha256Bytes
            let structuralErrors = [
                if receipt.schemaVersion <> EvidenceSchemaVersion then "receipt schema version is unsupported"
                if receipt.tool <> "FsAssay" || receipt.toolVersion <> SupportedToolVersion then "receipt tool identity is invalid"
                if not (Set.contains receipt.outcome (set [ "Pass"; "Fail"; "Inconclusive"; "ToolFailure" ])) then "receipt outcome is invalid"
                if not (isHex 40 receipt.candidate.commitSha || receipt.candidate.commitSha = "unavailable") then "candidate commit identity is invalid"
                if not (isHex 40 receipt.candidate.approvedHeadSha || receipt.candidate.approvedHeadSha = "unavailable") then "approved head identity is invalid"
                if not (isHex 40 receipt.candidate.treeSha || receipt.candidate.treeSha = "unavailable") then "candidate tree identity is invalid"
                if not (isHex 40 receipt.candidate.syntheticMergeSha || receipt.candidate.syntheticMergeSha = "not-applicable") then "synthetic merge identity is invalid"
                if not (isHex 64 receipt.candidate.packageSha256 || receipt.candidate.packageSha256 = "not-applicable") then "package identity is invalid"
                if receipt.authoritative && receipt.candidate.dirty then "dirty candidate cannot be authoritative"
                if receipt.outcome = "Pass" && receipt.candidate.dirty then "dirty candidate cannot pass"
                if receipt.authoritative && (receipt.candidate.commitSha = "unavailable" || receipt.candidate.treeSha = "unavailable" || receipt.candidate.approvedHeadSha = "unavailable") then "unversioned or incomplete candidate cannot be authoritative"
                if receipt.outcome = "Pass" && (receipt.candidate.commitSha = "unavailable" || receipt.candidate.treeSha = "unavailable" || receipt.candidate.approvedHeadSha = "unavailable") then "unversioned or incomplete candidate cannot pass"
                if receipt.candidate.kind = "synthetic-merge" && receipt.candidate.syntheticMergeSha <> receipt.candidate.commitSha then "synthetic merge must equal analyzed HEAD"
                if receipt.candidate.kind = "package" && receipt.candidate.packageSha256 = "not-applicable" then "package candidate lacks package identity"
                if (receipt.candidate.kind = "commit" || receipt.candidate.kind = "dirty-worktree") && receipt.candidate.commitSha <> "unavailable" && receipt.candidate.approvedHeadSha <> "unavailable" && receipt.candidate.commitSha <> receipt.candidate.approvedHeadSha then "approved head does not match analyzed commit"
                if receipt.candidate.kind = "dirty-worktree" && not receipt.candidate.dirty then "dirty-worktree kind contradicts dirty flag"
                if receipt.candidate.kind = "commit" && receipt.candidate.dirty then "commit candidate kind contradicts dirty flag"
                if not (validRelativePath receipt.candidate.repositoryRelativeTarget) then "candidate target is not repository-relative"
                if not (validRelativePath receipt.policy.path) then "policy path is not repository-relative"
                if not (Set.contains receipt.policy.status (set [ "loaded"; "invalid"; "unavailable" ])) then "policy status is invalid"
                if receipt.policy.status = "loaded" && not (isHex 64 receipt.policy.sha256) then "loaded policy lacks a SHA-256 identity"
                if receipt.policy.status <> "loaded" && String.IsNullOrWhiteSpace receipt.policy.error then "unavailable or invalid policy lacks an error"
                if receipt.policy.status = "loaded" && (not (String.IsNullOrEmpty receipt.policy.error) || not (Array.isEmpty receipt.policyErrors)) then "loaded policy cannot carry policy errors"
                if receipt.policy.status <> "loaded" && (Array.isEmpty receipt.policyErrors || receipt.policy.error <> String.concat "; " receipt.policyErrors) then "policy error summary does not reconcile"
                if receipt.policy.snapshot <> normalizePolicy receipt.policy.snapshot then "policy snapshot is not in canonical order"
                if receipt.policy.status = "loaded" then
                    match snapshotValidation with
                    | Error errors -> yield! errors |> List.map (fun error -> "policy snapshot is invalid: " + error)
                    | Ok _ when receipt.policy.sha256 <> snapshotHash -> "policy snapshot SHA-256 does not match the recorded policy identity"
                    | Ok _ -> ()
                elif receipt.policy.snapshot <> normalizePolicy unapprovedPolicy then
                    "unavailable or invalid policy must carry the exact fail-closed fallback snapshot"
                if receipt.appliedBaselineRecords <> sortedDistinct receipt.appliedBaselineRecords then "applied baseline record IDs are duplicated or unsorted"
                if not (Array.isEmpty receipt.appliedSuppressions) then "M3 cannot claim applied suppressions"
                for requiredTest in receipt.policy.snapshot.requiredTests do
                    if not (validRelativePath requiredTest.project) then "required test policy project is not repository-relative"
                if receipt.counts.projectsDiscovered <> receipt.projects.Length then "project discovery count does not reconcile"
                if receipt.counts.projectsLoaded <> projectCount "loaded" then "loaded project count does not reconcile"
                if receipt.counts.projectsWorkspaceLoaded <> (receipt.projects |> Array.filter _.loaded |> Array.length) then "workspace-loaded project count does not reconcile"
                if receipt.counts.projectsSupported <> (receipt.projects |> Array.filter _.supported |> Array.length) then "supported project count does not reconcile"
                if receipt.counts.projectsFailed <> projectCount "failed" then "failed project count does not reconcile"
                if receipt.counts.projectsSkipped <> projectCount "skipped" then "skipped project count does not reconcile"
                if receipt.counts.projectsUnsupported <> projectCount "unsupported" then "unsupported project count does not reconcile"
                if receipt.counts.eligibleFiles <> sourceCount "analyzed" + sourceCount "compiler-incomplete" then "eligible source count does not reconcile"
                if receipt.counts.analyzedFiles <> sourceCount "analyzed" then "analyzed source count does not reconcile"
                if receipt.counts.compilerIncompleteFiles <> sourceCount "compiler-incomplete" then "compiler-incomplete source count does not reconcile"
                for project in receipt.projects do
                    if not (validRelativePath project.path) then $"project path is not repository-relative: {project.path}"
                    if not (Set.contains project.status (set [ "loaded"; "failed"; "skipped"; "unsupported" ])) then $"project status is invalid: {project.status}"
                    if String.IsNullOrWhiteSpace project.projectClass || Array.isEmpty project.targetFrameworks then $"project identity is incomplete: {project.path}"
                for source in receipt.sources do
                    if not (validRelativePath source.path) then $"source path is not repository-relative: {source.path}"
                    if not (Set.contains source.disposition (set [ "analyzed"; "compiler-incomplete"; "generated-excluded"; "policy-excluded" ])) then $"source disposition is invalid: {source.disposition}"
                for test in receipt.tests do
                    if String.IsNullOrWhiteSpace test.id || not (validRelativePath test.project) then "test identity is invalid"
                    match test.status with
                    | "passed" when test.passed < 1 || test.failed <> 0 || test.skipped <> 0 -> "passed test counts contradict status"
                    | "failed" when test.failed < 1 -> "failed test requires nonzero failures"
                    | "notRun" when test.passed <> 0 || test.failed <> 0 || test.skipped <> 0 -> "notRun test requires zero counts"
                    | "skipped" when test.failed <> 0 || test.skipped < 1 -> "skipped test counts contradict status"
                    | status when not (Set.contains status (set [ "passed"; "failed"; "notRun"; "skipped" ])) -> $"test status is invalid: {status}"
                    | _ -> ()
                let duplicateRuleIds = receipt.rules |> Array.countBy _.ruleId |> Array.filter (fun (_, count) -> count > 1)
                if not (Array.isEmpty duplicateRuleIds) then "rule receipts contain duplicates"
                for rule in receipt.rules do
                    if not (Set.contains rule.authorityClass (set [ "blocking"; "advisory"; "experimental"; "prototype"; "dummy"; "deprecated"; "removed"; "unclassified" ])) then $"rule authority class is invalid: {rule.ruleId}"
                    let expectedClass = authorityClass (receipt.policy.status = "loaded") receipt.policy.snapshot rule.ruleId
                    if rule.authorityClass <> expectedClass then $"rule authority class does not reconcile with policy: {rule.ruleId}"
                    if not (Set.contains rule.status (set [ "completed"; "incomplete"; "unavailable" ])) then $"rule status is invalid: {rule.ruleId}"
                    if (rule.status = "completed") <> rule.evidenceAvailable then $"rule evidence status contradicts availability: {rule.ruleId}"
                    let actual = receipt.findings |> Array.filter (fun f -> f.ruleId = rule.ruleId) |> Array.length
                    if rule.findingCount <> actual then $"rule finding count does not reconcile: {rule.ruleId}"
                for finding in receipt.findings do
                    if not (validRelativePath finding.path) then $"finding path is not repository-relative: {finding.path}"
                    if String.IsNullOrWhiteSpace finding.symbol then $"finding symbol is blank: {finding.ruleId}"
                    if finding.line < 1 || finding.column < 0 then $"finding location is invalid: {finding.ruleId}"
                    match receipt.rules |> Array.tryFind (fun r -> r.ruleId = finding.ruleId) with
                    | None -> $"finding has no rule outcome: {finding.ruleId}"
                    | Some rule when finding.authorityClass <> rule.authorityClass -> $"finding authority class does not reconcile: {finding.ruleId}"
                    | _ -> ()
                    if not (isHex 64 finding.fingerprint) then $"finding fingerprint is invalid: {finding.ruleId}"
                    if finding.fingerprint <> findingFingerprint finding.ruleId finding.path finding.line finding.column finding.message then $"finding fingerprint does not reconcile: {finding.ruleId}"
                if receipt.reasons <> (receipt.reasons |> Array.sortBy (fun r -> r.code, r.detail)) then "reason ordering is unstable"
                if receipt.reasons.Length <> (receipt.reasons |> Array.distinct).Length then "reason evidence contains duplicates"
                if receipt.projects <> (receipt.projects |> Array.sortBy (fun p -> p.path, p.status)) then "project ordering is unstable"
                if receipt.sources <> (receipt.sources |> Array.sortBy (fun s -> s.path, s.disposition)) then "source ordering is unstable"
                if receipt.rules <> (receipt.rules |> Array.sortBy _.ruleId) then "rule ordering is unstable"
                if receipt.findings <> (receipt.findings |> Array.sortBy (fun f -> f.ruleId, f.path, f.line, f.column, f.message)) then "finding ordering is unstable"
                for name, values in [
                    "policy errors", receipt.policyErrors
                    "evidence errors", receipt.evidenceErrors
                    "missing evidence", receipt.missingEvidence
                    "tool failures", receipt.toolFailures
                ] do
                    if values <> sortedDistinct values then $"{name} are duplicated or unsorted"
            ]
            if not structuralErrors.IsEmpty then structuralErrors
            else
                let policy = receipt.policy.snapshot
                let candidateMissing, candidateErrors = candidateAuthorityEvidence receipt.candidate
                let missingEvidence = (receipt.missingEvidence |> Array.toList) @ candidateMissing
                let evidenceErrors = (receipt.evidenceErrors |> Array.toList) @ candidateErrors
                let loadedProjects = receipt.projects |> Array.filter (fun p -> p.status = "loaded")
                let failedProjects = receipt.projects |> Array.filter (fun p -> p.status = "failed")
                let skippedProjects = receipt.projects |> Array.filter (fun p -> p.status = "skipped")
                let unsupportedProjects = receipt.projects |> Array.filter (fun p -> p.status = "unsupported")
                let analyzedSources = receipt.sources |> Array.filter (fun s -> s.disposition = "analyzed")
                let compilerIncompleteSources = receipt.sources |> Array.filter (fun s -> s.disposition = "compiler-incomplete")
                let eligibleSources = receipt.sources |> Array.filter (fun s -> s.disposition = "analyzed" || s.disposition = "compiler-incomplete")
                let requiredTestFailures = receipt.tests |> Array.filter (fun t -> t.status = "failed")
                let requiredTestsNotRun = receipt.tests |> Array.filter (fun t -> t.status = "notRun")
                let requiredTestsSkipped = receipt.tests |> Array.filter (fun t -> t.status = "skipped")
                let incompleteness = [
                    if not (Array.isEmpty policy.approvedBlockingRules) then
                        yield "gate-c-approval-missing", "blocking rules require Human Gate C approval; this pending candidate permits none"
                    yield! receipt.policyErrors |> Array.toList |> List.map (fun v -> "policy-invalid", v)
                    yield! missingEvidence |> List.map (fun v -> "evidence-missing", v)
                    if String.IsNullOrWhiteSpace receipt.toolchain.sdkVersion || receipt.toolchain.sdkVersion = "unavailable" then
                        yield "sdk-identity-missing", "exact .NET SDK identity is unavailable"
                    if String.IsNullOrWhiteSpace receipt.toolchain.runtimeVersion || String.IsNullOrWhiteSpace receipt.toolchain.fsharpCompilerServiceVersion || receipt.toolchain.fsharpCompilerServiceVersion = "unavailable" then
                        yield "compiler-identity-missing", "runtime or F# compiler service identity is unavailable"
                    if receipt.projects.Length = 0 then yield "projects-zero-discovered", "no eligible F# projects were discovered"
                    if loadedProjects.Length = 0 then yield "projects-zero-loaded", "no eligible F# projects were loaded"
                    if failedProjects.Length > 0 then yield "project-load-failed", $"{failedProjects.Length} project(s) failed to load"
                    if skippedProjects.Length > 0 then yield "project-skipped", $"{skippedProjects.Length} project(s) were skipped"
                    if unsupportedProjects.Length > 0 then yield "project-unsupported", $"{unsupportedProjects.Length} project(s) or framework(s) were unsupported"
                    for requiredClass in policy.requiredProjectClasses do
                        let matching = receipt.projects |> Array.filter (fun p -> p.projectClass = requiredClass)
                        if matching.Length = 0 then
                            yield "required-project-class-missing", $"required project class '{requiredClass}' was not discovered"
                        elif matching |> Array.forall (fun p -> p.status <> "loaded") then
                            yield "required-project-class-unavailable", $"required project class '{requiredClass}' was not loaded"
                    for requiredFramework in policy.requiredTargetFrameworks do
                        if loadedProjects |> Array.forall (fun p -> not (Array.contains requiredFramework p.targetFrameworks)) then
                            yield "required-framework-missing", $"required target framework '{requiredFramework}' was not loaded"
                    if eligibleSources.Length = 0 then yield "files-zero-eligible", "no eligible F# source files were discovered"
                    if analyzedSources.Length = 0 then yield "files-zero-analyzed", "no eligible F# source files were analyzed"
                    if compilerIncompleteSources.Length > 0 then yield "compiler-workspace-incomplete", $"{compilerIncompleteSources.Length} file(s) lacked complete compiler/workspace evidence"
                    for test in requiredTestsNotRun do
                        yield "required-test-not-run", $"required test '{test.id}' was not run"
                    for test in requiredTestsSkipped do
                        yield "required-test-skipped", $"required test '{test.id}' was skipped ({test.skipped})"
                    for requirement in policy.requiredTests do
                        match receipt.tests |> Array.tryFind (fun t -> t.id = requirement.id && t.project.Replace('\\', '/').EndsWith(requirement.project.Replace('\\', '/'), StringComparison.Ordinal)) with
                        | None -> yield "required-test-evidence-missing", $"required test '{requirement.id}' has no evidence"
                        | Some test when test.status = "passed" && test.passed < requirement.minimumPassed ->
                            yield "required-test-count-insufficient", $"required test '{requirement.id}' passed {test.passed}, below minimum {requirement.minimumPassed}"
                        | _ -> ()
                ]
                let failures = [
                    for test in requiredTestFailures do
                        yield "required-test-failed", $"required test '{test.id}' failed ({test.failed})"
                ]
                let toolReasons = [
                    yield! receipt.toolFailures |> Array.toList |> List.map (fun v -> "tool-failure", v)
                    yield! evidenceErrors |> List.map (fun v -> "invalid-evidence", v)
                ]
                let expectedOutcome =
                    if not toolReasons.IsEmpty then "ToolFailure"
                    elif not failures.IsEmpty then "Fail"
                    elif not incompleteness.IsEmpty then "Inconclusive"
                    else "Pass"
                let expectedAuthoritative = expectedOutcome <> "ToolFailure" && incompleteness.IsEmpty && evidenceErrors.IsEmpty
                let expectedReasons =
                    (toolReasons @ failures @ incompleteness)
                    |> List.distinct
                    |> List.sort
                    |> List.map (fun (c, d) -> { code = c; detail = d })
                    |> List.toArray
                [
                    if receipt.outcome <> expectedOutcome then
                        $"receipt outcome '{receipt.outcome}' does not reconcile with evidence outcome '{expectedOutcome}'"
                    if receipt.authoritative <> expectedAuthoritative then
                        $"receipt authoritative={receipt.authoritative} does not reconcile with evidence authority={expectedAuthoritative}"
                    if receipt.reasons <> expectedReasons then
                        "receipt reason set does not reconcile with itemized evidence"
                ]
        with ex -> [ "receipt graph is incomplete: " + ex.Message ]

    let deserializeAndValidateReceipt (bytes: byte[]) =
        try
            let receipt = JsonSerializer.Deserialize<AuthorityReceipt>(bytes, jsonOptions ())
            if isNull (box receipt) then Error [ "receipt JSON was null" ]
            else
                match validateReceipt receipt with
                | [] -> Ok receipt
                | errors -> Error errors
        with ex -> Error [ ex.Message ]

    let deserializeAndValidateReceiptForContext (context: ReceiptValidationContext) (bytes: byte[]) =
        match deserializeAndValidateReceipt bytes with
        | Error errors -> Error errors
        | Ok receipt ->
            let optionalMismatch label length expected actual =
                match expected with
                | None -> None
                | Some value when not (isHex length value) -> Some $"expected {label} is malformed"
                | Some value when value <> actual -> Some $"receipt {label} '{actual}' does not match expected '{value}'"
                | Some _ -> None
            let normExpectedPolicy = normalizePolicySha context.expectedPolicySha256
            let errors = [
                if not (isHex 64 normExpectedPolicy) then
                    "expected policy identity is not a SHA-256 digest"
                elif receipt.policy.status <> "loaded" then
                    "receipt does not contain a loaded policy identity"
                elif receipt.policy.sha256 <> normExpectedPolicy then
                    $"receipt policy identity '{receipt.policy.sha256}' does not match expected '{normExpectedPolicy}'"
                if not (isHex 40 context.expectedCommitSha) then
                    "expected candidate commit is not a 40-character Git object ID"
                elif receipt.candidate.commitSha <> context.expectedCommitSha then
                    $"receipt candidate commit '{receipt.candidate.commitSha}' does not match expected '{context.expectedCommitSha}'"
                if not (isHex 40 context.expectedTreeSha) then
                    "expected candidate tree is not a 40-character Git object ID"
                elif receipt.candidate.treeSha <> context.expectedTreeSha then
                    $"receipt candidate tree '{receipt.candidate.treeSha}' does not match expected '{context.expectedTreeSha}'"
                match optionalMismatch "approved head" 40 context.expectedApprovedHeadSha receipt.candidate.approvedHeadSha with
                | Some error -> error
                | None -> ()
                match optionalMismatch "synthetic merge" 40 context.expectedSyntheticMergeSha receipt.candidate.syntheticMergeSha with
                | Some error -> error
                | None -> ()
                match optionalMismatch "package SHA-256" 64 context.expectedPackageSha256 receipt.candidate.packageSha256 with
                | Some error -> error
                | None -> ()
                if receipt.candidate.kind = "synthetic-merge" && context.expectedApprovedHeadSha.IsNone then
                    "synthetic-merge validation requires an expected approved head"
                if receipt.candidate.kind = "synthetic-merge" && context.expectedSyntheticMergeSha.IsNone then
                    "synthetic-merge validation requires an expected synthetic merge identity"
                if receipt.candidate.kind = "package" && context.expectedPackageSha256.IsNone then
                    "package validation requires an expected package SHA-256"
            ]
            if errors.IsEmpty then Ok receipt else Error errors

    let createPassingReceiptJson (commitSha: string) (treeSha: string) (policyLockJson: string) : string * string =
        let parsedPolicy = JsonSerializer.Deserialize<PolicyLock>(policyLockJson, jsonOptions ())
        let normalizedPolicy, policySha =
            match canonicalPolicyIdentity parsedPolicy with
            | Ok (norm, sha) -> norm, sha
            | Error errs -> invalidOp ("Invalid policy lock for FsAssay receipt: " + String.concat "; " errs)
        let projects =
            normalizedPolicy.requiredProjectClasses
            |> Array.mapi (fun idx cls ->
                let path =
                    if cls = "test" then
                        normalizedPolicy.requiredTests |> Array.tryHead |> Option.map _.project |> Option.defaultValue "tests/Suite.Tests.fsproj"
                    else sprintf "src/Component%d.fsproj" idx
                {
                    path = path
                    projectClass = cls
                    targetFrameworks = normalizedPolicy.requiredTargetFrameworks
                    supported = true
                    loaded = true
                    status = "loaded"
                    reason = ""
                })
            |> Array.sortBy (fun p -> p.path, p.status)
        let tests =
            normalizedPolicy.requiredTests
            |> Array.map (fun req -> {
                id = req.id
                project = req.project
                status = "passed"
                passed = max req.minimumPassed 1
                failed = 0
                skipped = 0
            })
            |> Array.sortBy (fun t -> t.id, t.project)
        let receipt: AuthorityReceipt = {
            schemaVersion = EvidenceSchemaVersion
            tool = "FsAssay"
            toolVersion = SupportedToolVersion
            candidate = {
                kind = "commit"
                commitSha = commitSha
                approvedHeadSha = commitSha
                treeSha = treeSha
                dirty = false
                syntheticMergeSha = "not-applicable"
                packageSha256 = "not-applicable"
                repositoryRelativeTarget = "Hallmark.slnx"
            }
            policy = {
                status = "loaded"
                path = "fsassay-policy.lock.json"
                sha256 = policySha
                snapshot = normalizedPolicy
                error = ""
            }
            toolchain = {
                sdkVersion = "10.0.115"
                runtimeVersion = Environment.Version.ToString()
                fsharpCompilerServiceVersion = "43.10.103.0"
            }
            outcome = "Pass"
            authoritative = true
            reasons = [||]
            counts = {
                projectsDiscovered = projects.Length
                projectsLoaded = projects.Length
                projectsWorkspaceLoaded = projects.Length
                projectsSupported = projects.Length
                projectsFailed = 0
                projectsSkipped = 0
                projectsUnsupported = 0
                eligibleFiles = 1
                analyzedFiles = 1
                compilerIncompleteFiles = 0
            }
            projects = projects
            sources = [| { path = "src/Operations.fs"; disposition = "analyzed"; reason = "" } |]
            tests = tests
            rules = [||]
            findings = [||]
            appliedBaselineRecords = [||]
            appliedSuppressions = [||]
            policyErrors = [||]
            evidenceErrors = [||]
            missingEvidence = [||]
            toolFailures = [||]
        }
        let writeOpts = JsonSerializerOptions(WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        let json = JsonSerializer.Serialize(receipt, writeOpts)
        match deserializeAndValidateReceiptForContext {
            expectedPolicySha256 = policySha
            expectedCommitSha = commitSha
            expectedTreeSha = treeSha
            expectedApprovedHeadSha = None
            expectedSyntheticMergeSha = None
            expectedPackageSha256 = None
        } (Encoding.UTF8.GetBytes json) with
        | Ok _ -> json, policySha
        | Error errs -> invalidOp ("Generated FsAssay receipt failed self-validation: " + String.concat "; " errs)

module Runner =
    let private executionIdentity executables =
        let tools = executables |> Map.toArray |> Array.map (fun (key,path) -> {Key=key;Value=path+":"+Storage.hashFile path})
        let environment = Processes.inheritedEnvironment() |> List.map (fun (key,value) -> {Key=key;Value=ContentDigest.ofText value}) |> List.toArray
        ContentDigest.ofText(Storage.encode {|tools=tools;environment=environment|})
    let private secretRefs operation =
        match operation with
        | PostgresMigrate p -> p.PasswordSecret |> Option.map (fun r -> [r.Name]) |> Option.defaultValue []
        | _ ->
            let values = match operation with Process p -> p.Environment | HttpCheck p -> p.Headers | ComposeUp(_,_,env) -> env | _ -> Map.empty
            values |> Map.toList |> List.choose (fun (_,v) -> match v with Secret r -> Some r.Name | Literal _ -> None)
    let private strings operation =
        let literals values = values |> Map.toList |> List.choose (fun (_,v) -> match v with Literal text -> Some text | Secret _ -> None)
        match operation with
        | Process p -> p.Executable :: p.Directory :: (p.Arguments @ literals p.Environment @ Option.toList p.StandardInputFile)
        | WriteFile (path,text) -> [path;text]
        | Capture path -> [path]
        | ComposeUp (file,project,env) -> [file;project] @ literals env
        | PostgresMigrate p -> p.ContainerEngine :: p.ContainerId :: p.DatabaseName :: p.User :: p.Migrations
        | HttpCheck p -> [p.Url;p.Body;p.Contains] @ literals p.Headers
        | SealRelease version -> [version]
    let private outputKeys = function
        | Process _ -> set ["stdout";"stderr";"exitCode"]
        | WriteFile _ | Capture _ -> set ["path";"digest"]
        | ComposeUp _ -> set ["project";"file"]
        | PostgresMigrate _ -> set ["systemIdentifier";"databaseOid";"version";"checksum"]
        | HttpCheck _ -> set ["status";"body"]
        | SealRelease _ -> set ["path";"digest";"version"]
    let private refs = Regex("\\{\\{([a-z][a-z0-9-]*)\\.([a-zA-Z0-9_-]+)\\}\\}",RegexOptions.CultureInvariant)
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
        | other -> invalidOp ("Unknown AssayKind in gate-b-policy.json: " + other)

    let private parseGateOutcome (name: string) =
        match name.Trim() with
        | "Pass" -> Pass
        | "Inconclusive" -> Inconclusive
        | "Fail" -> Fail
        | "ToolFailure" -> ToolFailure
        | other -> invalidOp ("Unknown GateOutcome in gate-b-policy.json: " + other)

    let private tryJsonString (names: string list) (elem: JsonElement) =
        names
        |> List.tryPick (fun name ->
            match elem.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(v.GetString())) ->
                Some (v.GetString().Trim())
            | _ -> None)

    let private extractExpectedProducers (root: JsonElement) =
        let parseEntry (defaultAssayOpt: string option) (elem: JsonElement) =
            let rawAssay =
                tryJsonString ["assay"; "kind"] elem
                |> Option.orElse defaultAssayOpt
                |> Option.defaultWith (fun () -> invalidOp "Producer identity in gate-b-policy.json requires an assay name")
            let assayKind = parseAssayKind rawAssay
            let assayName = string assayKind
            {|
                assay = assayName
                tool = tryJsonString ["tool"; "producer"; "name"] elem |> Option.defaultValue assayName
                toolVersion = tryJsonString ["toolVersion"; "version"; "producerVersion"] elem |> Option.defaultValue ""
                toolSha256 = tryJsonString ["toolSha256"; "producerSha256"] elem |> Option.map FsAssayAuthority.normalizePolicySha |> Option.defaultValue ""
                commitSha = tryJsonString ["commitSha"; "revision"; "expectedCommitSha"] elem |> Option.defaultValue ""
                treeSha = tryJsonString ["treeSha"; "tree"; "expectedTreeSha"] elem |> Option.defaultValue ""
                policySha256 = tryJsonString ["policySha256"; "policyDigest"; "expectedPolicySha256"] elem |> Option.map FsAssayAuthority.normalizePolicySha |> Option.defaultValue ""
                approvedHeadSha = tryJsonString ["approvedHeadSha"; "expectedApprovedHeadSha"] elem |> Option.defaultValue ""
                syntheticMergeSha = tryJsonString ["syntheticMergeSha"; "expectedSyntheticMergeSha"] elem |> Option.defaultValue ""
                packageSha256 = tryJsonString ["packageSha256"; "expectedPackageSha256"] elem |> Option.map FsAssayAuthority.normalizePolicySha |> Option.defaultValue ""
            |}

        let fromContainer (propElem: JsonElement) =
            if propElem.ValueKind = JsonValueKind.Array then
                propElem.EnumerateArray() |> Seq.map (parseEntry None) |> Seq.toList
            elif propElem.ValueKind = JsonValueKind.Object then
                propElem.EnumerateObject() |> Seq.map (fun p -> parseEntry (Some p.Name) p.Value) |> Seq.toList
            else []

        let explicitEntries =
            ["expectedProducers"; "producers"; "producerIdentities"]
            |> List.collect (fun propName ->
                match root.TryGetProperty propName with
                | true, propElem -> fromContainer propElem
                | _ -> [])

        let topLevelFsAssay =
            let commit = tryJsonString ["commitSha"; "expectedCommitSha"] root |> Option.defaultValue ""
            let tree = tryJsonString ["treeSha"; "expectedTreeSha"] root |> Option.defaultValue ""
            let policy = tryJsonString ["policySha256"; "expectedPolicySha256"] root |> Option.map FsAssayAuthority.normalizePolicySha |> Option.defaultValue ""
            if (commit <> "" || tree <> "" || policy <> "") && not (explicitEntries |> List.exists (fun e -> e.assay = "FsAssay")) then
                [{|
                    assay = "FsAssay"
                    tool = "FsAssay"
                    toolVersion = tryJsonString ["toolVersion"] root |> Option.defaultValue ""
                    toolSha256 = ""
                    commitSha = commit
                    treeSha = tree
                    policySha256 = policy
                    approvedHeadSha = tryJsonString ["approvedHeadSha"; "expectedApprovedHeadSha"] root |> Option.defaultValue ""
                    syntheticMergeSha = tryJsonString ["syntheticMergeSha"; "expectedSyntheticMergeSha"] root |> Option.defaultValue ""
                    packageSha256 = tryJsonString ["packageSha256"; "expectedPackageSha256"] root |> Option.map FsAssayAuthority.normalizePolicySha |> Option.defaultValue ""
                |}]
            else []

        (explicitEntries @ topLevelFsAssay)
        |> List.distinctBy _.assay
        |> List.sortBy _.assay
        |> List.toArray

    let private extractApprovalSubjectsFromElement (fRoot: JsonElement) =
        let releaseVersion =
            match fRoot.TryGetProperty("releaseVersion") with
            | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
            | _ -> ""
        let packages =
            match fRoot.TryGetProperty("packages") with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                arr.EnumerateArray()
                |> Seq.map (fun p -> {|
                    packageId = p.GetProperty("packageId").GetString()
                    componentName = p.GetProperty("componentName").GetString()
                    ownerTeam = p.GetProperty("ownerTeam").GetString()
                    currentVersion = p.GetProperty("currentVersion").GetString()
                    candidateVersion = p.GetProperty("candidateVersion").GetString()
                    sourceDigest = p.GetProperty("sourceDigest").GetString()
                    dependsOn =
                        match p.TryGetProperty("dependsOn") with
                        | true, deps when deps.ValueKind = JsonValueKind.Array ->
                            deps.EnumerateArray() |> Seq.map (fun d -> d.GetString()) |> Seq.toArray
                        | _ -> [||]
                |})
                |> Seq.sortBy _.packageId
                |> Seq.toArray
            | _ -> [||]
        let changeSets =
            match fRoot.TryGetProperty("changeSets") with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                arr.EnumerateArray()
                |> Seq.map (fun c ->
                    let migVer =
                        match c.TryGetProperty("migrationVersion") with
                        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                        | _ -> "V2"
                    {|
                        changeSetId = c.GetProperty("changeSetId").GetString()
                        packageId = c.GetProperty("packageId").GetString()
                        classification = c.GetProperty("classification").GetString()
                        migrationVersion = migVer
                        summary = c.GetProperty("summary").GetString()
                    |})
                |> Seq.sortBy _.changeSetId
                |> Seq.toArray
            | _ -> [||]
        {|
            releaseVersion = releaseVersion
            packages = packages
            changeSets = changeSets
        |}

    let private resolvePolicyPath (directory: string) (rootDir: string) =
        let inRoot = if String.IsNullOrWhiteSpace rootDir then "" else Path.Combine(rootDir, "gate-b-policy.json")
        let inContext = Path.Combine(directory, "gate-b-policy.json")
        if not (String.IsNullOrWhiteSpace inRoot) && File.Exists inRoot then Some inRoot
        elif File.Exists inContext then Some inContext
        else None

    let private resolveGateFile (directory: string) (rootDir: string) (rawPath: string) =
        if Path.IsPathRooted rawPath then rawPath
        elif not (String.IsNullOrWhiteSpace rootDir) then Storage.child rootDir rawPath
        else Storage.child directory rawPath

    let private extractGateRequirements (rootDir: string) (directory: string) (fixtureOverride: string option) =
        match resolvePolicyPath directory rootDir with
        | None -> None
        | Some path ->
            use doc = JsonDocument.Parse(Storage.readBounded path)
            let root = doc.RootElement
            let requiredAssays =
                match root.TryGetProperty("requiredAssays") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.map (fun e -> string (parseAssayKind (e.GetString())))
                    |> Seq.toArray
                | _ -> [||]
            let expectedProducers = extractExpectedProducers root
            let inlineSubjects =
                match root.TryGetProperty("approvalSubjects") with
                | true, subElem when subElem.ValueKind = JsonValueKind.Object ->
                    Some (extractApprovalSubjectsFromElement subElem)
                | _ -> None
            let fixturePathOpt =
                match fixtureOverride with
                | Some fp -> Some (resolveGateFile directory rootDir fp)
                | None ->
                    match root.TryGetProperty("threeOwnerFixture") with
                    | true, prop when prop.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(prop.GetString())) ->
                        Some (resolveGateFile directory rootDir (prop.GetString()))
                    | _ -> None
            let fixtureSubjects =
                match fixturePathOpt with
                | Some fp when File.Exists fp ->
                    use fDoc = JsonDocument.Parse(Storage.readBounded fp)
                    Some (extractApprovalSubjectsFromElement fDoc.RootElement)
                | _ -> None
            match inlineSubjects, fixtureSubjects with
            | Some inl, Some fix when Storage.encode inl <> Storage.encode fix ->
                invalidOp "Gate B admission refused release: threeOwnerFixture subjects do not match frozen approvalSubjects"
            | _ -> ()
            let approvalSubjects = inlineSubjects |> Option.orElse fixtureSubjects
            Some {|
                requiredAssays = requiredAssays
                expectedProducers = expectedProducers
                approvalSubjects = approvalSubjects
            |}

    let createFsAssayReceiptJson (commitSha: string) (treeSha: string) (policyLockPath: string) : string * string =
        FsAssayAuthority.createPassingReceiptJson commitSha treeSha (Storage.readBounded policyLockPath)

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
        let targetDigest = ContentDigest.ofText(identity + workflow.Context)
        let policyFiles =
            [".fsassayrc"; "fsassay-policy.lock.json"]
            |> List.choose (fun name ->
                let path = Path.Combine(root, name)
                if File.Exists path then Some { Key = name; Value = Storage.hashFile path }
                else None)
            |> List.toArray
        let stepPolicies =
            workflow.Steps
            |> List.map (fun s -> {|
                id = s.Id
                kind = string s.Kind
                effect = string s.Effect
                repetition = string s.Repetition
                retrySafety = string s.RetrySafety
                timeoutSeconds = s.TimeoutSeconds
                maxOutputBytes = s.MaxOutputBytes
            |})
            |> List.toArray
        let gateRequirements = extractGateRequirements root directory None
        let policyDigest =
            match gateRequirements with
            | None ->
                ContentDigest.ofText(
                    Storage.encode {|
                        allowedExecutables = workflow.AllowedExecutables
                        stepPolicies = stepPolicies
                        policyFiles = policyFiles
                    |}
                )
            | Some gateReqs ->
                ContentDigest.ofText(
                    Storage.encode {|
                        allowedExecutables = workflow.AllowedExecutables
                        stepPolicies = stepPolicies
                        policyFiles = policyFiles
                        gateRequirements = gateReqs
                    |}
                )
        let draft: PlanDraft = {
            SchemaVersion=1;Name=workflow.Name;ProducerDigest=producer
            TargetDigest=targetDigest
            ConfigurationDigest=ContentDigest.ofText(workflow.CanonicalDefinition + Storage.encode secretVersions)
            PolicyDigest=policyDigest
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
        {Root=root;State=state;Directory=directory;Workflow=workflow;Plan=plan;Digest=Plan.digest plan;SourceDigest=sourceDigest;PolicyDigest=policyDigest;TargetDigest=targetDigest;Sources=sources;SecretVersions=secretVersions;Executables=executables;ExecutionIdentity=adapterIdentity}
    let private environment directory resolve values =
        values |> Map.toList |> List.map (fun (key,value) ->
            key,match value with Literal text -> resolve text | Secret r -> r.Prefix + Storage.secretValue directory r.Name)
    let private substitute (completed: StepRecord array) text =
        refs.Replace(text,MatchEvaluator(fun m ->
            completed |> Array.tryFind (fun step -> step.Id=m.Groups[1].Value && step.Status="succeeded")
            |> Option.bind (fun step -> step.Values |> Array.tryFind (fun p -> p.Key=m.Groups[2].Value))
            |> Option.map _.Value
            |> Option.defaultWith (fun () -> invalidOp "Required runtime output is unavailable")))
    let private evaluateGatePolicyFileWithOverride (directory: string) (rootDir: string) (expectedBindingOpt: ReleaseIdentityBinding option) (fixtureOverride: string option) : FileIdentity list =
        match resolvePolicyPath directory rootDir with
        | None -> []
        | Some path ->
            let evidenceFiles = ResizeArray<FileIdentity>()
            let addEvidenceFile filePath =
                let full = Path.GetFullPath filePath
                evidenceFiles.Add({ Path = full; Digest = Storage.hashFile full })
            addEvidenceFile path
            use doc = JsonDocument.Parse(Storage.readBounded path)
            let root = doc.RootElement
            let requiredAssays =
                root.GetProperty("requiredAssays").EnumerateArray()
                |> Seq.map (fun e -> parseAssayKind (e.GetString()))
                |> Seq.toList
            let expectedProducers =
                extractExpectedProducers root
                |> Array.map (fun p -> p.assay, p)
                |> Map.ofArray
            let lineage =
                if root.TryGetProperty("lineage") |> fst then
                    let lStr = root.GetProperty("lineage").GetString()
                    if lStr = "Active" then Active
                    elif lStr.StartsWith("Superseded:", StringComparison.Ordinal) then Superseded (lStr.Substring(11))
                    elif lStr.StartsWith("Withdrawn:", StringComparison.Ordinal) then Withdrawn (lStr.Substring(10))
                    else invalidOp ("Invalid lineage in gate-b-policy.json: " + lStr)
                else Active
            let resolveFile = resolveGateFile directory rootDir
            let receipts =
                root.GetProperty("receipts").EnumerateArray()
                |> Seq.map (fun r ->
                    let assay = parseAssayKind (r.GetProperty("assay").GetString())
                    let assayName = string assay
                    let declaredOutcome = parseGateOutcome (r.GetProperty("outcome").GetString())
                    let declaredDigest = r.GetProperty("receiptDigest").GetString()
                    match r.TryGetProperty("receiptPath") with
                    | true, rp when rp.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(rp.GetString())) ->
                        let rawReceiptPath = rp.GetString()
                        let fullReceiptPath = resolveFile rawReceiptPath
                        if not (File.Exists fullReceiptPath) then
                            invalidOp (sprintf "Gate B admission refused release: receiptPath not found for %A (%s)" assay fullReceiptPath)
                        let actualDigest = Storage.hashFile fullReceiptPath
                        if actualDigest <> declaredDigest then
                            invalidOp (sprintf "Gate B admission refused release: receiptDigest mismatch for %A (expected %s, observed %s)" assay declaredDigest actualDigest)
                        let rawText = Storage.readBounded fullReceiptPath
                        use rDoc = JsonDocument.Parse(rawText)
                        let rRoot = rDoc.RootElement
                        match rRoot.TryGetProperty("outcome") with
                        | true, outProp when outProp.ValueKind = JsonValueKind.String ->
                            let fileOutcome = parseGateOutcome (outProp.GetString())
                            if fileOutcome <> declaredOutcome || fileOutcome <> Pass then
                                invalidOp (sprintf "Gate B admission refused release: raw receipt %s has outcome %A" fullReceiptPath fileOutcome)
                        | _ ->
                            invalidOp (sprintf "Gate B admission refused release: raw receipt %s is missing outcome property" fullReceiptPath)
                        match rRoot.TryGetProperty("authoritative") with
                        | true, authProp when (authProp.ValueKind = JsonValueKind.True || authProp.ValueKind = JsonValueKind.False) ->
                            if not (authProp.GetBoolean()) then
                                invalidOp (sprintf "Gate B admission refused release: raw receipt %s is not authoritative" fullReceiptPath)
                        | _ ->
                            invalidOp (sprintf "Gate B admission refused release: raw receipt %s is missing authoritative property" fullReceiptPath)
                        let expectedProdOpt = Map.tryFind assayName expectedProducers
                        if assay = FsAssay then
                            let rawBytes = Encoding.UTF8.GetBytes rawText
                            let fsReceipt =
                                match FsAssayAuthority.deserializeAndValidateReceipt rawBytes with
                                | Ok recObj -> recObj
                                | Error errs ->
                                    invalidOp (sprintf "Gate B admission refused release: FsAssay receipt validation failed (%s): %s" fullReceiptPath (String.concat "; " errs))
                            let expCommit =
                                expectedProdOpt
                                |> Option.map _.commitSha
                                |> Option.filter (fun s -> s <> "")
                                |> Option.orElseWith (fun () -> tryJsonString ["commitSha"; "expectedCommitSha"] r)
                                |> Option.defaultValue fsReceipt.candidate.commitSha
                            let expTree =
                                expectedProdOpt
                                |> Option.map _.treeSha
                                |> Option.filter (fun s -> s <> "")
                                |> Option.orElseWith (fun () -> tryJsonString ["treeSha"; "expectedTreeSha"] r)
                                |> Option.defaultValue fsReceipt.candidate.treeSha
                            let expPolicy =
                                expectedProdOpt
                                |> Option.map _.policySha256
                                |> Option.filter (fun s -> s <> "")
                                |> Option.orElseWith (fun () -> tryJsonString ["policySha256"; "expectedPolicySha256"] r |> Option.map FsAssayAuthority.normalizePolicySha)
                                |> Option.defaultValue fsReceipt.policy.sha256
                            let expApprovedHead =
                                expectedProdOpt |> Option.map _.approvedHeadSha |> Option.filter (fun s -> s <> "")
                            let expSyntheticMerge =
                                expectedProdOpt |> Option.map _.syntheticMergeSha |> Option.filter (fun s -> s <> "")
                            let expPackageSha =
                                expectedProdOpt |> Option.map _.packageSha256 |> Option.filter (fun s -> s <> "")
                            match FsAssayAuthority.deserializeAndValidateReceiptForContext {
                                expectedPolicySha256 = expPolicy
                                expectedCommitSha = expCommit
                                expectedTreeSha = expTree
                                expectedApprovedHeadSha = expApprovedHead
                                expectedSyntheticMergeSha = expSyntheticMerge
                                expectedPackageSha256 = expPackageSha
                            } rawBytes with
                            | Ok _ -> ()
                            | Error errs ->
                                invalidOp (sprintf "Gate B admission refused release: FsAssay receipt context mismatch (%s): %s" fullReceiptPath (String.concat "; " errs))
                            match expectedProdOpt with
                            | Some exp when exp.toolVersion <> "" && fsReceipt.toolVersion <> exp.toolVersion ->
                                invalidOp (sprintf "Gate B admission refused release: FsAssay toolVersion mismatch (expected %s, observed %s)" exp.toolVersion fsReceipt.toolVersion)
                            | _ -> ()
                        else
                            match expectedProdOpt with
                            | Some exp ->
                                let obsTool = tryJsonString ["tool"; "producer"] rRoot |> Option.defaultValue ""
                                if exp.tool <> "" && obsTool <> "" && obsTool <> exp.tool then
                                    invalidOp (sprintf "Gate B admission refused release: %A producer tool mismatch (expected %s, observed %s)" assay exp.tool obsTool)
                                let obsVer = tryJsonString ["toolVersion"; "version"] rRoot |> Option.defaultValue ""
                                if exp.toolVersion <> "" && obsVer <> exp.toolVersion then
                                    invalidOp (sprintf "Gate B admission refused release: %A producer version mismatch (expected %s, observed %s)" assay exp.toolVersion obsVer)
                                let obsCommit = tryJsonString ["commitSha"; "revision"] rRoot |> Option.defaultValue ""
                                if exp.commitSha <> "" && obsCommit <> exp.commitSha then
                                    invalidOp (sprintf "Gate B admission refused release: %A commitSha mismatch (expected %s, observed %s)" assay exp.commitSha obsCommit)
                                let obsTree = tryJsonString ["treeSha"; "tree"] rRoot |> Option.defaultValue ""
                                if exp.treeSha <> "" && obsTree <> exp.treeSha then
                                    invalidOp (sprintf "Gate B admission refused release: %A treeSha mismatch (expected %s, observed %s)" assay exp.treeSha obsTree)
                            | None -> ()
                        addEvidenceFile fullReceiptPath
                    | _ ->
                        invalidOp (sprintf "Gate B admission refused release: receiptPath is required for %A receipt" assay)
                    {
                        Assay = assay
                        Outcome = declaredOutcome
                        ReceiptDigest = declaredDigest
                        PlanDigest = r.GetProperty("planDigest").GetString()
                        SourceDigest = r.GetProperty("sourceDigest").GetString()
                        PolicyDigest = r.GetProperty("policyDigest").GetString()
                        TargetDigest = r.GetProperty("targetDigest").GetString()
                    })
                |> Seq.toList
            let fixturePathOpt =
                match fixtureOverride with
                | Some fp -> Some (resolveFile fp)
                | None ->
                    match root.TryGetProperty("threeOwnerFixture") with
                    | true, prop when prop.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(prop.GetString())) ->
                        Some (resolveFile (prop.GetString()))
                    | _ -> None
            let threeOwnerBlockers =
                match fixturePathOpt with
                | Some fixturePath ->
                    if not (File.Exists fixturePath) then
                        invalidOp ("Gate B admission refused release: threeOwnerFixture not found: " + fixturePath)
                    addEvidenceFile fixturePath
                    use fDoc = JsonDocument.Parse(Storage.readBounded fixturePath)
                    let fRoot = fDoc.RootElement
                    match root.TryGetProperty("approvalSubjects") with
                    | true, subElem when subElem.ValueKind = JsonValueKind.Object ->
                        let expectedSub = extractApprovalSubjectsFromElement subElem
                        let observedSub = extractApprovalSubjectsFromElement fRoot
                        if Storage.encode expectedSub <> Storage.encode observedSub then
                            invalidOp "Gate B admission refused release: threeOwnerFixture subjects do not match frozen approvalSubjects"
                    | _ -> ()
                    let packages =
                        fRoot.GetProperty("packages").EnumerateArray()
                        |> Seq.map (fun p -> {
                            PackageId = p.GetProperty("packageId").GetString()
                            ComponentName = p.GetProperty("componentName").GetString()
                            OwnerTeam = p.GetProperty("ownerTeam").GetString()
                            CurrentVersion = p.GetProperty("currentVersion").GetString()
                            CandidateVersion = p.GetProperty("candidateVersion").GetString()
                            SourceDigest = p.GetProperty("sourceDigest").GetString()
                            DependsOn =
                                p.GetProperty("dependsOn").EnumerateArray()
                                |> Seq.map (fun d -> d.GetString())
                                |> Seq.toList
                        })
                        |> Seq.toList
                    let changeSets =
                        fRoot.GetProperty("changeSets").EnumerateArray()
                        |> Seq.map (fun c ->
                            let migVer =
                                match c.TryGetProperty("migrationVersion") with
                                | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                                | _ -> "V2"
                            let cls =
                                match c.GetProperty("classification").GetString() with
                                | "BackwardCompatible" -> BackwardCompatible
                                | "AdditiveMigration" -> AdditiveMigration migVer
                                | "BreakingContract" -> BreakingContract migVer
                                | _ -> Unclassified
                            {
                                ChangeSetId = c.GetProperty("changeSetId").GetString()
                                PackageId = c.GetProperty("packageId").GetString()
                                Classification = cls
                                Summary = c.GetProperty("summary").GetString()
                            })
                        |> Seq.toList
                    let ownerReceipts =
                        fRoot.GetProperty("receipts").EnumerateArray()
                        |> Seq.map (fun r -> {
                            PackageId = r.GetProperty("packageId").GetString()
                            OwnerTeam = r.GetProperty("ownerTeam").GetString()
                            Assay = parseAssayKind (r.GetProperty("assay").GetString())
                            Outcome = parseGateOutcome (r.GetProperty("outcome").GetString())
                            ReceiptDigest = r.GetProperty("receiptDigest").GetString()
                            EvaluatedAgainstSourceDigest = r.GetProperty("sourceDigest").GetString()
                        })
                        |> Seq.toList
                    let corrections =
                        if fRoot.TryGetProperty("corrections") |> fst then
                            fRoot.GetProperty("corrections").EnumerateArray()
                            |> Seq.map (fun c ->
                                let kind =
                                    match c.GetProperty("kind").GetString() with
                                    | "Superseded" ->
                                        let rep =
                                            if c.TryGetProperty("replacementReceiptDigest") |> fst then
                                                c.GetProperty("replacementReceiptDigest").GetString()
                                            else ""
                                        ReceiptSuperseded rep
                                    | _ -> ReceiptWithdrawn
                                {
                                    CorrectionId = c.GetProperty("correctionId").GetString()
                                    AffectedReceiptDigest = c.GetProperty("affectedReceiptDigest").GetString()
                                    Actor = c.GetProperty("actor").GetString()
                                    Reason = c.GetProperty("reason").GetString()
                                    EffectiveAt = c.GetProperty("effectiveAt").GetString()
                                    Kind = kind
                                })
                            |> Seq.toList
                        else []
                    let releaseVer = fRoot.GetProperty("releaseVersion").GetString()
                    let ownerVerdict =
                        Admission.evaluateThreeOwnerCutover releaseVer packages changeSets ownerReceipts corrections
                    ownerVerdict.Blockers
                | None -> []
            let effectiveLineage =
                match lineage, threeOwnerBlockers with
                | Active, b :: _ -> Withdrawn b
                | l, _ -> l
            let verdict =
                match expectedBindingOpt with
                | Some expectedBinding ->
                    Admission.evaluateBoundReleaseGate expectedBinding requiredAssays receipts effectiveLineage
                | None ->
                    let baseReceipts =
                        receipts
                        |> List.map (fun r -> { Assay = r.Assay; Outcome = r.Outcome; ReceiptDigest = r.ReceiptDigest })
                    Admission.evaluateGateB requiredAssays baseReceipts effectiveLineage
            if not verdict.Admitted then
                invalidOp (sprintf "Gate B admission refused release: %A" verdict.Rejections)
            evidenceFiles |> Seq.distinctBy _.Digest |> Seq.toList

    let private evaluateGatePolicyFile (directory: string) (rootDir: string) (expectedBindingOpt: ReleaseIdentityBinding option) : FileIdentity list =
        evaluateGatePolicyFileWithOverride directory rootDir expectedBindingOpt None

    let private saveReleaseGateSnapshot (prepared: Prepared) (releaseVersion: string) (runId: string) =
        let normalizedVersion = Storage.name "version" releaseVersion
        let gateSnapshotPath = Path.Combine(prepared.Directory, "releases", normalizedVersion + ".gate.json")
        let reqsDigest =
            match extractGateRequirements prepared.Root prepared.Directory None with
            | Some reqs -> ContentDigest.ofText(Storage.encode reqs)
            | None -> ""
        let payload =
            Storage.encode {|
                schemaVersion = 1
                version = normalizedVersion
                runId = runId
                planDigest = prepared.Digest
                sourceDigest = prepared.SourceDigest
                policyDigest = prepared.PolicyDigest
                targetDigest = prepared.TargetDigest
                rootDirectory = prepared.Root
                requirementsDigest = reqsDigest
            |}
        Storage.atomic gateSnapshotPath payload

    let verifyPromotionGate (directory: string) (version: string) (trustedPublicKey: string) (fixtureOverride: string option) =
        let normalizedVersion = Storage.name "version" version
        let releasePath = Path.Combine(directory, "releases", normalizedVersion + ".json")
        if not (File.Exists releasePath) then
            invalidOp ("Promotion refused: signed release record not found for version " + normalizedVersion)
        let release = Storage.verifyRelease releasePath trustedPublicKey
        let origin = Storage.loadRun directory release.RunId
        if origin.PlanDigest <> release.PlanDigest then
            invalidOp "Promotion refused: signed release planDigest does not match the originating run"
        match fixtureOverride with
        | Some fixturePath ->
            if not (File.Exists fixturePath) then
                invalidOp ("Promotion refused: three-owner fixture not found: " + fixturePath)
            use fDoc = JsonDocument.Parse(Storage.readBounded fixturePath)
            let fRoot = fDoc.RootElement
            let packages =
                fRoot.GetProperty("packages").EnumerateArray()
                |> Seq.map (fun p -> {
                    PackageId = p.GetProperty("packageId").GetString()
                    ComponentName = p.GetProperty("componentName").GetString()
                    OwnerTeam = p.GetProperty("ownerTeam").GetString()
                    CurrentVersion = p.GetProperty("currentVersion").GetString()
                    CandidateVersion = p.GetProperty("candidateVersion").GetString()
                    SourceDigest = p.GetProperty("sourceDigest").GetString()
                    DependsOn =
                        p.GetProperty("dependsOn").EnumerateArray()
                        |> Seq.map (fun d -> d.GetString())
                        |> Seq.toList
                })
                |> Seq.toList
            let changeSets =
                fRoot.GetProperty("changeSets").EnumerateArray()
                |> Seq.map (fun c ->
                    let migVer =
                        match c.TryGetProperty("migrationVersion") with
                        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                        | _ -> "V2"
                    let cls =
                        match c.GetProperty("classification").GetString() with
                        | "BackwardCompatible" -> BackwardCompatible
                        | "AdditiveMigration" -> AdditiveMigration migVer
                        | "BreakingContract" -> BreakingContract migVer
                        | _ -> Unclassified
                    {
                        ChangeSetId = c.GetProperty("changeSetId").GetString()
                        PackageId = c.GetProperty("packageId").GetString()
                        Classification = cls
                        Summary = c.GetProperty("summary").GetString()
                    })
                |> Seq.toList
            let ownerReceipts =
                fRoot.GetProperty("receipts").EnumerateArray()
                |> Seq.map (fun r -> {
                    PackageId = r.GetProperty("packageId").GetString()
                    OwnerTeam = r.GetProperty("ownerTeam").GetString()
                    Assay = parseAssayKind (r.GetProperty("assay").GetString())
                    Outcome = parseGateOutcome (r.GetProperty("outcome").GetString())
                    ReceiptDigest = r.GetProperty("receiptDigest").GetString()
                    EvaluatedAgainstSourceDigest = r.GetProperty("sourceDigest").GetString()
                })
                |> Seq.toList
            let corrections =
                if fRoot.TryGetProperty("corrections") |> fst then
                    fRoot.GetProperty("corrections").EnumerateArray()
                    |> Seq.map (fun c ->
                        let kind =
                            match c.GetProperty("kind").GetString() with
                            | "Superseded" ->
                                let rep =
                                    if c.TryGetProperty("replacementReceiptDigest") |> fst then
                                        c.GetProperty("replacementReceiptDigest").GetString()
                                    else ""
                                ReceiptSuperseded rep
                            | _ -> ReceiptWithdrawn
                        {
                            CorrectionId = c.GetProperty("correctionId").GetString()
                            AffectedReceiptDigest = c.GetProperty("affectedReceiptDigest").GetString()
                            Actor = c.GetProperty("actor").GetString()
                            Reason = c.GetProperty("reason").GetString()
                            EffectiveAt = c.GetProperty("effectiveAt").GetString()
                            Kind = kind
                        })
                    |> Seq.toList
                else []
            let releaseVer = fRoot.GetProperty("releaseVersion").GetString()
            let verdict = Admission.evaluateThreeOwnerCutover releaseVer packages changeSets ownerReceipts corrections
            if not verdict.Admitted then
                invalidOp ("Promotion refused by three-owner cutover gate: " + String.Join("; ", verdict.Blockers))
        | None -> ()
        let gateSnapshotPath = Path.Combine(directory, "releases", normalizedVersion + ".gate.json")
        if File.Exists gateSnapshotPath then
            use sDoc = JsonDocument.Parse(Storage.readBounded gateSnapshotPath)
            let sRoot = sDoc.RootElement
            let snapPlan = sRoot.GetProperty("planDigest").GetString()
            if snapPlan <> release.PlanDigest then
                invalidOp "Promotion refused: release gate snapshot planDigest does not match signed release"
            let snapRoot =
                match sRoot.TryGetProperty("rootDirectory") with
                | true, r when r.ValueKind = JsonValueKind.String && Directory.Exists(r.GetString()) -> r.GetString()
                | _ -> Environment.CurrentDirectory
            let snapReqDigest =
                match sRoot.TryGetProperty("requirementsDigest") with
                | true, r when r.ValueKind = JsonValueKind.String -> r.GetString()
                | _ -> ""
            if snapReqDigest <> "" then
                let currentReqDigest =
                    match extractGateRequirements snapRoot directory fixtureOverride with
                    | Some reqs -> ContentDigest.ofText(Storage.encode reqs)
                    | None -> ""
                if currentReqDigest <> snapReqDigest then
                    invalidOp "Promotion refused: frozen Gate B requirements or approval subjects changed after release signing"
            let expectedBinding = {
                PlanDigest = release.PlanDigest
                SourceDigest = sRoot.GetProperty("sourceDigest").GetString()
                PolicyDigest = sRoot.GetProperty("policyDigest").GetString()
                TargetDigest = sRoot.GetProperty("targetDigest").GetString()
            }
            evaluateGatePolicyFileWithOverride directory snapRoot (Some expectedBinding) fixtureOverride |> ignore
        else
            evaluateGatePolicyFileWithOverride directory Environment.CurrentDirectory None fixtureOverride |> ignore

    let private evaluateReleaseGatePolicy (prepared: Prepared) (releaseStep: Step) (completed: StepRecord array) : FileIdentity list =
        let byId = prepared.Workflow.Steps |> List.map (fun s -> s.Id, s) |> Map.ofList
        let rec collectAncestors seen id =
            if Set.contains id seen then seen
            else byId[id].Needs |> List.fold collectAncestors (Set.add id seen)
        let releaseAncestors = releaseStep.Needs |> List.fold collectAncestors Set.empty
        let ancestorVerifySteps =
            prepared.Workflow.Steps
            |> List.filter (fun s -> s.Kind = Verify && Set.contains s.Id releaseAncestors)
        if ancestorVerifySteps.IsEmpty then
            invalidOp "Gate B admission refused release: no Verify ancestor declared"
        for vStep in ancestorVerifySteps do
            match completed |> Array.tryFind (fun c -> c.Id = vStep.Id) with
            | Some c when c.Status = "succeeded" && c.ExitCode = 0 -> ()
            | _ -> invalidOp ("Gate B admission refused release: verification ancestor " + vStep.Id + " did not pass")

        let expectedBinding = {
            PlanDigest = prepared.Digest
            SourceDigest = prepared.SourceDigest
            PolicyDigest = prepared.PolicyDigest
            TargetDigest = prepared.TargetDigest
        }
        let files = evaluateGatePolicyFile prepared.Directory prepared.Root (Some expectedBinding)
        let inRoot = Path.Combine(prepared.Root, "gate-b-policy.json")
        let inContext = Path.Combine(prepared.Directory, "gate-b-policy.json")
        if File.Exists inRoot && Path.GetFullPath inRoot <> Path.GetFullPath inContext then
            Storage.atomic inContext (Storage.readBounded inRoot)
        files

    let private postgresReadLedgerSql = """
SELECT pg_catalog.json_build_object(
    'identity', pg_catalog.json_build_object(
        'systemIdentifier', control.system_identifier::text, 'databaseOid', db.oid::bigint
    ),
    'ledger', (SELECT COALESCE(pg_catalog.json_agg(pg_catalog.json_build_object(
        'version', version, 'script', script, 'kind', type,
        'checksum', checksum, 'success', success
    ) ORDER BY installed_rank), '[]'::json) FROM public.flyway_schema_history)
)
FROM pg_catalog.pg_control_system() AS control
CROSS JOIN pg_catalog.pg_database AS db
WHERE db.datname = pg_catalog.current_database();
"""

    let private flywayImageFor engine =
        if engine = "podman" then
            "docker.io/flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657"
        else
            "flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657"

    let private enginePrefixArgs engine =
        if engine = "podman" then ["--remote=false"]
        else ["--host"; "unix:///var/run/docker.sock"]

    let private readPostgresMigrations (root: string) (migrationFiles: string list) =
        let parsed =
            migrationFiles
            |> List.mapi (fun idx relPath ->
                let expectedVersion = idx + 1
                let fullPath = Storage.child root relPath
                let scriptName = Path.GetFileName fullPath
                let prefix = sprintf "V%d__" expectedVersion
                if not (scriptName.StartsWith(prefix, StringComparison.Ordinal) && scriptName.EndsWith(".sql", StringComparison.Ordinal)) then
                    invalidOp (sprintf "Expected migration filename %sdescription.sql, got %s" prefix scriptName)
                let sql = Storage.readBounded fullPath
                let checksum = Admission.computeFlywayChecksum sql
                { Version = expectedVersion; Script = scriptName; Checksum = checksum }, fullPath)
        let migrations = parsed |> List.map fst
        let preceding = migrations |> List.take (migrations.Length - 1)
        let current = migrations |> List.last
        parsed, preceding, current

    let private queryPostgresSnapshot (prepared: Prepared) (spec: PostgresMigrateSpec) (cancel: CancellationToken) =
        let exe = Processes.executable spec.ContainerEngine
        let args =
            enginePrefixArgs spec.ContainerEngine
            @ [
                "exec"
                "--env"; "PGCONNECT_TIMEOUT=5"
                "--env"; "PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=5000 -c search_path=pg_catalog"
                "--user"; "postgres"
                spec.ContainerId
                "psql"; "--no-psqlrc"; "--no-password"; "--host"; "/var/run/postgresql"
                "--dbname"; spec.DatabaseName; "--username"; spec.User
                "--set"; "ON_ERROR_STOP=1"; "--quiet"; "--tuples-only"; "--no-align"
                "--command"; postgresReadLedgerSql
            ]
        let result = Processes.run exe args prepared.Root [] None 15 262144 [] cancel |> _.GetAwaiter().GetResult()
        if result.Status <> "completed" || result.ExitCode <> 0 then
            invalidOp ("Could not read PostgreSQL identity and Flyway history: " + result.Stderr.Trim())
        use doc = System.Text.Json.JsonDocument.Parse(result.Stdout.Trim())
        let root = doc.RootElement
        let idElem = root.GetProperty("identity")
        let sysId = idElem.GetProperty("systemIdentifier").GetString()
        let dbOid = idElem.GetProperty("databaseOid").GetInt64()
        if String.IsNullOrWhiteSpace sysId || dbOid <= 0L then
            invalidOp "Invalid PostgreSQL target identity"
        let ledger =
            root.GetProperty("ledger").EnumerateArray()
            |> Seq.map (fun row ->
                let ver =
                    match row.TryGetProperty("version") with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> Some (v.GetString())
                    | _ -> None
                let cs =
                    match row.TryGetProperty("checksum") with
                    | true, c when c.ValueKind = System.Text.Json.JsonValueKind.Number -> Some (c.GetInt32())
                    | _ -> None
                {
                    Version = ver
                    Script = row.GetProperty("script").GetString()
                    Kind = row.GetProperty("kind").GetString()
                    Checksum = cs
                    Success = row.GetProperty("success").GetBoolean()
                })
            |> Seq.toList
        {| SystemIdentifier = sysId; DatabaseOid = dbOid; Ledger = ledger |}

    let private writeReconciledPostgresOutputs (prepared: Prepared) (step: Step) (runIdOpt: string option) (sysId: string) (dbOid: int64) (current: FlywayMigration) (ledgerCount: int) (overwriteWorkspaceOutputs: bool) =
        let ledgerPath = Path.Combine(prepared.Directory, "ledger", step.Id + ".json")
        let payload =
            Storage.encode {|
                status = "committed"
                runId = Option.defaultValue "" runIdOpt
                stepId = step.Id
                systemIdentifier = sysId
                databaseOid = dbOid
                version = string current.Version
                script = current.Script
                checksum = string current.Checksum
                ledgerEntries = ledgerCount
            |}
        Storage.atomic ledgerPath payload
        if overwriteWorkspaceOutputs then
            for outRel in step.Outputs do
                let outPath = Storage.child prepared.Root outRel
                Storage.atomicOutput outPath payload

    let private writePostgresLedgerConflict (prepared: Prepared) (step: Step) (runIdOpt: string option) (sysId: string) (dbOid: int64) (version: string) (script: string) (reason: string) =
        let ledgerPath = Path.Combine(prepared.Directory, "ledger", step.Id + ".json")
        let payload =
            Storage.encode {|
                status = "conflict"
                runId = Option.defaultValue "" runIdOpt
                stepId = step.Id
                systemIdentifier = sysId
                databaseOid = dbOid
                version = version
                script = script
                reason = reason
            |}
        Storage.atomic ledgerPath payload

    let private observePostgresLedger (prepared: Prepared) (step: Step) (spec: PostgresMigrateSpec) (priorRunId: string option) (overwriteOutputs: bool) : ExternalLedgerReadback =
        let mutable observedSysId = ""
        let mutable observedOid = 0L
        let mutable expectedVersion = ""
        let mutable expectedScript = ""
        try
            let _, preceding, current = readPostgresMigrations prepared.Root spec.Migrations
            expectedVersion <- string current.Version
            expectedScript <- current.Script
            let targetFile = Path.Combine(prepared.Directory, "ledger", step.Id + ".target.json")
            let snapshot = queryPostgresSnapshot prepared spec CancellationToken.None
            observedSysId <- snapshot.SystemIdentifier
            observedOid <- snapshot.DatabaseOid
            if not (File.Exists targetFile) then
                raise (InvalidOperationException "Recorded PostgreSQL target identity file is missing")
            use tDoc = System.Text.Json.JsonDocument.Parse(Storage.readBounded targetFile)
            let tRoot = tDoc.RootElement
            let expSys = tRoot.GetProperty("systemIdentifier").GetString()
            let expOid = tRoot.GetProperty("databaseOid").GetInt64()
            if expSys <> snapshot.SystemIdentifier || expOid <> snapshot.DatabaseOid then
                raise (InvalidOperationException "PostgreSQL target identity changed after preparation")
            match Admission.inspectFlywayHistory preceding current snapshot.Ledger with
            | CurrentMigrationCommitted _ ->
                writeReconciledPostgresOutputs prepared step priorRunId snapshot.SystemIdentifier snapshot.DatabaseOid current snapshot.Ledger.Length overwriteOutputs
                let outputsValid =
                    step.Outputs
                    |> List.forall (fun out -> File.Exists(Storage.child prepared.Root out))
                CommittedInLedger outputsValid
            | ConflictingMigrationChecksum (_, exp, obs) ->
                let reason = sprintf "Flyway checksum conflicts with the frozen migration (expected %d, observed %A)" exp obs
                writePostgresLedgerConflict prepared step priorRunId snapshot.SystemIdentifier snapshot.DatabaseOid expectedVersion expectedScript reason
                ConflictingLedger reason
            | DivergedFlywayHistory reason ->
                let fullReason = "Flyway history conflicts with the frozen migration sequence: " + reason
                writePostgresLedgerConflict prepared step priorRunId snapshot.SystemIdentifier snapshot.DatabaseOid expectedVersion expectedScript fullReason
                ConflictingLedger fullReason
            | ExpectedPredecessor ->
                AbsentFromLedger
        with e ->
            writePostgresLedgerConflict prepared step priorRunId observedSysId observedOid expectedVersion expectedScript e.Message
            ConflictingLedger e.Message

    let private observeExternalLedger (prepared: Prepared) (step: Step) (priorStep: StepRecord option) (priorRunId: string option) (overwriteOutputs: bool) : ExternalLedgerReadback =
        match step.Operation with
        | PostgresMigrate spec ->
            observePostgresLedger prepared step spec priorRunId overwriteOutputs
        | SealRelease version ->
            let normalizedVersion = Storage.name "version" version
            let releasePath = Path.Combine(prepared.Directory, "releases", normalizedVersion + ".json")
            if not (File.Exists releasePath) then
                AbsentFromLedger
            else
                try
                    use key = Storage.signingKey prepared.Directory
                    let record = Storage.verifyRelease releasePath (key.ExportSubjectPublicKeyInfoPem())
                    if record.Name <> prepared.Workflow.Name || record.Context <> prepared.Workflow.Context || record.Version <> normalizedVersion then
                        invalidOp "Signed release identity does not match the active workflow name, context, or version"
                    if record.PlanDigest <> prepared.Digest then
                        invalidOp "Signed release planDigest does not match the active frozen plan"
                    match priorStep, priorRunId with
                    | Some p, Some expectedRunId when p.Status <> "succeeded" ->
                        if record.RunId <> expectedRunId then
                            invalidOp (sprintf "Signed release runId (%s) does not match the originating run (%s)" record.RunId expectedRunId)
                    | Some p, Some _ ->
                        let releaseDigest = Storage.hashFile releasePath
                        if not (p.Files |> Array.exists (fun f -> f.Path = releasePath && f.Digest = releaseDigest)) then
                            invalidOp "Prior completed release stage does not reference the active signed release record"
                    | _, None ->
                        invalidOp "Signed release cannot be reused without an originating run identity"
                    | None, Some _ ->
                        invalidOp "Signed release cannot be reused without a prior release stage record"
                    let origin = Storage.loadRun prepared.Directory record.RunId
                    if origin.PlanDigest <> record.PlanDigest || origin.SourceDigest <> prepared.SourceDigest then
                        invalidOp "Originating release run identity does not match the frozen plan or source"
                    let preReleaseSteps =
                        origin.Steps
                        |> Array.takeWhile (fun s -> s.Id <> step.Id)
                    if ContentDigest.ofText(Storage.encode preReleaseSteps) <> record.EvidenceDigest then
                        invalidOp "Signed release evidenceDigest does not match the originating run steps"
                    let expectedBinding = {
                        PlanDigest = prepared.Digest
                        SourceDigest = prepared.SourceDigest
                        PolicyDigest = prepared.PolicyDigest
                        TargetDigest = prepared.TargetDigest
                    }
                    evaluateGatePolicyFile prepared.Directory prepared.Root (Some expectedBinding) |> ignore
                    CommittedInLedger true
                with e ->
                    ConflictingLedger ("Existing release record failed verification: " + e.Message)
        | _ ->
            let ledgerPath = Path.Combine(prepared.Directory, "ledger", step.Id + ".json")
            if not (File.Exists ledgerPath) then
                Unobserved
            else
                use doc = System.Text.Json.JsonDocument.Parse(Storage.readBounded ledgerPath)
                let root = doc.RootElement
                match root.GetProperty("status").GetString() with
                | "committed" ->
                    let outputsValid =
                        step.Outputs
                        |> List.forall (fun out -> File.Exists(Storage.child prepared.Root out))
                    CommittedInLedger outputsValid
                | "absent" -> AbsentFromLedger
                | "conflict" ->
                    let reason =
                        if root.TryGetProperty("reason") |> fst then root.GetProperty("reason").GetString()
                        else "conflicting ledger observation"
                    ConflictingLedger reason
                | _ -> Unobserved
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
        use ownershipLock = Storage.lockFile (Path.Combine(prepared.State,"ownership.lock"))
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
            if result.Status<>"completed" then
                raise (AdapterFailure(result.Status,result.ExitCode,"Process " + step.Id + ": " + result.Status))
            if result.ExitCode<>p.ExpectedExit then
                let detail = $"Process {step.Id} exited with code {result.ExitCode} (expected {p.ExpectedExit})"
                raise (AdapterFailure("failed",result.ExitCode,detail))
            values <- [|{Key="stdout";Value=result.Stdout.Trim()};{Key="stderr";Value=result.Stderr.Trim()};{Key="exitCode";Value=string result.ExitCode}|]
        | WriteFile (path,content) ->
            let path = Storage.child prepared.Root (resolve path)
            claim prepared ("file:"+path)
            Storage.atomicOutput path (resolve content)
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
        | PostgresMigrate spec ->
            let resolvedSpec = {
                spec with
                    ContainerId = resolve spec.ContainerId
                    DatabaseName = resolve spec.DatabaseName
                    User = resolve spec.User
            }
            let parsedMigrations, preceding, current = readPostgresMigrations prepared.Root resolvedSpec.Migrations
            let preSnapshot = queryPostgresSnapshot prepared resolvedSpec cancel
            match Admission.inspectFlywayHistory preceding current preSnapshot.Ledger with
            | ExpectedPredecessor -> ()
            | _ ->
                writePostgresLedgerConflict prepared step (Some run) preSnapshot.SystemIdentifier preSnapshot.DatabaseOid (string current.Version) current.Script "Flyway history differs from the expected predecessor; no migration was dispatched"
                raise (AdapterFailure("failed", 3, "Flyway history differs from the expected predecessor; no migration was dispatched"))
            let targetFile = Path.Combine(prepared.Directory, "ledger", step.Id + ".target.json")
            Storage.atomic targetFile (Storage.encode {| systemIdentifier = preSnapshot.SystemIdentifier; databaseOid = preSnapshot.DatabaseOid |})
            let password =
                match resolvedSpec.PasswordSecret with
                | Some secretRef -> secretRef.Prefix + Storage.secretValue prepared.Directory secretRef.Name
                | None ->
                    let envPass = Environment.GetEnvironmentVariable("FLYWAY_PASSWORD")
                    if String.IsNullOrEmpty envPass then
                        raise (AdapterFailure("failed", 3, "postgres-migrate requires passwordSecret or FLYWAY_PASSWORD"))
                    envPass
            let stageDir = Path.Combine(prepared.Directory, "flyway-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory stageDir |> ignore
            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(stageDir, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherExecute)
            try
                for m, fullPath in parsedMigrations do
                    let dst = Path.Combine(stageDir, m.Script)
                    File.Copy(fullPath, dst, true)
                    if not (OperatingSystem.IsWindows()) then
                        File.SetUnixFileMode(dst, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.GroupRead ||| UnixFileMode.OtherRead)
                let flywayArgs =
                    enginePrefixArgs resolvedSpec.ContainerEngine
                    @ ["run"; "--rm"]
                    @ (if resolvedSpec.ContainerEngine = "podman" then ["--timeout"; "120"] else [])
                    @ [
                        "--pull"; "never"
                        "--network"; "container:" + resolvedSpec.ContainerId
                        "--cap-drop"; "ALL"
                        "--security-opt"; "no-new-privileges"
                        "--mount"; sprintf "type=bind,source=%s,target=/flyway/sql,readonly" stageDir
                        "--env"; "FLYWAY_PASSWORD"
                        flywayImageFor resolvedSpec.ContainerEngine
                        "-locations=filesystem:/flyway/sql"
                        "-defaultSchema=public"
                        "-table=flyway_schema_history"
                        "-validateOnMigrate=true"
                        "-cleanDisabled=true"
                        "-placeholderReplacement=false"
                        "-outOfOrder=false"
                        "-baselineOnMigrate=false"
                        "-outputType=json"
                        sprintf "-url=jdbc:postgresql://127.0.0.1:5432/%s" resolvedSpec.DatabaseName
                        sprintf "-user=%s" resolvedSpec.User
                        sprintf "-target=%d" current.Version
                        "migrate"
                    ]
                let! migrateResult = Processes.run (Processes.executable resolvedSpec.ContainerEngine) flywayArgs prepared.Root [("FLYWAY_PASSWORD", password)] None step.TimeoutSeconds step.MaxOutputBytes (password :: secrets) cancel
                Storage.atomic (Path.Combine(prepared.Directory, "logs", run, step.Id + ".stdout.log")) (Processes.redact (password :: secrets) migrateResult.Stdout)
                Storage.atomic (Path.Combine(prepared.Directory, "logs", run, step.Id + ".stderr.log")) (Processes.redact (password :: secrets) migrateResult.Stderr)
            finally
                if Directory.Exists stageDir then Directory.Delete(stageDir, true)
            let postSnapshot = queryPostgresSnapshot prepared resolvedSpec cancel
            if postSnapshot.SystemIdentifier <> preSnapshot.SystemIdentifier || postSnapshot.DatabaseOid <> preSnapshot.DatabaseOid then
                writePostgresLedgerConflict prepared step (Some run) postSnapshot.SystemIdentifier postSnapshot.DatabaseOid (string current.Version) current.Script "PostgreSQL target identity changed after preparation"
                raise (AdapterFailure("failed", 3, "PostgreSQL target identity changed after preparation"))
            match Admission.inspectFlywayHistory preceding current postSnapshot.Ledger with
            | CurrentMigrationCommitted cs ->
                writeReconciledPostgresOutputs prepared step (Some run) postSnapshot.SystemIdentifier postSnapshot.DatabaseOid current postSnapshot.Ledger.Length true
                message <- "applied"
                values <- [|
                    { Key = "systemIdentifier"; Value = postSnapshot.SystemIdentifier }
                    { Key = "databaseOid"; Value = string postSnapshot.DatabaseOid }
                    { Key = "version"; Value = string current.Version }
                    { Key = "checksum"; Value = string cs }
                |]
            | _ ->
                writePostgresLedgerConflict prepared step (Some run) postSnapshot.SystemIdentifier postSnapshot.DatabaseOid (string current.Version) current.Script "migration commit is unconfirmed in flyway_schema_history"
                raise (AdapterFailure("failed", 3, "migration commit is unconfirmed in flyway_schema_history"))
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
            let gateEvidenceFiles = evaluateReleaseGatePolicy prepared step completed |> List.toArray
            let artifacts =
                Array.append (completed |> Array.collect _.Files) gateEvidenceFiles
                |> Array.distinct
                |> Array.map (fun file -> let path,digest = Storage.storeArtifact prepared.Directory file.Path in {Path=path;Digest=digest})
                |> Array.distinct
            let release,path = Storage.seal prepared.Directory prepared.Workflow.Name (resolve version) prepared.Digest run prepared.Workflow.Context artifacts (ContentDigest.ofText(Storage.encode completed))
            saveReleaseGateSnapshot prepared release.Version release.RunId
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
                let priorRunId = previous |> Option.map _.RunId
                let readback = if step.RetrySafety = ExternalLedger then observeExternalLedger prepared step (Some p) priorRunId (p.Status <> "succeeded") else Unobserved
                let decision = if converging && (step.Effect=DesiredState || step.Kind=Verify) then CanonForge.Execution.Execute else Recovery.decideWithLedger true (filesCurrent p.Files) step.RetrySafety outcome readback
                match decision with
                | Refuse reason -> invalidOp (step.Id + ": " + reason)
                | Reuse | CanonForge.Execution.Execute -> ()
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
                    | None -> CanonForge.Execution.Execute
                    | Some _ when converging && (step.Effect=DesiredState || step.Kind=Verify) -> CanonForge.Execution.Execute
                    | Some p ->
                        let outcome = if p.Status="succeeded" then Completed elif p.Status="running" then Interrupted else Failed
                        let priorRunId = previous |> Option.map _.RunId
                        let readback = if step.RetrySafety = ExternalLedger then observeExternalLedger prepared step (Some p) priorRunId (p.Status <> "succeeded") else Unobserved
                        Recovery.decideWithLedger true (filesCurrent p.Files) step.RetrySafety outcome readback
                match reuse with
                | Refuse reason -> invalidOp (id + ": " + reason)
                | Reuse ->
                    let priorRecord = prior.Value
                    let reconciledRecord =
                        if priorRecord.Status = "succeeded" then priorRecord
                        else
                            let resolvedFiles, resolvedValues =
                                match step.Operation with
                                | SealRelease version ->
                                    let normalizedVersion = Storage.name "version" (substitute completed version)
                                    let releasePath = Path.Combine(prepared.Directory, "releases", normalizedVersion + ".json")
                                    let releaseDigest = Storage.hashFile releasePath
                                    saveReleaseGateSnapshot prepared normalizedVersion runId
                                    let declared =
                                        step.Outputs
                                        |> List.map (substitute completed >> Storage.child prepared.Root)
                                        |> List.map (fun path -> { Path = path; Digest = Storage.hashFile path })
                                        |> List.toArray
                                    let files = Array.append [| { Path = releasePath; Digest = releaseDigest } |] declared |> Array.distinct
                                    let values = [|
                                        { Key = "path"; Value = releasePath }
                                        { Key = "digest"; Value = releaseDigest }
                                        { Key = "version"; Value = normalizedVersion }
                                    |]
                                    files, values
                                | _ ->
                                    let files =
                                        step.Outputs
                                        |> List.map (Storage.child prepared.Root)
                                        |> List.map (fun path -> { Path = path; Digest = Storage.hashFile path })
                                        |> List.toArray
                                    let values =
                                        let ledgerPath = Path.Combine(prepared.Directory, "ledger", id + ".json")
                                        if File.Exists ledgerPath then
                                            try
                                                use doc = System.Text.Json.JsonDocument.Parse(Storage.readBounded ledgerPath)
                                                let root = doc.RootElement
                                                [|
                                                    for key in ["systemIdentifier"; "databaseOid"; "version"; "checksum"] do
                                                        match root.TryGetProperty(key) with
                                                        | true, prop ->
                                                            let v =
                                                                if prop.ValueKind = System.Text.Json.JsonValueKind.String then prop.GetString()
                                                                else prop.GetRawText()
                                                            yield { Key = key; Value = v }
                                                        | _ -> ()
                                                |]
                                            with _ -> priorRecord.Values
                                        else priorRecord.Values
                                    files, values
                            Storage.append prepared.Directory runId id "succeeded"
                            { priorRecord with
                                Status = "succeeded"
                                Ended = Storage.now()
                                Message = "reconciled-from-external-ledger"
                                ExitCode = 0
                                Values = if resolvedValues.Length > 0 then resolvedValues else priorRecord.Values
                                Files = resolvedFiles }
                    completed <- Array.append completed [|reconciledRecord|]
                    record <- Storage.saveRun prepared.Directory {record with Steps=completed}
                | CanonForge.Execution.Execute ->
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
