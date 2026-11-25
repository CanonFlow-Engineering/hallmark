mod bundle;
mod plan;
mod postgres;
mod state;

use std::env;
use std::error::Error;
use std::ffi::OsString;
use std::io::{self, Write};
use std::path::Path;
use std::process::ExitCode;

use hallmark_core::{
    History, LedgerObservation, LedgerOutcome, RecoveryDecision, decide_recovery, inspect_history,
};
use serde::Serialize;
use serde_json::json;

use crate::bundle::{verify_bundle_file, verify_release_file};
use crate::plan::{Inputs, Plan, read_inputs};
use crate::postgres::{LocalPostgres, Snapshot, stage_sql};
use crate::state::{Run, RunResult, Store};

type Result<T> = std::result::Result<T, Box<dyn Error>>;

const HELP: &str =
    "Hallmark CLI (Rust): operator commands, pinned worker launch and isolation, and independent bundle verification.

hallmark engine <COMMAND> [ARGS...]
hallmark bundle-verify BUNDLE --public-key FILE
hallmark release-verify RELEASE --public-key FILE
hallmark plan MANIFEST
hallmark apply MANIFEST --plan-digest SHA256 --state-dir DIR
hallmark status --state-dir DIR
hallmark resume MANIFEST --run ID --state-dir DIR
hallmark --version

Use `hallmark engine` to delegate six-stage planning, execution, recovery, and Gate B release admission to the Hallmark Lifecycle Engine (F#).
Exit codes: 0 success, 3 held or unavailable runtime/state evidence, 64 invalid invocation.";

fn main() -> ExitCode {
    let Ok(args) = env::args_os()
        .skip(1)
        .map(OsString::into_string)
        .collect::<std::result::Result<Vec<_>, _>>()
    else {
        eprintln!("hallmark: command arguments must be UTF-8");
        return ExitCode::from(64);
    };
    match dispatch(&args) {
        Some(Ok(code)) => ExitCode::from(code),
        Some(Err(error)) => {
            eprintln!("hallmark: {error}");
            ExitCode::from(3)
        }
        None => {
            eprintln!("{HELP}");
            ExitCode::from(64)
        }
    }
}

fn dispatch(args: &[String]) -> Option<Result<u8>> {
    let args: Vec<_> = args.iter().map(String::as_str).collect();
    match args.as_slice() {
        [] | ["--help" | "help"] => Some(line(HELP)),
        ["--version"] => Some(line(concat!(
            "hallmark ",
            env!("CARGO_PKG_VERSION"),
            " (Hallmark CLI + Hallmark Lifecycle Engine)"
        ))),
        ["engine" | "worker" | "banquet", rest @ ..] if !rest.is_empty() => {
            Some(delegate_worker(rest))
        }
        ["mcp", rest @ ..] => {
            let mut mcp_args = Vec::with_capacity(rest.len() + 1);
            mcp_args.push("mcp");
            mcp_args.extend_from_slice(rest);
            Some(delegate_worker(&mcp_args))
        }
        ["bundle-verify", bundle_path, "--public-key", key_path]
            if !bundle_path.is_empty() && !key_path.is_empty() =>
        {
            Some(verify_bundle_file(
                Path::new(bundle_path),
                Path::new(key_path),
            ))
        }
        ["release-verify", release_path, "--public-key", key_path]
            if !release_path.is_empty() && !key_path.is_empty() =>
        {
            Some(verify_release_file(
                Path::new(release_path),
                Path::new(key_path),
            ))
        }
        ["plan", manifest] => Some(plan(Path::new(manifest))),
        ["status", "--state-dir", state] if !state.is_empty() => Some(
            Store::lock(Path::new(state))
                .and_then(|store| store.read())
                .and_then(|run| emit(&run)),
        ),
        ["apply" | "resume", manifest, flag1, value1, flag2, value2]
            if !value1.is_empty() && !value2.is_empty() =>
        {
            let (option, value, state) = match (*flag1, *flag2) {
                ("--state-dir", option) => (option, *value2, *value1),
                (option, "--state-dir") => (option, *value1, *value2),
                _ => return None,
            };
            let manifest = Path::new(manifest);
            let state = Path::new(state);
            match (args[0], option) {
                ("apply", "--plan-digest") => Some(apply(manifest, value, state)),
                ("resume", "--run") => Some(resume(manifest, value, state)),
                _ => None,
            }
        }
        _ => None,
    }
}

fn line(text: &str) -> Result<u8> {
    writeln!(io::stdout().lock(), "{text}")?;
    Ok(0)
}

fn emit(value: &impl Serialize) -> Result<u8> {
    line(&serde_json::to_string_pretty(value)?)
}

fn prepare(inputs: &Inputs) -> Result<(LocalPostgres, Plan, Snapshot)> {
    let provider = LocalPostgres::find(inputs.container_engine)?;
    let identity = provider.identity()?;
    let snapshot = provider.observe(&inputs.database)?;
    let plan = inputs.freeze(snapshot.identity.clone(), identity)?;
    Ok((provider, plan, snapshot))
}

fn history(inputs: &Inputs, snapshot: &Snapshot) -> Result<History> {
    let (current, preceding) = inputs.migrations.split_last().ok_or("missing migration")?;
    Ok(inspect_history(preceding, current, &snapshot.ledger))
}

fn plan(manifest: &Path) -> Result<u8> {
    let inputs = read_inputs(manifest)?;
    let (_, plan, snapshot) = prepare(&inputs)?;
    let admission = match history(&inputs, &snapshot)? {
        History::ExpectedPredecessor => "expectedPredecessor",
        History::CurrentMigration { checksum }
            if inputs
                .migrations
                .last()
                .is_some_and(|current| current.checksum == checksum) =>
        {
            "currentVersionPresent"
        }
        History::CurrentMigration { .. } | History::Diverged => "unexpectedHistory",
    };
    emit(&json!({
        "planDigest": plan.plan_digest,
        "inputs": plan.inputs,
        "observedLedger": snapshot.ledger,
        "admission": admission,
    }))
}

fn apply(manifest: &Path, reviewed: &str, state_dir: &Path) -> Result<u8> {
    let store = Store::lock(state_dir)?;
    store.ensure_new()?;
    let inputs = read_inputs(manifest)?;
    let (provider, plan, snapshot) = prepare(&inputs)?;
    if plan.plan_digest != reviewed {
        return Err("reviewed plan digest differs from the resolved plan".into());
    }
    if history(&inputs, &snapshot)? != History::ExpectedPredecessor {
        return Err(
            "Flyway history differs from the expected predecessor; no migration was dispatched"
                .into(),
        );
    }
    let password = env::var_os("FLYWAY_PASSWORD")
        .filter(|value| !value.is_empty())
        .ok_or("apply requires FLYWAY_PASSWORD")?;
    let staged = stage_sql(&store.directory, &inputs)?;
    let current = inputs.migrations.last().ok_or("missing migration")?;
    let run = Run {
        schema_version: 1,
        id: plan.plan_digest.clone(),
        operation: plan.pending_migration()?,
        plan,
        result: RunResult::Pending,
    };
    store.write(&run)?;

    if let Err(error) = provider.migrate(&inputs.database, &staged, current.version, &password) {
        eprintln!("hallmark: {error}; checking the ledger for the pending run");
    }
    match provider.observe(&inputs.database) {
        Ok(snapshot) => settle(run, &inputs, snapshot, &store),
        Err(error) => held(&run, &format!("migration readback unavailable: {error}")),
    }
}

fn resume(manifest: &Path, run_id: &str, state_dir: &Path) -> Result<u8> {
    let store = Store::lock(state_dir)?;
    let run = store.read()?;
    if run_id != run.id {
        return Err("run ID differs from the stored run".into());
    }
    let inputs = read_inputs(manifest)?;
    if run.plan.container_engine() != inputs.container_engine {
        return Err("container engine changed since this run was recorded".into());
    }
    let (_, plan, snapshot) = prepare(&inputs)?;
    if plan != run.plan {
        return Err(
            "plan, producer, source or target identity changed since this run was recorded".into(),
        );
    }
    settle(run, &inputs, snapshot, &store)
}

fn settle(mut run: Run, inputs: &Inputs, snapshot: Snapshot, store: &Store) -> Result<u8> {
    if snapshot.identity != run.plan.inputs.observed_database {
        return held(&run, "PostgreSQL target identity changed after preparation");
    }
    let current = inputs.migrations.last().ok_or("missing migration")?;
    let subject = run.plan.subject(&snapshot.identity, current.version);
    let outcome = match history(inputs, &snapshot)? {
        History::ExpectedPredecessor => LedgerOutcome::Unknown,
        History::CurrentMigration { checksum } => LedgerOutcome::Applied {
            checksum: checksum.to_string(),
        },
        History::Diverged => {
            return held(
                &run,
                "Flyway history conflicts with the frozen migration sequence",
            );
        }
    };
    match decide_recovery(&run.operation, &LedgerObservation { subject, outcome }) {
        RecoveryDecision::RecordApplied => {
            if matches!(run.result, RunResult::Pending) {
                run.result = RunResult::Applied {
                    ledger: snapshot.ledger,
                };
                store.write(&run)?;
            }
            emit(&run)
        }
        RecoveryDecision::AwaitReconciliation | RecoveryDecision::RetryEligible => held(
            &run,
            "migration commit is unconfirmed; resume performs readback only",
        ),
        RecoveryDecision::RejectConflictingChecksum => {
            held(&run, "Flyway checksum conflicts with the frozen migration")
        }
        RecoveryDecision::RejectUnrelatedObservation => held(
            &run,
            "ledger observation belongs to another migration subject",
        ),
    }
}

fn held(run: &Run, reason: &str) -> Result<u8> {
    eprintln!("hallmark: {reason}");
    emit(run)?;
    Ok(3)
}

const ALLOWED_WORKER_ENV: [&str; 14] = [
    "PATH",
    "HOME",
    "TMPDIR",
    "LANG",
    "LC_ALL",
    "DOTNET_ROOT",
    "DOTNET_CLI_HOME",
    "SSL_CERT_FILE",
    "SSL_CERT_DIR",
    "XDG_RUNTIME_DIR",
    "XDG_CONFIG_HOME",
    "XDG_DATA_HOME",
    "DBUS_SESSION_BUS_ADDRESS",
    "FLYWAY_PASSWORD",
];

fn is_pinned_image_digest(image: &str) -> bool {
    let hex_digest = if let Some(rest) = image.strip_prefix("sha256:") {
        rest
    } else if let Some((_, rest)) = image.rsplit_once("@sha256:") {
        rest
    } else {
        return false;
    };
    hex_digest.len() == 64
        && hex_digest
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
}

type MountEntry = (std::path::PathBuf, String, bool);

fn create_private_dir(path: &Path) -> Result<std::path::PathBuf> {
    std::fs::create_dir_all(path)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700));
    }
    Ok(std::fs::canonicalize(path)?)
}

fn bind_mount(
    mounts: &mut Vec<MountEntry>,
    host_dir: std::path::PathBuf,
    preferred_target: Option<&str>,
    readonly: bool,
) -> String {
    if let Some(entry) = mounts
        .iter_mut()
        .find(|(d, t, _)| *d == host_dir && preferred_target.is_none_or(|p| p == t))
    {
        entry.2 = entry.2 && readonly;
        return entry.1.clone();
    }
    let target =
        preferred_target.map_or_else(|| format!("/mnt/m{}", mounts.len()), ToString::to_string);
    mounts.push((host_dir, target.clone(), readonly));
    target
}

fn mcp_workspace_root(start: &Path) -> std::path::PathBuf {
    let home = env::var_os("HOME").map(std::path::PathBuf::from);
    let mut current = start.to_path_buf();
    for _ in 0..2 {
        let Some(parent) = current.parent() else {
            break;
        };
        let parent_str = parent.to_string_lossy();
        if parent_str.is_empty()
            || matches!(
                parent_str.as_ref(),
                "/" | "/home" | "/tmp" | "/var" | "/var/tmp" | "/opt" | "/run" | "/state"
            )
            || home.as_deref() == Some(parent)
        {
            break;
        }
        current = parent.to_path_buf();
    }
    current
}

fn add_mcp_host_mounts(args: &[&str], mounts: &mut Vec<MountEntry>) -> Result<()> {
    let allow_effects = args.contains(&"--allow-effects")
        || env::var("HALLMARK_MCP_ALLOW_EFFECTS").as_deref() == Ok("true");
    let mut roots = Vec::new();
    if let Ok(cwd) = env::current_dir().and_then(std::fs::canonicalize) {
        roots.push(mcp_workspace_root(&cwd));
    }
    for (idx, token) in args.iter().enumerate() {
        if *token == "--state-dir"
            && let Some(dir_arg) = args.get(idx + 1)
        {
            let state_canon = create_private_dir(Path::new(dir_arg))?;
            roots.push(mcp_workspace_root(&state_canon));
        }
    }
    if let Ok(extra) = env::var("HALLMARK_MCP_WORKSPACE")
        && let Ok(canon) = std::fs::canonicalize(extra)
    {
        roots.push(canon);
    }
    for root in roots {
        let target = root.to_string_lossy().to_string();
        bind_mount(mounts, root, Some(&target), !allow_effects);
    }
    Ok(())
}

fn mount_positional_arg(
    subcommand: &str,
    token: &str,
    readonly_workspace: bool,
    mounts: &mut Vec<MountEntry>,
) -> Result<Option<String>> {
    let candidate = Path::new(token);
    if candidate.is_file() {
        let canonical = std::fs::canonicalize(candidate)?;
        if let (Some(parent), Some(name)) = (canonical.parent(), canonical.file_name()) {
            let target_dir = bind_mount(mounts, parent.to_path_buf(), None, readonly_workspace);
            return Ok(Some(format!("{target_dir}/{}", name.to_string_lossy())));
        }
    } else if matches!(subcommand, "freeze" | "bundle" | "export")
        && let Some(file_name) = candidate.file_name()
    {
        let parent_raw = candidate
            .parent()
            .filter(|p| !p.as_os_str().is_empty())
            .map_or(env::current_dir()?, std::path::PathBuf::from);
        if parent_raw.is_dir() {
            let canonical_parent = std::fs::canonicalize(parent_raw)?;
            let target_dir = bind_mount(mounts, canonical_parent, None, false);
            return Ok(Some(format!(
                "{target_dir}/{}",
                file_name.to_string_lossy()
            )));
        }
    }
    Ok(None)
}

fn collect_podman_mounts(args: &[&str]) -> Result<(Vec<MountEntry>, Vec<String>)> {
    let subcommand = args.first().copied().unwrap_or("");
    let readonly_workspace = matches!(subcommand, "plan" | "release-verify" | "bundle-verify");
    let needs_state_dir = matches!(
        subcommand,
        "plan"
            | "freeze"
            | "apply"
            | "resume"
            | "converge"
            | "status"
            | "history"
            | "drift"
            | "secret-set"
            | "secret-list"
            | "public-key"
            | "bundle"
            | "promote"
            | "channels"
            | "destroy"
            | "export"
            | "mcp"
            | "triage"
    );
    let mut mounts: Vec<MountEntry> = Vec::new();
    if subcommand == "mcp" {
        add_mcp_host_mounts(args, &mut mounts)?;
    }
    let mut container_args: Vec<String> = Vec::with_capacity(args.len() + 2);
    let mut has_state_dir = false;
    let mut idx = 0;
    while idx < args.len() {
        let token = args[idx];
        if token == "--state-dir"
            && let Some(dir_arg) = args.get(idx + 1)
        {
            has_state_dir = true;
            let canonical = create_private_dir(Path::new(dir_arg))?;
            if subcommand == "mcp" {
                let host_target = canonical.to_string_lossy().to_string();
                bind_mount(&mut mounts, canonical.clone(), Some(&host_target), false);
            }
            let target = bind_mount(&mut mounts, canonical, Some("/state"), false);
            container_args.extend(["--state-dir".to_string(), target]);
            idx += 2;
            continue;
        }
        if token == "--public-key"
            && let Some(key_arg) = args.get(idx + 1)
        {
            let key_path = std::fs::canonicalize(Path::new(key_arg))?;
            if let (Some(parent), Some(name)) = (key_path.parent(), key_path.file_name()) {
                let target_dir = bind_mount(&mut mounts, parent.to_path_buf(), None, true);
                container_args.extend([
                    "--public-key".to_string(),
                    format!("{target_dir}/{}", name.to_string_lossy()),
                ]);
                idx += 2;
                continue;
            }
        }
        if idx > 0
            && !token.starts_with("--")
            && let Some(mapped) =
                mount_positional_arg(subcommand, token, readonly_workspace, &mut mounts)?
        {
            container_args.push(mapped);
            idx += 1;
            continue;
        }
        container_args.push(token.to_string());
        idx += 1;
    }
    if needs_state_dir && !has_state_dir {
        let canonical = create_private_dir(&env::current_dir()?.join(".canonforge"))?;
        let target = bind_mount(&mut mounts, canonical, Some("/state"), false);
        container_args.extend(["--state-dir".to_string(), target]);
    }
    Ok((mounts, container_args))
}

fn verify_rootless_podman(image: &str) -> Result<()> {
    if !is_pinned_image_digest(image) {
        return Err("HALLMARK_WORKER_IMAGE must be a sha256-pinned image digest".into());
    }
    let mut info_cmd = std::process::Command::new("podman");
    info_cmd.env_clear();
    for key in ALLOWED_WORKER_ENV {
        if let Some(val) = env::var_os(key) {
            info_cmd.env(key, val);
        }
    }
    let info = info_cmd
        .args([
            "--remote=false",
            "info",
            "--format",
            "{{.Host.Security.Rootless}}",
        ])
        .output()?;
    if !info.status.success() || String::from_utf8_lossy(&info.stdout).trim() != "true" {
        return Err("rootless Podman is required for HALLMARK_WORKER_IMAGE execution".into());
    }
    Ok(())
}

fn delegate_worker_podman(image: &str, args: &[&str]) -> Result<u8> {
    verify_rootless_podman(image)?;
    let (mounts, container_args) = collect_podman_mounts(args)?;
    let mut cmd = std::process::Command::new("podman");
    cmd.env_clear();
    for key in ALLOWED_WORKER_ENV {
        if let Some(val) = env::var_os(key) {
            cmd.env(key, val);
        }
    }
    cmd.args([
        "--remote=false",
        "run",
        "--rm",
        "--pull",
        "never",
        "--userns=keep-id:uid=1654,gid=1654",
        "--cap-drop=all",
        "--security-opt=no-new-privileges",
        "--read-only",
        "--network",
        "none",
        "--memory=512m",
        "--pids-limit=128",
        "--tmpfs",
        "/tmp:rw,noexec,nosuid,size=64m",
        "-e",
        "HOME=/tmp",
        "-e",
        "DOTNET_CLI_HOME=/tmp",
    ]);
    if matches!(args.first().copied(), Some("secret-set" | "mcp")) {
        cmd.arg("-i");
    }
    for (dir, target, readonly) in mounts {
        let dir_str = dir.to_string_lossy();
        if dir_str.contains(',') || dir_str.contains('\0') {
            return Err("mount path contains unsupported characters".into());
        }
        let spec = if readonly {
            format!("type=bind,source={dir_str},target={target},readonly")
        } else {
            format!("type=bind,source={dir_str},target={target}")
        };
        cmd.args(["--mount", &spec]);
    }
    cmd.arg(image).args(&container_args);
    let status = cmd.status()?;
    let code = status.code().unwrap_or(3).clamp(0, 255);
    Ok(u8::try_from(code).unwrap_or(3))
}

fn delegate_worker(args: &[&str]) -> Result<u8> {
    if let Ok(raw_image) =
        env::var("HALLMARK_WORKER_IMAGE").or_else(|_| env::var("HALLMARK_BANQUET_IMAGE"))
        && !raw_image.trim().is_empty()
    {
        return delegate_worker_podman(raw_image.trim(), args);
    }

    let repo_root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let worker_dll =
        match env::var("HALLMARK_WORKER_DLL").or_else(|_| env::var("HALLMARK_BANQUET_DLL")) {
            Ok(val) if !val.trim().is_empty() => std::path::PathBuf::from(val),
            _ => repo_root.join("cli/bin/Release/net10.0/Hallmark.Cli.dll"),
        };
    if !worker_dll.is_file() {
        let cli_proj = repo_root.join("cli/Hallmark.Cli.fsproj");
        if cli_proj.is_file() {
            let mut build_cmd = std::process::Command::new("dotnet");
            build_cmd.env_clear();
            for key in ALLOWED_WORKER_ENV {
                if let Some(val) = env::var_os(key) {
                    build_cmd.env(key, val);
                }
            }
            let build_status = build_cmd
                .arg("build")
                .arg(&cli_proj)
                .args(["-c", "Release", "--nologo", "-v", "q"])
                .status()?;
            if !build_status.success() {
                return Err("failed to build Hallmark Lifecycle Engine assembly".into());
            }
        }
    }
    if !worker_dll.is_file() {
        return Err(format!(
            "Hallmark Lifecycle Engine assembly not found at {}",
            worker_dll.display()
        )
        .into());
    }
    let mut cmd = std::process::Command::new("dotnet");
    cmd.env_clear();
    for key in ALLOWED_WORKER_ENV {
        if let Some(val) = env::var_os(key) {
            cmd.env(key, val);
        }
    }
    cmd.arg(&worker_dll).args(args);
    let status = cmd.status()?;
    let code = status.code().unwrap_or(3).clamp(0, 255);
    Ok(u8::try_from(code).unwrap_or(3))
}

#[cfg(test)]
mod tests {
    use super::delegate_worker;

    #[test]
    fn cli_delegates_capabilities_to_fsharp_lifecycle_engine() {
        let code = delegate_worker(&["capabilities"])
            .expect("Hallmark CLI should delegate to compiled Hallmark Lifecycle Engine assembly");
        assert_eq!(code, 0);
    }
}
