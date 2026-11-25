use std::fs::{File, OpenOptions, TryLockError};
use std::io::Write;
use std::os::unix::fs::{OpenOptionsExt, PermissionsExt};
use std::path::{Path, PathBuf};

use hallmark_core::{LedgerEntry, PendingMigration};
use serde::{Deserialize, Serialize};
use tempfile::NamedTempFile;

use crate::Result;
use crate::plan::{MAX_DOCUMENT, Plan, json_digest, read_bytes};

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct Run {
    pub(crate) schema_version: u8,
    #[serde(rename = "runId")]
    pub(crate) id: String,
    pub(crate) plan: Plan,
    pub(crate) operation: PendingMigration,
    pub(crate) result: RunResult,
}

#[derive(Serialize, Deserialize)]
#[serde(tag = "status", rename_all = "camelCase", deny_unknown_fields)]
pub(crate) enum RunResult {
    Pending,
    Applied { ledger: Vec<LedgerEntry> },
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct SavedRun {
    sha256: String,
    run: Run,
}

#[derive(Serialize)]
struct SaveRun<'a> {
    sha256: String,
    run: &'a Run,
}

pub(crate) struct Store {
    pub(crate) directory: PathBuf,
    record: PathBuf,
    _lock: File,
}

impl Store {
    pub(crate) fn lock(path: &Path) -> Result<Self> {
        let directory = path.canonicalize()?;
        let metadata = directory.metadata()?;
        if !metadata.is_dir() || metadata.permissions().mode() & 0o077 != 0 {
            return Err("state-dir must be an existing private directory (mode 0700)".into());
        }
        let lock = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
            .truncate(false)
            .mode(0o600)
            .open(directory.join("run.lock"))?;
        match lock.try_lock() {
            Ok(()) => {}
            Err(TryLockError::WouldBlock) => {
                return Err("state directory is locked by another Hallmark process".into());
            }
            Err(TryLockError::Error(error)) => return Err(error.into()),
        }
        let record = directory.join("run-v1.json");
        Ok(Self {
            directory,
            record,
            _lock: lock,
        })
    }

    pub(crate) fn ensure_new(&self) -> Result<()> {
        if self.record.try_exists()? {
            return Err("state directory already contains a run; use status or resume".into());
        }
        Ok(())
    }

    pub(crate) fn read(&self) -> Result<Run> {
        let saved: SavedRun = serde_json::from_slice(&read_bytes(&self.record, MAX_DOCUMENT)?)?;
        let run = saved.run;
        if run.schema_version != 1
            || run.id != run.plan.plan_digest
            || run.plan.plan_digest != json_digest(&run.plan.inputs)?
            || run.operation != run.plan.pending_migration()?
            || saved.sha256 != json_digest(&run)?
        {
            return Err("run record has an invalid schema or failed its consistency check".into());
        }
        Ok(run)
    }

    pub(crate) fn write(&self, run: &Run) -> Result<()> {
        let stored = SaveRun {
            sha256: json_digest(run)?,
            run,
        };
        let bytes = serde_json::to_vec_pretty(&stored)?;
        if bytes.len() as u64 > MAX_DOCUMENT {
            return Err("run record exceeds its storage limit".into());
        }
        // File sync persists the contents, directory sync persists the replacement.
        let mut staged = NamedTempFile::new_in(&self.directory)?;
        staged.write_all(&bytes)?;
        staged.as_file().sync_all()?;
        staged.persist(&self.record)?;
        File::open(&self.directory)?.sync_all()?;
        File::open(
            self.directory
                .parent()
                .ok_or("state-dir requires a parent")?,
        )?
        .sync_all()?;
        Ok(())
    }
}
