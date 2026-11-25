namespace CanonForge.Execution

open CanonForge.Lab

type SecretRef = { Name: string; Prefix: string }
type EnvironmentValue = Literal of string | Secret of SecretRef
type RetrySafety = Manual | Reconcile | ExternalLedger
type ProcessSpec = {
    Executable: string
    Arguments: string list
    Directory: string
    Environment: Map<string, EnvironmentValue>
    StandardInputFile: string option
    ExpectedExit: int
}
type HttpSpec = { Url: string; Method: string; Body: string; Headers: Map<string, EnvironmentValue>; Status: int; Contains: string }
type PostgresMigrateSpec = {
    ContainerEngine: string
    ContainerId: string
    DatabaseName: string
    User: string
    Migrations: string list
    PasswordSecret: SecretRef option
}
type Operation =
    | Process of ProcessSpec
    | WriteFile of path: string * content: string
    | Capture of path: string
    | ComposeUp of file: string * project: string * environment: Map<string, EnvironmentValue>
    | PostgresMigrate of PostgresMigrateSpec
    | SealRelease of version: string
    | HttpCheck of HttpSpec

type Step = {
    Id: string
    Kind: StageKind
    Effect: EffectKind
    Repetition: Repetition
    Needs: string list
    Operation: Operation
    TimeoutSeconds: int
    MaxOutputBytes: int
    Outputs: string list
    RetrySafety: RetrySafety
}

type Workflow = {
    Name: string
    Context: string
    Inputs: string list
    AllowedExecutables: string list
    Steps: Step list
    CanonicalDefinition: string
}

type PriorOutcome = Completed | Failed | Interrupted
type ReuseDecision = Reuse | Execute | Refuse of string

type ExternalLedgerReadback =
    | Unobserved
    | CommittedInLedger of outputsValid: bool
    | AbsentFromLedger
    | ConflictingLedger of reason: string

module Recovery =
    let decideWithLedger sameIdentity outputsCurrent safety outcome readback =
        if not sameIdentity then Refuse "The plan, inputs, configuration, producer or secret version changed."
        else
            match outcome with
            | Completed when not outputsCurrent ->
                Refuse "A completed step's output changed; reconcile in a new run."
            | Completed ->
                match safety with
                | Manual | Reconcile -> Reuse
                | ExternalLedger ->
                    match readback with
                    | CommittedInLedger true -> Reuse
                    | CommittedInLedger false ->
                        Refuse "External ledger shows the operation committed, but declared outputs are missing or altered."
                    | AbsentFromLedger ->
                        Refuse "External ledger no longer records the completed operation; reconcile in a new run."
                    | ConflictingLedger reason ->
                        Refuse ("External ledger conflicts with the frozen operation: " + reason)
                    | Unobserved ->
                        Refuse "External ledger readback is required before reusing a completed ExternalLedger step."
            | Failed | Interrupted ->
                match safety with
                | Manual -> Refuse "An imperative effect may have committed; reconcile it before a fresh run."
                | Reconcile -> Execute
                | ExternalLedger ->
                    match readback with
                    | CommittedInLedger true -> Reuse
                    | CommittedInLedger false ->
                        Refuse "External ledger shows the operation committed, but declared outputs are missing or altered."
                    | AbsentFromLedger -> Execute
                    | ConflictingLedger reason ->
                        Refuse ("External ledger conflicts with the frozen operation: " + reason)
                    | Unobserved ->
                        Refuse "External ledger readback is required before replaying an interrupted or failed ExternalLedger step."

    let decide sameIdentity outputsCurrent safety outcome =
        decideWithLedger sameIdentity outputsCurrent safety outcome Unobserved
