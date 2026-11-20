namespace Hallmark.Execution

open Hallmark.Lab

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
type Operation =
    | Process of ProcessSpec
    | WriteFile of path: string * content: string
    | Capture of path: string
    | ComposeUp of file: string * project: string * environment: Map<string, EnvironmentValue>
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

module Recovery =
    let decide sameIdentity outputsCurrent safety outcome =
        if not sameIdentity then Refuse "The plan, inputs, configuration, producer or secret version changed."
        else
            match outcome with
            | Completed when outputsCurrent -> Reuse
            | Completed -> Refuse "A completed step's output changed; reconcile in a new run."
            | Failed | Interrupted ->
                match safety with
                | Manual -> Refuse "An imperative effect may have committed; reconcile it before a fresh run."
                | Reconcile | ExternalLedger -> Execute
