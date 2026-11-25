//! Pillar 4: Portable Signed Release Stamp & Destination Target Admission Verifier (CF-028, CF-078).

use std::collections::{BTreeMap, BTreeSet};

use serde::{Deserialize, Serialize};

use crate::gates::{AssayVerdict, GateEvaluation};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ArtifactIdentity {
    pub path: String,
    pub sha256: String,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReleaseStamp {
    pub schema_version: u8,
    pub release_name: String,
    pub version: String,
    pub plan_digest: String,
    pub run_id: String,
    pub target_identity: String,
    pub policy_digest: String,
    pub artifacts: Vec<ArtifactIdentity>,
    pub attested_steps_digest: String,
    pub gate_receipts_digest: String,
    pub signer_public_key_id: String,
    pub signature: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BundleObservations {
    pub signature_cryptographically_valid: bool,
    pub observed_attested_steps_digest: String,
    pub observed_gate_receipts_digest: String,
    pub bundled_artifacts: BTreeMap<String, String>,
    pub unexpected_archive_entries: Vec<String>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DestinationAdmissionPolicy {
    pub trusted_public_key_ids: BTreeSet<String>,
    pub expected_target_identity: String,
    pub current_policy_digest: String,
    pub current_gate_evaluation: GateEvaluation,
}

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum BundleVerificationError {
    UnsupportedSchema(u8),
    UntrustedSigner(String),
    InvalidSignature,
    AttestedStepsDigestMismatch,
    GateReceiptsDigestMismatch,
    MissingBundledArtifact(String),
    CorruptedBundledArtifact {
        path: String,
        expected: String,
        observed: String,
    },
    UnexpectedArchiveEntries(Vec<String>),
    TargetIdentityMismatch {
        stamped_target: String,
        destination_target: String,
    },
    PolicyChangedSinceStamp {
        stamped_policy: String,
        current_policy: String,
    },
    CurrentGatesNotAuthoritativePass {
        outcome: AssayVerdict,
        authoritative: bool,
    },
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ReleaseVerificationVerdict {
    /// Portable bundle integrity and historical signature are valid, AND current destination admission is satisfied.
    AdmittedForTarget,
    /// Historical bundle signature and artifact bytes are intact, but destination target/policy/VERA freshness refused reuse for a new effect (CF-077, CF-078).
    HistoricalStampValidButTargetRefused,
    /// Bundle bytes, signature, or signer trust failed verification.
    InvalidBundle,
}

/// Verifies a portable release bundle both as a historical signed record and for current target admission.
#[must_use]
pub fn verify_release_bundle(
    stamp: &ReleaseStamp,
    bundle: &BundleObservations,
    destination: &DestinationAdmissionPolicy,
) -> (ReleaseVerificationVerdict, Vec<BundleVerificationError>) {
    let mut bundle_errors = BTreeSet::new();

    if stamp.schema_version != 1 {
        bundle_errors.insert(BundleVerificationError::UnsupportedSchema(
            stamp.schema_version,
        ));
    }
    if !destination
        .trusted_public_key_ids
        .contains(stamp.signer_public_key_id.as_str())
    {
        bundle_errors.insert(BundleVerificationError::UntrustedSigner(
            stamp.signer_public_key_id.clone(),
        ));
    }
    if !bundle.signature_cryptographically_valid {
        bundle_errors.insert(BundleVerificationError::InvalidSignature);
    }
    if bundle.observed_attested_steps_digest != stamp.attested_steps_digest {
        bundle_errors.insert(BundleVerificationError::AttestedStepsDigestMismatch);
    }
    if bundle.observed_gate_receipts_digest != stamp.gate_receipts_digest {
        bundle_errors.insert(BundleVerificationError::GateReceiptsDigestMismatch);
    }
    if !bundle.unexpected_archive_entries.is_empty() {
        bundle_errors.insert(BundleVerificationError::UnexpectedArchiveEntries(
            bundle.unexpected_archive_entries.clone(),
        ));
    }

    for artifact in &stamp.artifacts {
        match bundle.bundled_artifacts.get(artifact.path.as_str()) {
            None => {
                bundle_errors.insert(BundleVerificationError::MissingBundledArtifact(
                    artifact.path.clone(),
                ));
            }
            Some(observed_sha) if observed_sha != &artifact.sha256 => {
                bundle_errors.insert(BundleVerificationError::CorruptedBundledArtifact {
                    path: artifact.path.clone(),
                    expected: artifact.sha256.clone(),
                    observed: observed_sha.clone(),
                });
            }
            Some(_) => {}
        }
    }

    if !bundle_errors.is_empty() {
        return (
            ReleaseVerificationVerdict::InvalidBundle,
            bundle_errors.into_iter().collect(),
        );
    }

    let mut admission_errors = BTreeSet::new();
    if stamp.target_identity != destination.expected_target_identity {
        admission_errors.insert(BundleVerificationError::TargetIdentityMismatch {
            stamped_target: stamp.target_identity.clone(),
            destination_target: destination.expected_target_identity.clone(),
        });
    }
    if stamp.policy_digest != destination.current_policy_digest {
        admission_errors.insert(BundleVerificationError::PolicyChangedSinceStamp {
            stamped_policy: stamp.policy_digest.clone(),
            current_policy: destination.current_policy_digest.clone(),
        });
    }
    if destination.current_gate_evaluation.outcome != AssayVerdict::Pass
        || !destination.current_gate_evaluation.authoritative
    {
        admission_errors.insert(BundleVerificationError::CurrentGatesNotAuthoritativePass {
            outcome: destination.current_gate_evaluation.outcome,
            authoritative: destination.current_gate_evaluation.authoritative,
        });
    }

    if !admission_errors.is_empty() {
        return (
            ReleaseVerificationVerdict::HistoricalStampValidButTargetRefused,
            admission_errors.into_iter().collect(),
        );
    }

    (ReleaseVerificationVerdict::AdmittedForTarget, Vec::new())
}
