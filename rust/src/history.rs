use serde::{Deserialize, Serialize};

use crate::MigrationSubject;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Migration {
    pub version: u32,
    pub script: String,
    pub checksum: i32,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct LedgerEntry {
    pub version: Option<String>,
    pub script: String,
    pub kind: String,
    pub checksum: Option<i32>,
    pub success: bool,
}

impl LedgerEntry {
    fn same_script(&self, migration: &Migration) -> bool {
        self.version.as_deref() == Some(migration.version.to_string().as_str())
            && self.script == migration.script
            && self.kind == "SQL"
            && self.success
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum History {
    ExpectedPredecessor,
    CurrentMigration { checksum: i32 },
    Diverged,
}

/// Compare a complete local Flyway history to the frozen migrations for one upgrade.
#[must_use]
pub fn inspect_history(
    preceding: &[Migration],
    current: &Migration,
    observed: &[LedgerEntry],
) -> History {
    let Some((prefix, rest)) = observed.split_at_checked(preceding.len()) else {
        return History::Diverged;
    };
    let prior_agrees = prefix.iter().zip(preceding).all(|(row, migration)| {
        row.same_script(migration) && row.checksum == Some(migration.checksum)
    });
    if !prior_agrees {
        return History::Diverged;
    }

    match rest {
        [] => History::ExpectedPredecessor,
        [row] if row.same_script(current) => {
            row.checksum
                .map_or(History::Diverged, |checksum| History::CurrentMigration {
                    checksum,
                })
        }
        _ => History::Diverged,
    }
}

/// Flyway hashes UTF-8 SQL without its first BOM or line terminators.
#[must_use]
pub fn migration_checksum(sql: &str) -> i32 {
    let sql = sql.strip_prefix('\u{feff}').unwrap_or(sql);
    let mut checksum = crc32fast::Hasher::new();
    for line in sql.split(['\r', '\n']) {
        checksum.update(line.as_bytes());
    }
    i32::from_ne_bytes(checksum.finalize().to_ne_bytes())
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PendingMigrationV2 {
    pub subject: MigrationSubject,
    pub expected_checksum: i32,
    pub expected_pre_catalog_digest: String,
    pub expected_post_catalog_digest: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DatabaseObservation {
    pub subject: MigrationSubject,
    pub history_state: History,
    /// Canonical `pg_catalog` schema digest of tables, columns, constraints, and indexes (CF-046).
    pub observed_catalog_digest: String,
    /// Number of active backend queries or Flyway containers currently executing against the target.
    pub active_migration_sessions: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RecoveryDecisionV2 {
    /// Ledger has the exact `V_n` checksum AND live `pg_catalog` matches expected post-upgrade schema.
    RecordApplied,
    /// Ledger is at `V_{n-1}`, zero active sessions exist, and live `pg_catalog` is untouched at `V_{n-1}`.
    /// Provably safe to re-admit without double-effect risk.
    DefinitivelyAbsentRetryEligible,
    /// Ledger is at `V_{n-1}` while a migration session is still active in PostgreSQL / Podman.
    AwaitInFlightSession,
    /// Ledger is at `V_{n-1}` with zero sessions, but live `pg_catalog` drifted from `V_{n-1}`.
    HoldPredecessorSchemaDrift,
    /// Ledger shows `V_n` committed with expected checksum, but live `pg_catalog` differs from expected `V_n` schema.
    HoldPostMigrationSchemaDrift,
    /// Ledger shows `V_n` with a different CRC32 checksum.
    RejectConflictingChecksum,
    /// Ledger history prefix diverged or contains failed rows.
    RejectDivergedHistory,
    /// Observation belongs to a different plan, target, operation, or migration ID.
    RejectUnrelatedObservation,
}

/// Session-aware and schema-drift-aware recovery reducer closing the dead-code `DefinitivelyAbsent` gap.
#[must_use]
pub fn decide_recovery_v2(
    pending: &PendingMigrationV2,
    observed: &DatabaseObservation,
) -> RecoveryDecisionV2 {
    if observed.subject != pending.subject {
        return RecoveryDecisionV2::RejectUnrelatedObservation;
    }

    match observed.history_state {
        History::CurrentMigration { checksum } if checksum == pending.expected_checksum => {
            if observed.observed_catalog_digest == pending.expected_post_catalog_digest {
                RecoveryDecisionV2::RecordApplied
            } else {
                RecoveryDecisionV2::HoldPostMigrationSchemaDrift
            }
        }
        History::CurrentMigration { .. } => RecoveryDecisionV2::RejectConflictingChecksum,
        History::ExpectedPredecessor => {
            if observed.active_migration_sessions > 0 {
                RecoveryDecisionV2::AwaitInFlightSession
            } else if observed.observed_catalog_digest == pending.expected_pre_catalog_digest {
                RecoveryDecisionV2::DefinitivelyAbsentRetryEligible
            } else {
                RecoveryDecisionV2::HoldPredecessorSchemaDrift
            }
        }
        History::Diverged => RecoveryDecisionV2::RejectDivergedHistory,
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ForwardEquivalenceOutcome {
    Equivalent {
        catalog_digest: String,
    },
    SchemaMismatch {
        fresh_catalog_digest: String,
        upgraded_catalog_digest: String,
    },
    UnverifiedEmptyDigest,
}

/// Verifies that a fresh baseline installation of `V_n` produces the exact same `pg_catalog`
/// schema digest as upgrading `V_1 -> ... -> V_n` on a populated fixture (CF-003, CF-004).
#[must_use]
pub fn verify_forward_equivalence(
    fresh_install_catalog_digest: &str,
    upgraded_catalog_digest: &str,
) -> ForwardEquivalenceOutcome {
    if fresh_install_catalog_digest.is_empty() || upgraded_catalog_digest.is_empty() {
        return ForwardEquivalenceOutcome::UnverifiedEmptyDigest;
    }
    if fresh_install_catalog_digest == upgraded_catalog_digest {
        ForwardEquivalenceOutcome::Equivalent {
            catalog_digest: fresh_install_catalog_digest.to_owned(),
        }
    } else {
        ForwardEquivalenceOutcome::SchemaMismatch {
            fresh_catalog_digest: fresh_install_catalog_digest.to_owned(),
            upgraded_catalog_digest: upgraded_catalog_digest.to_owned(),
        }
    }
}
