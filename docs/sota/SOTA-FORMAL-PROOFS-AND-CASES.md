# Hallmark SOTA Formal Proofs, Worked Cases & Verification Evidence

## 1. Mathematical & State-Machine Proofs

### Proof 1 — The Release Ancestor Coverage Theorem ([`rust/src/dag.rs`](../../rust/src/dag.rs))

**Problem**: In traditional CI/CD pipelines (GitHub Actions, GitLab CI, Jenkins), a pipeline author can add a new `Migrate` or `Configure` step and wire it to `Release` while accidentally bypassing the `Verify` gate stage.

**Formal Invariant**:
Let $G = (V, E)$ be a finite directed acyclic graph of stages where each vertex $v \in V$ has:
* $\text{kind}(v) \in \{\text{Build}, \text{Install}, \text{Configure}, \text{Migrate}, \text{Verify}, \text{Release}\}$
* $\text{effect}(v) \in \{\text{Observation}, \text{DesiredState}, \text{Imperative}\}$

For any stage $u \in V$, let $\text{Anc}(u) = \{ w \in V \mid w \to^+ u \}$ be the nonempty transitive closure of its dependencies.
For every release stage $r \in V$ with $\text{kind}(r) = \text{Release}$, define its verification gate set:
$$\mathcal{V}(r) = \{ g \in \text{Anc}(r) \mid \text{kind}(g) = \text{Verify} \}$$
and its verified coverage closure:
$$\mathcal{C}(r) = \mathcal{V}(r) \cup \bigcup_{g \in \mathcal{V}(r)} \text{Anc}(g)$$

**Theorem (enforced by `validate_release_coverage` in [`rust/src/dag.rs`](../../rust/src/dag.rs))**:
A workflow $G$ is admissible only if for every $r \in V$ with $\text{kind}(r) = \text{Release}$:
1. $\mathcal{V}(r) \neq \emptyset$ (`DagError::ReleaseNeedsVerification`), and
2. Every mutating ancestor $m \in \text{Anc}(r)$ satisfying $\text{kind}(m) \neq \text{Release}$ and $\text{effect}(m) \neq \text{Observation}$ satisfies $m \in \mathcal{C}(r)$ (`DagError::ReleaseVerificationGap`).

**Executable Proof Case ([`rust/tests/sota_lifecycle.rs`](../../rust/tests/sota_lifecycle.rs))**:
* **Positive Case**: `build-app -> install-db -> configure-app -> migrate-schema -> verify-gates -> release-bundle` passes validation and topologically sorts deterministically even when authored in reverse order.
* **Negative Control**: Adding `unverified-backfill` (`StageKind::Migrate`, `EffectKind::Imperative`) depending on `configure-app` and wired directly into `release-bundle` (bypassing `verify-gates`) is rejected before any effect runs with:
  ```text
  Err([ReleaseVerificationGap {
      release_stage: "release-bundle",
      unverified_ancestors: ["unverified-backfill"]
  }])
  ```

---

### Proof 2 — Gate B Join-Semilattice & VERA Supersession Law ([`rust/src/gates.rs`](../../rust/src/gates.rs))

**Problem**: When multiple verifiers run (`RustAssay`, `FsAssay`, `CsAssay`, `PythonAssay`), one gate may report a real violation (`Fail`) while a second gate is missing or superseded in VERA (`Inconclusive`). Naive short-circuiting or treating `Inconclusive > Fail` hides real product defects behind missing evidence.

**Formal Invariant**:
Define the bounded join-semilattice $(\mathcal{O}, \sqcup)$ over outcomes $\mathcal{O} = \{\text{ToolFailure}, \text{Fail}, \text{Inconclusive}, \text{Pass}\}$ ordered by rank:
$$\text{rank}(\text{ToolFailure}) = 1 < \text{rank}(\text{Fail}) = 2 < \text{rank}(\text{Inconclusive}) = 3 < \text{rank}(\text{Pass}) = 4$$
with $a \sqcup b = \min_{\text{rank}}(a, b)$, identity element $\text{Pass}$, and absorbing element $\text{ToolFailure}$.
Track an independent booleancompleteness flag $\text{incomplete} \in \{\text{true}, \text{false}\}$.
Then:
$$\text{authoritative} = (\text{outcome} \in \{\text{Pass}, \text{Fail}\}) \land \neg\text{incomplete}$$

**Executable Proof Cases ([`rust/tests/sota_lifecycle.rs`](../../rust/tests/sota_lifecycle.rs))**:
1. **Authoritative Pass**: Both `rust-assay-core` and `fs-assay-wallet` return `Pass` with matching commit, tree, producer digest, policy digest, scope, `scanned_items > 0`, and `VeraDisposition::Active` $\implies$ `outcome = Pass, authoritative = true`.
2. **VERA Correction Supersession (`CF-077`)**: `fs-assay-wallet` receipt is marked `VeraDisposition::SupersededBy { replacement_digest }` $\implies$ `outcome = Inconclusive, authoritative = false, reasons = [EvidenceSuperseded { ... }]`.
3. **Concurrent Fail + Missing Evidence (Gate B Law)**: `rust-assay-core` returns `Fail` while `fs-assay-wallet` is missing $\implies$ `outcome = Fail` (never downgraded to `Inconclusive`!), `authoritative = false`, and **both** `GateReportedFail("rust-assay-core")` and `MissingRequiredGate("fs-assay-wallet")` are retained in `reasons`.

---

### Proof 3 — Closing the `DefinitivelyAbsent` Dead-Code Branch & Detecting Out-of-Band Schema Drift ([`rust/src/history.rs`](../../rust/src/history.rs))

**Problem in v0.1 Pilot**:
In [`rust/cli/src/main.rs`](../../rust/cli/src/main.rs#L194-L219), `settle()` mapped `History::ExpectedPredecessor` unconditionally to `LedgerOutcome::Unknown`, making `LedgerOutcome::DefinitivelyAbsent` and `RecoveryDecision::RetryEligible` dead code in production runs. Furthermore, `READ_LEDGER` in [`rust/cli/src/postgres.rs`](../../rust/cli/src/postgres.rs#L19-L32) only read `public.flyway_schema_history`, missing out-of-band DDL changes (`CF-046`) and active background sessions.

**SOTA Truth Table (`decide_recovery_v2` in [`rust/src/history.rs`](../../rust/src/history.rs))**:

| Flyway `history_state` | `active_migration_sessions` | `observed_catalog_digest` | `RecoveryDecisionV2` |
|---|---:|---|---|
| `CurrentMigration { checksum == expected }` | any | `== expected_post_catalog_digest` | **`RecordApplied`** |
| `CurrentMigration { checksum == expected }` | any | `!= expected_post_catalog_digest` | **`HoldPostMigrationSchemaDrift`** (catches out-of-band DDL drop/alter!) |
| `CurrentMigration { checksum != expected }` | any | any | **`RejectConflictingChecksum`** |
| `ExpectedPredecessor` | `> 0` | any | **`AwaitInFlightSession`** (Flyway query still active in Postgres!) |
| `ExpectedPredecessor` | `== 0` | `== expected_pre_catalog_digest` | **`DefinitivelyAbsentRetryEligible`** (provably safe to retry!) |
| `ExpectedPredecessor` | `== 0` | `!= expected_pre_catalog_digest` | **`HoldPredecessorSchemaDrift`** (partial non-transactional DDL!) |
| `Diverged` | any | any | **`RejectDivergedHistory`** |

#### Production SQL Query for `READ_LEDGER_V2` (Drop-in Upgrade for `rust/cli/src/postgres.rs`)
To supply `observed_catalog_digest` and `active_migration_sessions` in a single atomic read-only `psql` round-trip without adding any external crates:

```sql
SELECT pg_catalog.json_build_object(
    'identity', pg_catalog.json_build_object(
        'systemIdentifier', control.system_identifier::text,
        'databaseOid', db.oid::bigint
    ),
    'activeMigrationSessions', (
        SELECT count(*)::int
        FROM pg_catalog.pg_stat_activity
        WHERE datname = pg_catalog.current_database()
          AND pid <> pg_catalog.pg_backend_pid()
          AND state <> 'idle'
          AND (application_name ILIKE '%flyway%' OR query ILIKE '%flyway_schema_history%')
    ),
    'catalogSnapshot', (
        SELECT COALESCE(pg_catalog.json_agg(obj ORDER BY obj->>'kind', obj->>'name'), '[]'::json)
        FROM (
            SELECT pg_catalog.json_build_object(
                'kind', 'column',
                'name', c.relname || '.' || a.attname,
                'type', pg_catalog.format_type(a.atttypid, a.atttypmod),
                'notNull', a.attnotnull
            ) AS obj
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid
            WHERE n.nspname = 'public'
              AND c.relkind = 'r'
              AND c.relname <> 'flyway_schema_history'
              AND a.attnum > 0
              AND NOT a.attisdropped
            UNION ALL
            SELECT pg_catalog.json_build_object(
                'kind', 'index',
                'name', c.relname,
                'def', pg_catalog.pg_get_indexdef(c.oid)
            ) AS obj
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind = 'i'
              AND c.relname NOT LIKE 'flyway_schema_history%'
        ) AS schema_items
    ),
    'ledger', (
        SELECT COALESCE(pg_catalog.json_agg(pg_catalog.json_build_object(
            'version', version, 'script', script, 'kind', type,
            'checksum', checksum, 'success', success
        ) ORDER BY installed_rank), '[]'::json)
        FROM public.flyway_schema_history
    )
)
FROM pg_catalog.pg_control_system() AS control
CROSS JOIN pg_catalog.pg_database AS db
WHERE db.datname = pg_catalog.current_database();
```
Hashing the canonical `catalogSnapshot` JSON in `postgres.rs` with `sha2::Sha256` produces `observed_catalog_digest` (`sha256:...`), enabling both live drift detection (`CF-046`) and fresh-vs-upgrade forward equivalence (`CF-003`, `CF-004`) with **zero additional dependencies**!

---

## 2. Worked End-to-End Sample: Dhall & JSON Lowering to `WorkflowDraft` (`CF-072`)

Rather than embedding a heavy Dhall interpreter inside the Rust binary, Hallmark v1.0 defines a strict **Canonical JSON Intermediate Representation (`WorkflowDraft` in [`rust/src/dag.rs`](../../rust/src/dag.rs))**. Teams authoring in Dhall evaluate offline via `dhall-to-json` into `WorkflowDraft`:

```json
{
  "schemaVersion": 1,
  "name": "wallet-reference-lifecycle",
  "producerDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "targetDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "configurationDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "policyDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "stages": [
    {
      "id": "build-app",
      "kind": "build",
      "effect": "imperative",
      "repetition": "executeOnce",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": [],
      "consumedOutputs": [],
      "declaredOutputs": ["artifact-digest"]
    },
    {
      "id": "install-db",
      "kind": "install",
      "effect": "desiredState",
      "repetition": "reconcile",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": ["build-app"],
      "consumedOutputs": [
        { "producerStage": "build-app", "outputName": "artifact-digest" }
      ],
      "declaredOutputs": ["db-endpoint"]
    },
    {
      "id": "configure-app",
      "kind": "configure",
      "effect": "desiredState",
      "repetition": "reconcile",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": ["install-db"],
      "consumedOutputs": [
        { "producerStage": "install-db", "outputName": "db-endpoint" }
      ],
      "declaredOutputs": []
    },
    {
      "id": "migrate-schema",
      "kind": "migrate",
      "effect": "imperative",
      "repetition": "executeOnce",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": ["configure-app"],
      "consumedOutputs": [],
      "declaredOutputs": []
    },
    {
      "id": "verify-gates",
      "kind": "verify",
      "effect": "observation",
      "repetition": "reconcile",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": ["migrate-schema"],
      "consumedOutputs": [],
      "declaredOutputs": []
    },
    {
      "id": "release-bundle",
      "kind": "release",
      "effect": "imperative",
      "repetition": "executeOnce",
      "adapterDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "actionDigest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "dependsOn": ["verify-gates"],
      "consumedOutputs": [],
      "declaredOutputs": []
    }
  ]
}
```
This JSON document is deserialized and validated by `hallmark_core::dag::validate_workflow` in [`rust/tests/sota_lifecycle.rs`](../../rust/tests/sota_lifecycle.rs).
