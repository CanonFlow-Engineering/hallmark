use std::collections::{BTreeMap, BTreeSet};

use hallmark_core::dag::{
    DagError, EffectKind, OutputBinding, Repetition, StageDraft, StageKind, WorkflowDraft,
    validate_workflow,
};
use hallmark_core::gates::{
    AssayVerdict, GateDisposition, GateEvaluation, GateReason, GateReceipt, GateRequirement,
    VeraDisposition, evaluate_gates,
};
use hallmark_core::release::{
    ArtifactIdentity, BundleObservations, BundleVerificationError, DestinationAdmissionPolicy,
    ReleaseStamp, ReleaseVerificationVerdict, verify_release_bundle,
};
use hallmark_core::{
    DatabaseObservation, ForwardEquivalenceOutcome, History, LedgerEntry, Migration,
    MigrationSubject, PendingMigrationV2, RecoveryDecisionV2, decide_recovery_v2, inspect_history,
    migration_checksum, verify_forward_equivalence,
};

const VALID_DIGEST: &str =
    "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
const OTHER_DIGEST: &str =
    "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

fn stage(
    id: &str,
    kind: StageKind,
    effect: EffectKind,
    repetition: Repetition,
    depends_on: &[&str],
) -> StageDraft {
    StageDraft {
        id: id.to_owned(),
        kind,
        effect,
        repetition,
        adapter_digest: VALID_DIGEST.to_owned(),
        action_digest: VALID_DIGEST.to_owned(),
        depends_on: depends_on.iter().map(|s| (*s).to_owned()).collect(),
        consumed_outputs: Vec::new(),
        declared_outputs: Vec::new(),
    }
}

fn valid_six_stage_workflow() -> WorkflowDraft {
    let mut build = stage(
        "build-app",
        StageKind::Build,
        EffectKind::Imperative,
        Repetition::ExecuteOnce,
        &[],
    );
    build.declared_outputs.push("artifact-digest".to_owned());

    let mut install = stage(
        "install-db",
        StageKind::Install,
        EffectKind::DesiredState,
        Repetition::Reconcile,
        &["build-app"],
    );
    install.consumed_outputs.push(OutputBinding {
        producer_stage: "build-app".to_owned(),
        output_name: "artifact-digest".to_owned(),
    });
    install.declared_outputs.push("db-endpoint".to_owned());

    let mut configure = stage(
        "configure-app",
        StageKind::Configure,
        EffectKind::DesiredState,
        Repetition::Reconcile,
        &["install-db"],
    );
    configure.consumed_outputs.push(OutputBinding {
        producer_stage: "install-db".to_owned(),
        output_name: "db-endpoint".to_owned(),
    });

    let migrate = stage(
        "migrate-schema",
        StageKind::Migrate,
        EffectKind::Imperative,
        Repetition::ExecuteOnce,
        &["configure-app"],
    );
    let verify = stage(
        "verify-gates",
        StageKind::Verify,
        EffectKind::Observation,
        Repetition::Reconcile,
        &["migrate-schema"],
    );
    let release = stage(
        "release-bundle",
        StageKind::Release,
        EffectKind::Imperative,
        Repetition::ExecuteOnce,
        &["verify-gates"],
    );

    WorkflowDraft {
        schema_version: 1,
        name: "wallet-reference-lifecycle".to_owned(),
        producer_digest: VALID_DIGEST.to_owned(),
        target_digest: VALID_DIGEST.to_owned(),
        configuration_digest: VALID_DIGEST.to_owned(),
        policy_digest: VALID_DIGEST.to_owned(),
        stages: vec![release, verify, migrate, configure, install, build],
    }
}

#[test]
fn dag_orders_valid_six_stage_workflow_independent_of_authoring_order() {
    let draft = valid_six_stage_workflow();
    let validated = validate_workflow(&draft).expect("valid six-stage workflow");
    assert_eq!(
        validated.ordered_stage_ids,
        vec![
            "build-app",
            "install-db",
            "configure-app",
            "migrate-schema",
            "verify-gates",
            "release-bundle"
        ]
    );
}

#[test]
fn dag_enforces_release_ancestor_coverage_and_stage_typed_outputs() {
    let mut draft = valid_six_stage_workflow();
    let unverified_side_migration = stage(
        "unverified-backfill",
        StageKind::Migrate,
        EffectKind::Imperative,
        Repetition::ExecuteOnce,
        &["configure-app"],
    );
    draft.stages.push(unverified_side_migration);
    for s in &mut draft.stages {
        if s.id == "release-bundle" {
            s.depends_on.push("unverified-backfill".to_owned());
        }
    }
    let errors = validate_workflow(&draft).expect_err("unverified ancestor must block release");
    assert_eq!(
        errors,
        vec![DagError::ReleaseVerificationGap {
            release_stage: "release-bundle".to_owned(),
            unverified_ancestors: vec!["unverified-backfill".to_owned()],
        }]
    );

    let mut bad_stage_output = valid_six_stage_workflow();
    let late_build = StageDraft {
        id: "illegal-build".to_owned(),
        kind: StageKind::Build,
        effect: EffectKind::Imperative,
        repetition: Repetition::ExecuteOnce,
        adapter_digest: VALID_DIGEST.to_owned(),
        action_digest: VALID_DIGEST.to_owned(),
        depends_on: vec!["install-db".to_owned()],
        consumed_outputs: vec![OutputBinding {
            producer_stage: "install-db".to_owned(),
            output_name: "db-endpoint".to_owned(),
        }],
        declared_outputs: Vec::new(),
    };
    bad_stage_output.stages.push(late_build);
    let errors =
        validate_workflow(&bad_stage_output).expect_err("Build cannot consume Install output");
    assert!(errors.contains(&DagError::ForbiddenStageOutputConsumption {
        consumer: "illegal-build".to_owned(),
        consumer_kind: StageKind::Build,
        producer: "install-db".to_owned(),
        producer_kind: StageKind::Install,
        output_name: "db-endpoint".to_owned(),
    }));
}

#[test]
fn gate_reducer_preserves_gate_b_semilattice_and_vera_correction_refusal() {
    let req_rust = GateRequirement {
        gate_id: "rust-assay-core".to_owned(),
        producer_id: "rust-assay/0.1.0".to_owned(),
        expected_producer_digest: VALID_DIGEST.to_owned(),
        expected_policy_digest: VALID_DIGEST.to_owned(),
        expected_scope: "rust/src".to_owned(),
        disposition: GateDisposition::RequiredBlocking,
        require_vera_freshness: true,
    };
    let req_fs = GateRequirement {
        gate_id: "fs-assay-wallet".to_owned(),
        producer_id: "fs-assay/1.0.4".to_owned(),
        expected_producer_digest: VALID_DIGEST.to_owned(),
        expected_policy_digest: VALID_DIGEST.to_owned(),
        expected_scope: "Wallet.slnx".to_owned(),
        disposition: GateDisposition::RequiredBlocking,
        require_vera_freshness: true,
    };

    let clean_rust = GateReceipt {
        gate_id: "rust-assay-core".to_owned(),
        receipt_digest: VALID_DIGEST.to_owned(),
        producer_id: "rust-assay/0.1.0".to_owned(),
        observed_producer_digest: VALID_DIGEST.to_owned(),
        observed_policy_digest: VALID_DIGEST.to_owned(),
        candidate_commit: "commit-a".to_owned(),
        candidate_tree: "tree-a".to_owned(),
        declared_scope: "rust/src".to_owned(),
        verdict: AssayVerdict::Pass,
        scanned_items: 2,
        vera_disposition: VeraDisposition::Active,
    };
    let clean_fs = GateReceipt {
        gate_id: "fs-assay-wallet".to_owned(),
        receipt_digest: VALID_DIGEST.to_owned(),
        producer_id: "fs-assay/1.0.4".to_owned(),
        observed_producer_digest: VALID_DIGEST.to_owned(),
        observed_policy_digest: VALID_DIGEST.to_owned(),
        candidate_commit: "commit-a".to_owned(),
        candidate_tree: "tree-a".to_owned(),
        declared_scope: "Wallet.slnx".to_owned(),
        verdict: AssayVerdict::Pass,
        scanned_items: 22,
        vera_disposition: VeraDisposition::Active,
    };

    let eval = evaluate_gates(
        "commit-a",
        "tree-a",
        &[req_rust.clone(), req_fs.clone()],
        &[clean_rust.clone(), clean_fs.clone()],
    );
    assert_eq!(eval.outcome, AssayVerdict::Pass);
    assert!(eval.authoritative);

    let mut superseded_fs = clean_fs.clone();
    superseded_fs.vera_disposition = VeraDisposition::SupersededBy {
        replacement_digest: OTHER_DIGEST.to_owned(),
    };
    let eval = evaluate_gates(
        "commit-a",
        "tree-a",
        &[req_rust.clone(), req_fs.clone()],
        &[clean_rust, superseded_fs],
    );
    assert_eq!(eval.outcome, AssayVerdict::Inconclusive);
    assert!(!eval.authoritative);
    assert!(eval.reasons.contains(&GateReason::EvidenceSuperseded {
        gate_id: "fs-assay-wallet".to_owned(),
        replacement_digest: OTHER_DIGEST.to_owned(),
    }));

    let mut failing_rust = clean_fs;
    failing_rust.gate_id = "rust-assay-core".to_owned();
    failing_rust.producer_id = "rust-assay/0.1.0".to_owned();
    failing_rust.declared_scope = "rust/src".to_owned();
    failing_rust.verdict = AssayVerdict::Fail;
    let eval = evaluate_gates("commit-a", "tree-a", &[req_rust, req_fs], &[failing_rust]);
    assert_eq!(eval.outcome, AssayVerdict::Fail);
    assert!(!eval.authoritative);
    assert!(
        eval.reasons
            .contains(&GateReason::GateReportedFail("rust-assay-core".to_owned()))
    );
    assert!(eval.reasons.contains(&GateReason::MissingRequiredGate(
        "fs-assay-wallet".to_owned()
    )));
}

#[test]
fn migration_v2_distinguishes_in_flight_sessions_schema_drift_and_safe_retry() {
    let v1 = Migration {
        version: 1,
        script: "V1__wallet.sql".to_owned(),
        checksum: migration_checksum("CREATE TABLE wallets(id text PRIMARY KEY);"),
    };
    let v2 = Migration {
        version: 2,
        script: "V2__index_wallet_operations.sql".to_owned(),
        checksum: migration_checksum("CREATE INDEX operations_wallet_idx ON operations(wallet);"),
    };
    let row_v1 = LedgerEntry {
        version: Some("1".to_owned()),
        script: v1.script.clone(),
        kind: "SQL".to_owned(),
        checksum: Some(v1.checksum),
        success: true,
    };
    let row_v2 = LedgerEntry {
        version: Some("2".to_owned()),
        script: v2.script.clone(),
        kind: "SQL".to_owned(),
        checksum: Some(v2.checksum),
        success: true,
    };

    let subject = MigrationSubject {
        plan_digest: VALID_DIGEST.to_owned(),
        target: "container:sys:16384".to_owned(),
        operation_id: "flyway-migrate".to_owned(),
        migration_id: "2".to_owned(),
    };
    let pending = PendingMigrationV2 {
        subject: subject.clone(),
        expected_checksum: v2.checksum,
        expected_pre_catalog_digest: "sha256:catalog-v1".to_owned(),
        expected_post_catalog_digest: "sha256:catalog-v2".to_owned(),
    };

    let in_flight = DatabaseObservation {
        subject: subject.clone(),
        history_state: inspect_history(
            std::slice::from_ref(&v1),
            &v2,
            std::slice::from_ref(&row_v1),
        ),
        observed_catalog_digest: "sha256:catalog-v1".to_owned(),
        active_migration_sessions: 1,
    };
    assert_eq!(
        decide_recovery_v2(&pending, &in_flight),
        RecoveryDecisionV2::AwaitInFlightSession
    );

    let clean_absent = DatabaseObservation {
        active_migration_sessions: 0,
        ..in_flight
    };
    assert_eq!(
        decide_recovery_v2(&pending, &clean_absent),
        RecoveryDecisionV2::DefinitivelyAbsentRetryEligible
    );

    let committed_history = inspect_history(&[v1], &v2, &[row_v1, row_v2]);
    assert_eq!(
        committed_history,
        History::CurrentMigration {
            checksum: v2.checksum
        }
    );
    let drifted_post = DatabaseObservation {
        subject,
        history_state: committed_history,
        observed_catalog_digest: "sha256:catalog-v2-dropped-index".to_owned(),
        active_migration_sessions: 0,
    };
    assert_eq!(
        decide_recovery_v2(&pending, &drifted_post),
        RecoveryDecisionV2::HoldPostMigrationSchemaDrift
    );

    let valid_post = DatabaseObservation {
        observed_catalog_digest: "sha256:catalog-v2".to_owned(),
        ..drifted_post
    };
    assert_eq!(
        decide_recovery_v2(&pending, &valid_post),
        RecoveryDecisionV2::RecordApplied
    );

    assert_eq!(
        verify_forward_equivalence("sha256:catalog-v2", "sha256:catalog-v2"),
        ForwardEquivalenceOutcome::Equivalent {
            catalog_digest: "sha256:catalog-v2".to_owned()
        }
    );
    assert_eq!(
        verify_forward_equivalence("sha256:catalog-v2-fresh", "sha256:catalog-v2-upgraded"),
        ForwardEquivalenceOutcome::SchemaMismatch {
            fresh_catalog_digest: "sha256:catalog-v2-fresh".to_owned(),
            upgraded_catalog_digest: "sha256:catalog-v2-upgraded".to_owned(),
        }
    );
}

#[test]
fn release_bundle_separates_historical_signature_validity_from_current_target_admission() {
    let stamp = ReleaseStamp {
        schema_version: 1,
        release_name: "wallet-service".to_owned(),
        version: "1.2.0".to_owned(),
        plan_digest: VALID_DIGEST.to_owned(),
        run_id: VALID_DIGEST.to_owned(),
        target_identity: "podman-wallet-prod".to_owned(),
        policy_digest: VALID_DIGEST.to_owned(),
        artifacts: vec![ArtifactIdentity {
            path: "artifacts/wallet.tar.gz".to_owned(),
            sha256: VALID_DIGEST.to_owned(),
        }],
        attested_steps_digest: VALID_DIGEST.to_owned(),
        gate_receipts_digest: VALID_DIGEST.to_owned(),
        signer_public_key_id: "key-canonflow-release-2026".to_owned(),
        signature: "sig-ed25519-base64".to_owned(),
    };

    let mut bundled_artifacts = BTreeMap::new();
    bundled_artifacts.insert(
        "artifacts/wallet.tar.gz".to_owned(),
        VALID_DIGEST.to_owned(),
    );

    let bundle = BundleObservations {
        signature_cryptographically_valid: true,
        observed_attested_steps_digest: VALID_DIGEST.to_owned(),
        observed_gate_receipts_digest: VALID_DIGEST.to_owned(),
        bundled_artifacts,
        unexpected_archive_entries: Vec::new(),
    };

    let mut trusted_keys = BTreeSet::new();
    trusted_keys.insert("key-canonflow-release-2026".to_owned());

    let destination_ok = DestinationAdmissionPolicy {
        trusted_public_key_ids: trusted_keys,
        expected_target_identity: "podman-wallet-prod".to_owned(),
        current_policy_digest: VALID_DIGEST.to_owned(),
        current_gate_evaluation: GateEvaluation {
            outcome: AssayVerdict::Pass,
            authoritative: true,
            reasons: Vec::new(),
            advisory_notes: Vec::new(),
        },
    };

    let (verdict, errors) = verify_release_bundle(&stamp, &bundle, &destination_ok);
    assert_eq!(verdict, ReleaseVerificationVerdict::AdmittedForTarget);
    assert_eq!(errors, Vec::<BundleVerificationError>::new());

    let destination_rotated = DestinationAdmissionPolicy {
        current_policy_digest: OTHER_DIGEST.to_owned(),
        ..destination_ok
    };
    let (verdict, errors) = verify_release_bundle(&stamp, &bundle, &destination_rotated);
    assert_eq!(
        verdict,
        ReleaseVerificationVerdict::HistoricalStampValidButTargetRefused
    );
    assert_eq!(
        errors,
        vec![BundleVerificationError::PolicyChangedSinceStamp {
            stamped_policy: VALID_DIGEST.to_owned(),
            current_policy: OTHER_DIGEST.to_owned(),
        }]
    );
}
