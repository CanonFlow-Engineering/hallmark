use std::fs::File;
use std::io::{self, Read};
use std::path::{Component, Path};

use hallmark_core::{Migration, MigrationSubject, PendingMigration, migration_checksum};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};

use crate::Result;
use crate::postgres::{ContainerEngine, DatabaseIdentity, ProviderIdentity};

pub(crate) const MAX_DOCUMENT: u64 = 256 * 1024;
const MAX_SQL: u64 = 1024 * 1024;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct Database {
    pub(crate) container_id: String,
    pub(crate) name: String,
    pub(crate) user: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Manifest {
    schema_version: u8,
    #[serde(default)]
    container_engine: ContainerEngine,
    context: String,
    database: Database,
    migrations: Vec<String>,
}

#[derive(Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct Source {
    pub(crate) migration: Migration,
    file: String,
    sha256: String,
}

pub(crate) struct Inputs {
    context: String,
    pub(crate) container_engine: ContainerEngine,
    pub(crate) database: Database,
    pub(crate) migrations: Vec<Migration>,
    pub(crate) sql: Vec<Vec<u8>>,
    files: Vec<String>,
}

#[derive(Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct Plan {
    pub(crate) plan_digest: String,
    pub(crate) inputs: PlanInputs,
}

#[derive(Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct PlanInputs {
    schema_version: u8,
    context: String,
    database: Database,
    pub(crate) observed_database: DatabaseIdentity,
    provider: ProviderIdentity,
    producer_sha256: String,
    sources: Vec<Source>,
}

impl Plan {
    pub(crate) fn container_engine(&self) -> ContainerEngine {
        match &self.inputs.provider {
            ProviderIdentity::Docker { .. } => ContainerEngine::Docker,
            ProviderIdentity::Podman { .. } => ContainerEngine::Podman,
        }
    }

    pub(crate) fn pending_migration(&self) -> Result<PendingMigration> {
        let current = self.inputs.sources.last().ok_or("plan needs a migration")?;
        Ok(PendingMigration {
            subject: self.subject(&self.inputs.observed_database, current.migration.version),
            expected_checksum: current.migration.checksum.to_string(),
        })
    }

    pub(crate) fn subject(&self, observed: &DatabaseIdentity, version: u32) -> MigrationSubject {
        MigrationSubject {
            plan_digest: self.plan_digest.clone(),
            target: format!(
                "{}:{}:{}",
                self.inputs.database.container_id,
                observed.system_identifier,
                observed.database_oid
            ),
            operation_id: "flyway-migrate".to_owned(),
            migration_id: version.to_string(),
        }
    }
}

fn is_identifier(value: &str, separator: u8, limit: usize) -> bool {
    value.len() <= limit
        && value.as_bytes().first().is_some_and(u8::is_ascii_lowercase)
        && value
            .bytes()
            .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == separator)
}

pub(crate) fn read_inputs(path: &Path) -> Result<Inputs> {
    let manifest: Manifest = serde_json::from_slice(&read_bytes(path, MAX_DOCUMENT)?)?;
    if manifest.schema_version != 1 {
        return Err("the Rust migration manifest requires schemaVersion 1".into());
    }
    if !is_identifier(&manifest.context, b'-', 64)
        || !is_identifier(&manifest.database.name, b'_', 63)
        || !is_identifier(&manifest.database.user, b'_', 63)
        || manifest.database.container_id.len() != 64
        || !manifest
            .database
            .container_id
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
    {
        return Err("invalid context, database identifier or full container ID".into());
    }
    if !(2..=16).contains(&manifest.migrations.len()) {
        return Err(
            "list two to sixteen SQL migrations from V1 through the desired version".into(),
        );
    }

    let root = path
        .canonicalize()?
        .parent()
        .ok_or("manifest needs a parent")?
        .to_path_buf();
    let mut migrations = Vec::new();
    let mut sql = Vec::new();
    for (index, file) in manifest.migrations.iter().enumerate() {
        let version = u32::try_from(index)? + 1;
        let path = Path::new(file);
        if path
            .components()
            .any(|part| !matches!(part, Component::Normal(_)))
        {
            return Err("migration paths must be relative to the manifest directory".into());
        }
        let script = path
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or("missing migration filename")?;
        let description = script
            .strip_prefix(&format!("V{version}__"))
            .and_then(|name| name.strip_suffix(".sql"));
        if !description.is_some_and(|name| is_identifier(name, b'_', 96)) {
            return Err(format!("expected V{version}__description.sql, got {script}").into());
        }
        let absolute = root.join(path).canonicalize()?;
        if !absolute.starts_with(&root) {
            return Err(format!("migration escapes the manifest directory: {file}").into());
        }
        let bytes = read_bytes(&absolute, MAX_SQL)?;
        let checksum = migration_checksum(std::str::from_utf8(&bytes)?);
        migrations.push(Migration {
            version,
            script: script.to_owned(),
            checksum,
        });
        sql.push(bytes);
    }

    Ok(Inputs {
        context: manifest.context,
        container_engine: manifest.container_engine,
        database: manifest.database,
        migrations,
        sql,
        files: manifest.migrations,
    })
}

impl Inputs {
    pub(crate) fn freeze(
        &self,
        observed_database: DatabaseIdentity,
        provider: ProviderIdentity,
    ) -> Result<Plan> {
        let sources = self
            .migrations
            .iter()
            .zip(self.files.iter().zip(&self.sql))
            .map(|(migration, (file, sql))| Source {
                migration: migration.clone(),
                file: file.clone(),
                sha256: sha256(sql),
            })
            .collect();
        let inputs = PlanInputs {
            schema_version: 1,
            context: self.context.clone(),
            database: self.database.clone(),
            observed_database,
            provider,
            producer_sha256: sha256_file(&std::env::current_exe()?)?,
            sources,
        };
        Ok(Plan {
            plan_digest: json_digest(&inputs)?,
            inputs,
        })
    }
}

pub(crate) fn read_bytes(path: &Path, maximum: u64) -> Result<Vec<u8>> {
    if !path.is_file() {
        return Err(format!("expected a regular file: {}", path.display()).into());
    }
    let mut bytes = Vec::new();
    File::open(path)?
        .take(maximum + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() as u64 > maximum {
        return Err(format!("document exceeds {maximum} bytes: {}", path.display()).into());
    }
    Ok(bytes)
}

pub(crate) fn sha256_file(path: &Path) -> io::Result<String> {
    let mut hash = Sha256::new();
    io::copy(&mut File::open(path)?, &mut hash)?;
    Ok(format!("{:x}", hash.finalize()))
}

fn sha256(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

pub(crate) fn json_digest(value: &impl Serialize) -> Result<String> {
    Ok(sha256(&serde_json::to_vec(value)?))
}
