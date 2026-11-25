use std::env;
use std::ffi::OsStr;
use std::fs;
use std::io::Read;
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

use hallmark_core::LedgerEntry;
use serde::{Deserialize, Serialize};
use tempfile::TempDir;

use crate::Result;
use crate::plan::{Database, Inputs, MAX_DOCUMENT, sha256_file};

const FLYWAY: &str =
    "flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657";
const PODMAN_FLYWAY: &str = "docker.io/flyway/flyway:13.12.1@sha256:3b4fae57858ee25735230e42f426fd200144fedbe692ea7789aa3e50a7c11657";
const DOCKER_SOCKET: &str = "unix:///var/run/docker.sock";
const READ_LEDGER: &str = "
SELECT pg_catalog.json_build_object(
    'identity', pg_catalog.json_build_object(
        'systemIdentifier', control.system_identifier::text, 'databaseOid', db.oid::bigint
    ),
    'ledger', (SELECT COALESCE(pg_catalog.json_agg(pg_catalog.json_build_object(
        'version', version, 'script', script, 'kind', type,
        'checksum', checksum, 'success', success
    ) ORDER BY installed_rank), '[]'::json) FROM public.flyway_schema_history)
)
FROM pg_catalog.pg_control_system() AS control
CROSS JOIN pg_catalog.pg_database AS db
WHERE db.datname = pg_catalog.current_database();
";

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct DatabaseIdentity {
    pub(crate) system_identifier: String,
    pub(crate) database_oid: u32,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Snapshot {
    pub(crate) identity: DatabaseIdentity,
    pub(crate) ledger: Vec<LedgerEntry>,
}

#[derive(Clone, Copy, Default, Debug, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "lowercase")]
pub(crate) enum ContainerEngine {
    #[default]
    Docker,
    Podman,
}

impl ContainerEngine {
    fn executable_name(self) -> &'static str {
        match self {
            Self::Docker => "docker",
            Self::Podman => "podman",
        }
    }

    fn flyway_image(self) -> &'static str {
        match self {
            Self::Docker => FLYWAY,
            Self::Podman => PODMAN_FLYWAY,
        }
    }
}

#[derive(Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(untagged, rename_all_fields = "camelCase", deny_unknown_fields)]
pub(crate) enum ProviderIdentity {
    Docker {
        docker_sha256: String,
        timeout_sha256: String,
        flyway_image: String,
    },
    Podman {
        podman_sha256: String,
        timeout_sha256: String,
        flyway_image: String,
        rootless_graph_root: PathBuf,
    },
}

pub(crate) struct LocalPostgres {
    engine: ContainerEngine,
    executable: PathBuf,
    timeout: PathBuf,
}

impl LocalPostgres {
    pub(crate) fn find(engine: ContainerEngine) -> Result<Self> {
        Ok(Self {
            engine,
            executable: find_executable(engine.executable_name())?,
            timeout: find_executable("timeout")?,
        })
    }

    pub(crate) fn identity(&self) -> Result<ProviderIdentity> {
        let executable_sha256 = sha256_file(&self.executable)?;
        let timeout_sha256 = sha256_file(&self.timeout)?;
        let flyway_image = self.engine.flyway_image().to_owned();
        match self.engine {
            ContainerEngine::Docker => Ok(ProviderIdentity::Docker {
                docker_sha256: executable_sha256,
                timeout_sha256,
                flyway_image,
            }),
            ContainerEngine::Podman => {
                let bytes = self.invoke(
                    "15s",
                    &["info", "--format", "json"],
                    None,
                    "inspect the local Podman runtime",
                )?;
                Ok(ProviderIdentity::Podman {
                    podman_sha256: executable_sha256,
                    timeout_sha256,
                    flyway_image,
                    rootless_graph_root: rootless_graph_root(&bytes)?,
                })
            }
        }
    }

    pub(crate) fn observe(&self, database: &Database) -> Result<Snapshot> {
        let bytes = self.invoke("15s", &[
            "exec",
            "--env", "PGCONNECT_TIMEOUT=5",
            "--env", "PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=5000 -c search_path=pg_catalog",
            "--user", "postgres",
            &database.container_id,
            "psql", "--no-psqlrc", "--no-password", "--host", "/var/run/postgresql",
            "--dbname", &database.name, "--username", &database.user,
            "--set", "ON_ERROR_STOP=1", "--quiet", "--tuples-only", "--no-align",
            "--command", READ_LEDGER,
        ], None, "read PostgreSQL identity and Flyway history")?;
        let snapshot: Snapshot = serde_json::from_slice(&bytes)?;
        if snapshot.identity.system_identifier.parse::<u64>().is_err()
            || snapshot.identity.database_oid == 0
        {
            return Err("invalid PostgreSQL target identity".into());
        }
        Ok(snapshot)
    }

    pub(crate) fn migrate(
        &self,
        database: &Database,
        snapshot: &TempDir,
        version: u32,
        password: &OsStr,
    ) -> Result<()> {
        let mount = snapshot
            .path()
            .to_str()
            .ok_or("state directory needs a UTF-8 path")?;
        let network = format!("container:{}", database.container_id);
        let mount = format!("type=bind,source={mount},target=/flyway/sql,readonly");
        let url = format!("-url=jdbc:postgresql://127.0.0.1:5432/{}", database.name);
        let user = format!("-user={}", database.user);
        let target = format!("-target={version}");
        let mut arguments = vec!["run", "--rm"];
        if self.engine == ContainerEngine::Podman {
            arguments.extend(["--timeout", "120"]);
        }
        arguments.extend([
            "--pull",
            "never",
            "--network",
            &network,
            "--cap-drop",
            "ALL",
            "--security-opt",
            "no-new-privileges",
            "--mount",
            &mount,
            "--env",
            "FLYWAY_PASSWORD",
            self.engine.flyway_image(),
            "-locations=filesystem:/flyway/sql",
            "-defaultSchema=public",
            "-table=flyway_schema_history",
            "-validateOnMigrate=true",
            "-cleanDisabled=true",
            "-placeholderReplacement=false",
            "-outOfOrder=false",
            "-baselineOnMigrate=false",
            "-outputType=json",
            &url,
            &user,
            &target,
            "migrate",
        ]);
        self.invoke(
            "120s",
            &arguments,
            Some(password),
            "dispatch Flyway migration",
        )?;
        Ok(())
    }

    fn invoke(
        &self,
        deadline: &str,
        arguments: &[&str],
        password: Option<&OsStr>,
        action: &str,
    ) -> Result<Vec<u8>> {
        let mut command = Command::new(&self.timeout);
        command
            .args(["--kill-after=5s", deadline])
            .arg(&self.executable)
            .env_clear()
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null());
        match self.engine {
            ContainerEngine::Docker => {
                command.args(["--host", DOCKER_SOCKET]);
            }
            ContainerEngine::Podman => {
                command.arg("--remote=false").env(
                    "PATH",
                    "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                );
                for name in [
                    "HOME",
                    "XDG_CONFIG_HOME",
                    "XDG_DATA_HOME",
                    "XDG_RUNTIME_DIR",
                    "DBUS_SESSION_BUS_ADDRESS",
                ] {
                    if let Some(value) = env::var_os(name) {
                        command.env(name, value);
                    }
                }
            }
        }
        command.args(arguments);
        if let Some(password) = password {
            command.env("FLYWAY_PASSWORD", password);
        }
        let mut child = command.spawn()?;
        let mut output = Vec::new();
        let read = child
            .stdout
            .take()
            .ok_or("missing container runtime output pipe")?
            .take(MAX_DOCUMENT + 1)
            .read_to_end(&mut output);
        let status = child.wait()?;
        read?;
        if !status.success() {
            return Err(format!("could not {action} ({status})").into());
        }
        if output.len() as u64 > MAX_DOCUMENT {
            return Err(format!("response limit exceeded while attempting to {action}").into());
        }
        Ok(output)
    }
}

fn rootless_graph_root(bytes: &[u8]) -> Result<PathBuf> {
    let info: serde_json::Value = serde_json::from_slice(bytes)?;
    if info
        .pointer("/host/security/rootless")
        .and_then(serde_json::Value::as_bool)
        != Some(true)
    {
        return Err("Podman requires a local rootless runtime".into());
    }
    let graph_root = info
        .pointer("/store/graphRoot")
        .and_then(serde_json::Value::as_str)
        .map(Path::new)
        .filter(|path| path.is_absolute())
        .ok_or("Podman did not report an absolute rootless storage path")?;
    Ok(graph_root.to_path_buf())
}

pub(crate) fn stage_sql(state_dir: &Path, inputs: &Inputs) -> Result<TempDir> {
    let snapshot = tempfile::Builder::new()
        .prefix("flyway-")
        .tempdir_in(state_dir)?;
    let path = snapshot
        .path()
        .to_str()
        .ok_or("state directory needs a UTF-8 path")?;
    if path.contains(['"', ',', '\n', '\r']) {
        return Err(
            "state directory contains a character unsupported by the container bind mount".into(),
        );
    }
    fs::set_permissions(snapshot.path(), fs::Permissions::from_mode(0o755))?;
    for (migration, bytes) in inputs.migrations.iter().zip(&inputs.sql) {
        let file = snapshot.path().join(&migration.script);
        fs::write(&file, bytes)?;
        fs::set_permissions(file, fs::Permissions::from_mode(0o644))?;
    }
    Ok(snapshot)
}

fn find_executable(name: &str) -> Result<PathBuf> {
    let path = env::var_os("PATH").ok_or("PATH is unset")?;
    for directory in env::split_paths(&path) {
        let candidate = directory.join(name);
        if candidate
            .metadata()
            .is_ok_and(|meta| meta.is_file() && meta.permissions().mode() & 0o111 != 0)
        {
            return Ok(candidate.canonicalize()?);
        }
    }
    Err(format!("required executable {name} is unavailable").into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn carries_the_schema_v1_docker_provider_bytes_forward() -> Result<()> {
        let legacy =
            r#"{"dockerSha256":"docker","timeoutSha256":"timeout","flywayImage":"flyway"}"#;
        let identity: ProviderIdentity = serde_json::from_str(legacy)?;
        assert_eq!(serde_json::to_string(&identity)?, legacy);
        assert!(matches!(identity, ProviderIdentity::Docker { .. }));
        Ok(())
    }

    #[test]
    fn podman_identity_requires_rootless_mode_and_absolute_storage() -> Result<()> {
        let rootless =
            br#"{"host":{"security":{"rootless":true}},"store":{"graphRoot":"/pg-store"}}"#;
        let rootful =
            br#"{"host":{"security":{"rootless":false}},"store":{"graphRoot":"/pg-store"}}"#;
        let relative =
            br#"{"host":{"security":{"rootless":true}},"store":{"graphRoot":"pg-store"}}"#;
        assert_eq!(rootless_graph_root(rootless)?, Path::new("/pg-store"));
        assert!(rootless_graph_root(rootful).is_err());
        assert!(rootless_graph_root(relative).is_err());
        Ok(())
    }
}
