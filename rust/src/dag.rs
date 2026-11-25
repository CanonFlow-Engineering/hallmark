//! Pillar 1: Stage-Typed 6-Stage Lifecycle DAG & Release Ancestor Coverage Theorem (CF-001, CF-083).

use std::collections::{BTreeMap, BTreeSet};

use serde::{Deserialize, Serialize};

pub const MAX_STAGES: usize = 256;

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum StageKind {
    Build,
    Install,
    Configure,
    Migrate,
    Verify,
    Release,
}

impl StageKind {
    /// Minimum lifecycle phase order used to prevent build stages from consuming runtime outputs (CF-083).
    #[must_use]
    pub const fn phase_rank(self) -> u8 {
        match self {
            Self::Build => 1,
            Self::Install | Self::Configure | Self::Migrate => 2,
            Self::Verify => 3,
            Self::Release => 4,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum EffectKind {
    Observation,
    DesiredState,
    Imperative,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum Repetition {
    Reconcile,
    ExecuteOnce,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct OutputBinding {
    pub producer_stage: String,
    pub output_name: String,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct StageDraft {
    pub id: String,
    pub kind: StageKind,
    pub effect: EffectKind,
    pub repetition: Repetition,
    pub adapter_digest: String,
    pub action_digest: String,
    pub depends_on: Vec<String>,
    pub consumed_outputs: Vec<OutputBinding>,
    pub declared_outputs: Vec<String>,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WorkflowDraft {
    pub schema_version: u8,
    pub name: String,
    pub producer_digest: String,
    pub target_digest: String,
    pub configuration_digest: String,
    pub policy_digest: String,
    pub stages: Vec<StageDraft>,
}

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum DagError {
    UnsupportedSchema(u8),
    EmptyWorkflow,
    TooManyStages(usize),
    InvalidIdentifier(String),
    InvalidDigest(String),
    DuplicateStage(String),
    MissingDependency {
        stage: String,
        dependency: String,
    },
    DuplicateDependency {
        stage: String,
        dependency: String,
    },
    UnsafeRepetition(String),
    VerificationMustObserve(String),
    UndeclaredOutputConsumed {
        consumer: String,
        producer: String,
        output_name: String,
    },
    OutputProducerNotInDependencies {
        consumer: String,
        producer: String,
    },
    ForbiddenStageOutputConsumption {
        consumer: String,
        consumer_kind: StageKind,
        producer: String,
        producer_kind: StageKind,
        output_name: String,
    },
    CyclicDependencies(Vec<String>),
    ReleaseNeedsVerification(String),
    ReleaseVerificationGap {
        release_stage: String,
        unverified_ancestors: Vec<String>,
    },
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ValidatedWorkflow {
    pub ordered_stage_ids: Vec<String>,
    pub ancestors_by_stage: BTreeMap<String, BTreeSet<String>>,
}

#[must_use]
pub fn is_valid_identifier(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 64
        && value.as_bytes().first().is_some_and(u8::is_ascii_lowercase)
        && value
            .bytes()
            .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-')
}

#[must_use]
pub fn is_valid_sha256_digest(value: &str) -> bool {
    let Some(hex) = value.strip_prefix("sha256:") else {
        return false;
    };
    hex.len() == 64
        && hex
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
}

/// Validates the 6-stage workflow graph, stage-typed output bindings, and Release Ancestor Coverage Theorem.
///
/// # Errors
///
/// Returns every detected structural, typing, cycle, or release-coverage violation in deterministic order.
pub fn validate_workflow(draft: &WorkflowDraft) -> Result<ValidatedWorkflow, Vec<DagError>> {
    let mut errors = BTreeSet::new();
    validate_header(draft, &mut errors);
    let by_id = validate_stages(&draft.stages, &mut errors);
    validate_edges(&draft.stages, &by_id, &mut errors);

    if !errors.is_empty() {
        return Err(errors.into_iter().collect());
    }

    let ordered_stage_ids = match topological_sort(&draft.stages) {
        Ok(order) => order,
        Err(cycle) => return Err(vec![DagError::CyclicDependencies(cycle)]),
    };

    let ancestors_by_stage = compute_ancestors(&ordered_stage_ids, &by_id);
    validate_release_coverage(&draft.stages, &by_id, &ancestors_by_stage, &mut errors);

    if !errors.is_empty() {
        return Err(errors.into_iter().collect());
    }

    Ok(ValidatedWorkflow {
        ordered_stage_ids,
        ancestors_by_stage,
    })
}

fn validate_header(draft: &WorkflowDraft, errors: &mut BTreeSet<DagError>) {
    if draft.schema_version != 1 {
        errors.insert(DagError::UnsupportedSchema(draft.schema_version));
    }
    if draft.stages.is_empty() {
        errors.insert(DagError::EmptyWorkflow);
    }
    if draft.stages.len() > MAX_STAGES {
        errors.insert(DagError::TooManyStages(draft.stages.len()));
    }
    if !is_valid_identifier(&draft.name) {
        errors.insert(DagError::InvalidIdentifier("name".to_owned()));
    }
    for (field, digest) in [
        ("producer", &draft.producer_digest),
        ("target", &draft.target_digest),
        ("configuration", &draft.configuration_digest),
        ("policy", &draft.policy_digest),
    ] {
        if !is_valid_sha256_digest(digest) {
            errors.insert(DagError::InvalidDigest((*field).to_owned()));
        }
    }
}

fn validate_stages<'a>(
    stages: &'a [StageDraft],
    errors: &mut BTreeSet<DagError>,
) -> BTreeMap<&'a str, &'a StageDraft> {
    let mut by_id: BTreeMap<&'a str, &'a StageDraft> = BTreeMap::new();
    for stage in stages {
        if !is_valid_identifier(&stage.id) {
            errors.insert(DagError::InvalidIdentifier(format!("stage:{}", stage.id)));
        }
        if by_id.insert(stage.id.as_str(), stage).is_some() {
            errors.insert(DagError::DuplicateStage(stage.id.clone()));
        }
        if !is_valid_sha256_digest(&stage.adapter_digest) {
            errors.insert(DagError::InvalidDigest(format!("{}.adapter", stage.id)));
        }
        if !is_valid_sha256_digest(&stage.action_digest) {
            errors.insert(DagError::InvalidDigest(format!("{}.action", stage.id)));
        }
        if stage.effect == EffectKind::Imperative && stage.repetition == Repetition::Reconcile {
            errors.insert(DagError::UnsafeRepetition(stage.id.clone()));
        }
        if stage.kind == StageKind::Verify && stage.effect != EffectKind::Observation {
            errors.insert(DagError::VerificationMustObserve(stage.id.clone()));
        }
    }
    by_id
}

fn validate_edges(
    stages: &[StageDraft],
    by_id: &BTreeMap<&str, &StageDraft>,
    errors: &mut BTreeSet<DagError>,
) {
    for stage in stages {
        let mut seen_deps = BTreeSet::new();
        for dep in &stage.depends_on {
            if !seen_deps.insert(dep.as_str()) {
                errors.insert(DagError::DuplicateDependency {
                    stage: stage.id.clone(),
                    dependency: dep.clone(),
                });
            }
            if !by_id.contains_key(dep.as_str()) {
                errors.insert(DagError::MissingDependency {
                    stage: stage.id.clone(),
                    dependency: dep.clone(),
                });
            }
        }

        for binding in &stage.consumed_outputs {
            if !seen_deps.contains(binding.producer_stage.as_str()) {
                errors.insert(DagError::OutputProducerNotInDependencies {
                    consumer: stage.id.clone(),
                    producer: binding.producer_stage.clone(),
                });
            }
            if let Some(producer) = by_id.get(binding.producer_stage.as_str()) {
                if !producer
                    .declared_outputs
                    .iter()
                    .any(|out| out == &binding.output_name)
                {
                    errors.insert(DagError::UndeclaredOutputConsumed {
                        consumer: stage.id.clone(),
                        producer: producer.id.clone(),
                        output_name: binding.output_name.clone(),
                    });
                }
                if stage.kind.phase_rank() < producer.kind.phase_rank() {
                    errors.insert(DagError::ForbiddenStageOutputConsumption {
                        consumer: stage.id.clone(),
                        consumer_kind: stage.kind,
                        producer: producer.id.clone(),
                        producer_kind: producer.kind,
                        output_name: binding.output_name.clone(),
                    });
                }
            }
        }
    }
}

fn compute_ancestors(
    ordered_stage_ids: &[String],
    by_id: &BTreeMap<&str, &StageDraft>,
) -> BTreeMap<String, BTreeSet<String>> {
    let mut ancestors_by_stage: BTreeMap<String, BTreeSet<String>> = BTreeMap::new();
    for stage_id in ordered_stage_ids {
        if let Some(stage) = by_id.get(stage_id.as_str()) {
            let mut trans = BTreeSet::new();
            for dep in &stage.depends_on {
                trans.insert(dep.clone());
                if let Some(dep_anc) = ancestors_by_stage.get(dep.as_str()) {
                    for item in dep_anc {
                        trans.insert(item.clone());
                    }
                }
            }
            ancestors_by_stage.insert(stage_id.clone(), trans);
        }
    }
    ancestors_by_stage
}

fn validate_release_coverage(
    stages: &[StageDraft],
    by_id: &BTreeMap<&str, &StageDraft>,
    ancestors_by_stage: &BTreeMap<String, BTreeSet<String>>,
    errors: &mut BTreeSet<DagError>,
) {
    for stage in stages {
        if stage.kind == StageKind::Release
            && let Some(ancestors) = ancestors_by_stage.get(stage.id.as_str())
        {
            let verify_gates: Vec<&str> = ancestors
                .iter()
                .filter(|id| {
                    by_id
                        .get(id.as_str())
                        .is_some_and(|s| s.kind == StageKind::Verify)
                })
                .map(String::as_str)
                .collect();
            if verify_gates.is_empty() {
                errors.insert(DagError::ReleaseNeedsVerification(stage.id.clone()));
            } else {
                let mut covered_by_verify = BTreeSet::new();
                for gate_id in &verify_gates {
                    covered_by_verify.insert((*gate_id).to_owned());
                    if let Some(gate_anc) = ancestors_by_stage.get(*gate_id) {
                        for item in gate_anc {
                            covered_by_verify.insert(item.clone());
                        }
                    }
                }
                let unverified: Vec<String> = ancestors
                    .iter()
                    .filter(|anc_id| {
                        by_id.get(anc_id.as_str()).is_some_and(|anc| {
                            anc.kind != StageKind::Release
                                && anc.effect != EffectKind::Observation
                                && !covered_by_verify.contains(anc_id.as_str())
                        })
                    })
                    .cloned()
                    .collect();
                if !unverified.is_empty() {
                    errors.insert(DagError::ReleaseVerificationGap {
                        release_stage: stage.id.clone(),
                        unverified_ancestors: unverified,
                    });
                }
            }
        }
    }
}

fn topological_sort(stages: &[StageDraft]) -> Result<Vec<String>, Vec<String>> {
    let mut completed: BTreeSet<&str> = BTreeSet::new();
    let mut remaining: Vec<&StageDraft> = stages.iter().collect();
    let mut ordered: Vec<String> = Vec::with_capacity(stages.len());

    while !remaining.is_empty() {
        let mut ready = Vec::new();
        let mut waiting = Vec::new();
        for stage in remaining {
            if stage
                .depends_on
                .iter()
                .all(|dep| completed.contains(dep.as_str()))
            {
                ready.push(stage);
            } else {
                waiting.push(stage);
            }
        }
        if ready.is_empty() {
            let mut cycle: Vec<String> = waiting.into_iter().map(|s| s.id.clone()).collect();
            cycle.sort();
            return Err(cycle);
        }
        ready.sort_by(|a, b| a.id.cmp(&b.id));
        for stage in ready {
            completed.insert(stage.id.as_str());
            ordered.push(stage.id.clone());
        }
        remaining = waiting;
    }

    Ok(ordered)
}
