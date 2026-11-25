//! Pure lifecycle planning, gate admission, migration recovery, and release verification
//! decisions for Hallmark's functional core.

pub mod dag;
pub mod gates;
mod history;
pub mod release;

pub use history::{
    DatabaseObservation, ForwardEquivalenceOutcome, History, LedgerEntry, Migration,
    PendingMigrationV2, RecoveryDecisionV2, decide_recovery_v2, inspect_history,
    migration_checksum, verify_forward_equivalence,
};

use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct MigrationSubject {
    pub plan_digest: String,
    pub target: String,
    pub operation_id: String,
    pub migration_id: String,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PendingMigration {
    pub subject: MigrationSubject,
    pub expected_checksum: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum LedgerOutcome {
    Applied {
        checksum: String,
    },
    /// The adapter established that the earlier attempt ended without committing.
    DefinitivelyAbsent,
    Unknown,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LedgerObservation {
    pub subject: MigrationSubject,
    pub outcome: LedgerOutcome,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RecoveryDecision {
    RecordApplied,
    /// An executor must still check current admission before scheduling a retry.
    RetryEligible,
    AwaitReconciliation,
    RejectUnrelatedObservation,
    RejectConflictingChecksum,
}

/// Interpret a ledger observation for the pending operation.
#[must_use]
pub fn decide_recovery(
    pending: &PendingMigration,
    observed: &LedgerObservation,
) -> RecoveryDecision {
    if observed.subject != pending.subject {
        return RecoveryDecision::RejectUnrelatedObservation;
    }

    match &observed.outcome {
        LedgerOutcome::Applied { checksum } if checksum == &pending.expected_checksum => {
            RecoveryDecision::RecordApplied
        }
        LedgerOutcome::Applied { .. } => RecoveryDecision::RejectConflictingChecksum,
        LedgerOutcome::DefinitivelyAbsent => RecoveryDecision::RetryEligible,
        LedgerOutcome::Unknown => RecoveryDecision::AwaitReconciliation,
    }
}
