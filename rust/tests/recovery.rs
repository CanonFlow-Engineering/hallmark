use hallmark_core::{
    LedgerObservation, LedgerOutcome, MigrationSubject, PendingMigration, RecoveryDecision,
    decide_recovery,
};

fn pending_migration() -> PendingMigration {
    PendingMigration {
        subject: MigrationSubject {
            plan_digest: "sha256:wallet-v2-plan".into(),
            target: "wallet-local".into(),
            operation_id: "wallet-upgrade-migration".into(),
            migration_id: "2".into(),
        },
        expected_checksum: "12345".into(),
    }
}

fn ledger_observation(pending: &PendingMigration, outcome: LedgerOutcome) -> LedgerObservation {
    LedgerObservation {
        subject: pending.subject.clone(),
        outcome,
    }
}

#[test]
fn a_committed_migration_with_the_expected_checksum_is_recorded_as_applied() {
    let pending = pending_migration();
    let observation = ledger_observation(
        &pending,
        LedgerOutcome::Applied {
            checksum: pending.expected_checksum.clone(),
        },
    );
    assert_eq!(
        decide_recovery(&pending, &observation),
        RecoveryDecision::RecordApplied
    );
    assert_eq!(
        decide_recovery(&pending, &observation),
        RecoveryDecision::RecordApplied
    );
}

#[test]
fn a_definitively_absent_migration_is_eligible_for_readmission() {
    let pending = pending_migration();
    let observation = ledger_observation(&pending, LedgerOutcome::DefinitivelyAbsent);
    assert_eq!(
        decide_recovery(&pending, &observation),
        RecoveryDecision::RetryEligible
    );
}

#[test]
fn an_unknown_outcome_waits_for_reconciliation() {
    let pending = pending_migration();
    let observation = ledger_observation(&pending, LedgerOutcome::Unknown);
    assert_eq!(
        decide_recovery(&pending, &observation),
        RecoveryDecision::AwaitReconciliation
    );
}

#[test]
fn an_applied_migration_with_a_different_checksum_is_rejected() {
    let pending = pending_migration();
    let observation = ledger_observation(
        &pending,
        LedgerOutcome::Applied {
            checksum: "67890".into(),
        },
    );
    assert_eq!(
        decide_recovery(&pending, &observation),
        RecoveryDecision::RejectConflictingChecksum
    );
}

#[test]
fn observations_for_another_plan_target_operation_or_migration_are_rejected() {
    let pending = pending_migration();
    let subject = &pending.subject;
    let other_subjects = [
        MigrationSubject {
            plan_digest: "sha256:different-plan".into(),
            ..subject.clone()
        },
        MigrationSubject {
            target: "other-target".into(),
            ..subject.clone()
        },
        MigrationSubject {
            operation_id: "other-operation".into(),
            ..subject.clone()
        },
        MigrationSubject {
            migration_id: "3".into(),
            ..subject.clone()
        },
    ];
    for subject in other_subjects {
        let observation = LedgerObservation {
            subject,
            outcome: LedgerOutcome::DefinitivelyAbsent,
        };
        assert_eq!(
            decide_recovery(&pending, &observation),
            RecoveryDecision::RejectUnrelatedObservation
        );
    }
}
