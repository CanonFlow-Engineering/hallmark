//! Pillar 2: Four-State Assay Gate Reducer & VERA Correction Resolution (CF-053, CF-074, CF-077).

use std::collections::{BTreeMap, BTreeSet};

use serde::{Deserialize, Serialize};

/// Four-state release verdict matching `CanonFlow`'s Authority Contract v1.
/// Precedence order (Gate B semilattice): `ToolFailure > Fail > Inconclusive > Pass`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum AssayVerdict {
    ToolFailure,
    Fail,
    Inconclusive,
    Pass,
}

impl AssayVerdict {
    #[must_use]
    pub const fn precedence_rank(self) -> u8 {
        match self {
            Self::ToolFailure => 1,
            Self::Fail => 2,
            Self::Inconclusive => 3,
            Self::Pass => 4,
        }
    }

    #[must_use]
    pub const fn join(self, other: Self) -> Self {
        if self.precedence_rank() <= other.precedence_rank() {
            self
        } else {
            other
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum GateDisposition {
    RequiredBlocking,
    Advisory,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub enum VeraDisposition {
    Active,
    SupersededBy { replacement_digest: String },
    Withdrawn { reason: String },
    StatusUnknown,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GateRequirement {
    pub gate_id: String,
    pub producer_id: String,
    pub expected_producer_digest: String,
    pub expected_policy_digest: String,
    pub expected_scope: String,
    pub disposition: GateDisposition,
    pub require_vera_freshness: bool,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GateReceipt {
    pub gate_id: String,
    pub receipt_digest: String,
    pub producer_id: String,
    pub observed_producer_digest: String,
    pub observed_policy_digest: String,
    pub candidate_commit: String,
    pub candidate_tree: String,
    pub declared_scope: String,
    pub verdict: AssayVerdict,
    pub scanned_items: u32,
    pub vera_disposition: VeraDisposition,
}

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum GateReason {
    MissingRequiredGate(String),
    DuplicateGateReceipt(String),
    CandidateIdentityMismatch {
        gate_id: String,
        expected_commit: String,
        observed_commit: String,
    },
    ProducerIdentityMismatch {
        gate_id: String,
        expected: String,
        observed: String,
    },
    PolicyIdentityMismatch {
        gate_id: String,
        expected: String,
        observed: String,
    },
    ScopeMismatch {
        gate_id: String,
        expected: String,
        observed: String,
    },
    VacuousZeroScan(String),
    EvidenceSuperseded {
        gate_id: String,
        replacement_digest: String,
    },
    EvidenceWithdrawn {
        gate_id: String,
        reason: String,
    },
    VeraFreshnessUnknown(String),
    GateReportedFail(String),
    GateReportedInconclusive(String),
    GateReportedToolFailure(String),
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GateEvaluation {
    pub outcome: AssayVerdict,
    pub authoritative: bool,
    pub reasons: Vec<GateReason>,
    pub advisory_notes: Vec<String>,
}

/// Evaluates all policy-configured gates against observed producer receipts and VERA dispositions.
#[must_use]
pub fn evaluate_gates(
    expected_commit: &str,
    expected_tree: &str,
    requirements: &[GateRequirement],
    receipts: &[GateReceipt],
) -> GateEvaluation {
    let mut state = EvaluationState::default();

    if requirements.is_empty() {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::MissingRequiredGate("<no-gates-configured>".to_owned()),
        );
    }

    let mut receipt_map: BTreeMap<&str, &GateReceipt> = BTreeMap::new();
    for receipt in receipts {
        if receipt_map
            .insert(receipt.gate_id.as_str(), receipt)
            .is_some()
        {
            state.mark_incomplete(
                AssayVerdict::ToolFailure,
                GateReason::DuplicateGateReceipt(receipt.gate_id.clone()),
            );
        }
    }

    for req in requirements {
        let Some(receipt) = receipt_map.get(req.gate_id.as_str()) else {
            if req.disposition == GateDisposition::RequiredBlocking {
                state.mark_incomplete(
                    AssayVerdict::Inconclusive,
                    GateReason::MissingRequiredGate(req.gate_id.clone()),
                );
            } else {
                state
                    .advisory_notes
                    .insert(format!("advisory gate '{}' was not run", req.gate_id));
            }
            continue;
        };

        if req.disposition == GateDisposition::Advisory {
            state.advisory_notes.insert(format!(
                "advisory gate '{}' reported {:?}",
                req.gate_id, receipt.verdict
            ));
            continue;
        }

        evaluate_blocking_gate(expected_commit, expected_tree, req, receipt, &mut state);
    }

    let authoritative = match state.outcome {
        AssayVerdict::Pass | AssayVerdict::Fail => !state.has_incompleteness,
        AssayVerdict::Inconclusive | AssayVerdict::ToolFailure => false,
    };

    GateEvaluation {
        outcome: state.outcome,
        authoritative,
        reasons: state.reasons.into_iter().collect(),
        advisory_notes: state.advisory_notes.into_iter().collect(),
    }
}

struct EvaluationState {
    outcome: AssayVerdict,
    has_incompleteness: bool,
    reasons: BTreeSet<GateReason>,
    advisory_notes: BTreeSet<String>,
}

impl Default for EvaluationState {
    fn default() -> Self {
        Self {
            outcome: AssayVerdict::Pass,
            has_incompleteness: false,
            reasons: BTreeSet::new(),
            advisory_notes: BTreeSet::new(),
        }
    }
}

impl EvaluationState {
    fn mark_incomplete(&mut self, verdict: AssayVerdict, reason: GateReason) {
        self.has_incompleteness = true;
        self.outcome = self.outcome.join(verdict);
        self.reasons.insert(reason);
    }
}

fn evaluate_blocking_gate(
    expected_commit: &str,
    expected_tree: &str,
    req: &GateRequirement,
    receipt: &GateReceipt,
    state: &mut EvaluationState,
) {
    match receipt.verdict {
        AssayVerdict::ToolFailure => {
            state.mark_incomplete(
                AssayVerdict::ToolFailure,
                GateReason::GateReportedToolFailure(req.gate_id.clone()),
            );
        }
        AssayVerdict::Fail => {
            state.outcome = state.outcome.join(AssayVerdict::Fail);
            state
                .reasons
                .insert(GateReason::GateReportedFail(req.gate_id.clone()));
        }
        AssayVerdict::Inconclusive => {
            state.mark_incomplete(
                AssayVerdict::Inconclusive,
                GateReason::GateReportedInconclusive(req.gate_id.clone()),
            );
        }
        AssayVerdict::Pass => {}
    }

    if receipt.candidate_commit != expected_commit || receipt.candidate_tree != expected_tree {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::CandidateIdentityMismatch {
                gate_id: req.gate_id.clone(),
                expected_commit: expected_commit.to_owned(),
                observed_commit: receipt.candidate_commit.clone(),
            },
        );
    }

    let same_producer_id = receipt.producer_id == req.producer_id;
    let same_producer_digest = receipt.observed_producer_digest == req.expected_producer_digest;
    if !same_producer_id || !same_producer_digest {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::ProducerIdentityMismatch {
                gate_id: req.gate_id.clone(),
                expected: req.expected_producer_digest.clone(),
                observed: receipt.observed_producer_digest.clone(),
            },
        );
    }

    if receipt.observed_policy_digest != req.expected_policy_digest {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::PolicyIdentityMismatch {
                gate_id: req.gate_id.clone(),
                expected: req.expected_policy_digest.clone(),
                observed: receipt.observed_policy_digest.clone(),
            },
        );
    }

    if receipt.declared_scope != req.expected_scope {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::ScopeMismatch {
                gate_id: req.gate_id.clone(),
                expected: req.expected_scope.clone(),
                observed: receipt.declared_scope.clone(),
            },
        );
    }

    if receipt.verdict == AssayVerdict::Pass && receipt.scanned_items == 0 {
        state.mark_incomplete(
            AssayVerdict::Inconclusive,
            GateReason::VacuousZeroScan(req.gate_id.clone()),
        );
    }

    match &receipt.vera_disposition {
        VeraDisposition::SupersededBy { replacement_digest } => {
            state.mark_incomplete(
                AssayVerdict::Inconclusive,
                GateReason::EvidenceSuperseded {
                    gate_id: req.gate_id.clone(),
                    replacement_digest: replacement_digest.clone(),
                },
            );
        }
        VeraDisposition::Withdrawn { reason } => {
            state.mark_incomplete(
                AssayVerdict::Inconclusive,
                GateReason::EvidenceWithdrawn {
                    gate_id: req.gate_id.clone(),
                    reason: reason.clone(),
                },
            );
        }
        VeraDisposition::StatusUnknown if req.require_vera_freshness => {
            state.mark_incomplete(
                AssayVerdict::Inconclusive,
                GateReason::VeraFreshnessUnknown(req.gate_id.clone()),
            );
        }
        VeraDisposition::Active | VeraDisposition::StatusUnknown => {}
    }
}
