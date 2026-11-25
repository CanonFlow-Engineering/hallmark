use std::collections::HashSet;
use std::fs;
use std::path::Path;
use std::process::{Command, Stdio};

use serde::{Deserialize, Serialize};
use serde_json::json;
use sha2::{Digest, Sha256};

use crate::{Result, emit};

const MAX_BUNDLE_BYTES: u64 = 512 * 1024 * 1024;
const MAX_METADATA_BYTES: usize = 8 * 1024 * 1024;
const MAX_ENTRIES: usize = 10_004;

#[derive(Clone, Debug, Serialize, Deserialize)]
pub(crate) struct FileIdentity {
    #[serde(rename = "Path")]
    pub(crate) path: String,
    #[serde(rename = "Digest")]
    pub(crate) digest: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub(crate) struct ReleaseRecord {
    #[serde(rename = "SchemaVersion")]
    pub(crate) schema_version: u32,
    #[serde(rename = "Name")]
    pub(crate) name: String,
    #[serde(rename = "Version")]
    pub(crate) version: String,
    #[serde(rename = "PlanDigest")]
    pub(crate) plan_digest: String,
    #[serde(rename = "RunId")]
    pub(crate) run_id: String,
    #[serde(rename = "Context")]
    pub(crate) context: String,
    #[serde(rename = "Artifacts")]
    pub(crate) artifacts: Vec<FileIdentity>,
    #[serde(rename = "EvidenceDigest")]
    pub(crate) evidence_digest: String,
    #[serde(rename = "PublicKey")]
    pub(crate) public_key: String,
    #[serde(rename = "Signature")]
    pub(crate) signature: String,
}

fn sha256_prefixed(bytes: &[u8]) -> String {
    format!("sha256:{:x}", Sha256::digest(bytes))
}

fn decode_base64(input: &str) -> Result<Vec<u8>> {
    let mut out = Vec::with_capacity(input.len() * 3 / 4);
    let mut buf: u32 = 0;
    let mut bits: u8 = 0;
    for byte in input.bytes() {
        if byte.is_ascii_whitespace() {
            continue;
        }
        if byte == b'=' {
            break;
        }
        let val = match byte {
            b'A'..=b'Z' => byte - b'A',
            b'a'..=b'z' => byte - b'a' + 26,
            b'0'..=b'9' => byte - b'0' + 52,
            b'+' => 62,
            b'/' => 63,
            _ => return Err("invalid base64 character in signature".into()),
        };
        buf = (buf << 6) | u32::from(val);
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            let extracted = u8::try_from((buf >> bits) & 0xFF).unwrap_or(0);
            out.push(extracted);
        }
    }
    Ok(out)
}

fn encode_asn1_integer(raw: &[u8]) -> Vec<u8> {
    let mut slice = raw;
    while slice.len() > 1 && slice[0] == 0 {
        slice = &slice[1..];
    }
    let needs_pad = slice.first().is_some_and(|b| (b & 0x80) != 0);
    let len = slice.len() + usize::from(needs_pad);
    let mut out = Vec::with_capacity(2 + len);
    out.push(0x02);
    out.push(u8::try_from(len).unwrap_or(0));
    if needs_pad {
        out.push(0x00);
    }
    out.extend_from_slice(slice);
    out
}

fn ieee_p1363_to_der(raw_sig: &[u8]) -> Result<Vec<u8>> {
    if raw_sig.len() != 64 {
        return Err(format!("expected 64-byte P-256 signature, got {}", raw_sig.len()).into());
    }
    let r_der = encode_asn1_integer(&raw_sig[0..32]);
    let s_der = encode_asn1_integer(&raw_sig[32..64]);
    let seq_len = r_der.len() + s_der.len();
    let mut der = Vec::with_capacity(2 + seq_len);
    der.push(0x30);
    der.push(u8::try_from(seq_len)?);
    der.extend_from_slice(&r_der);
    der.extend_from_slice(&s_der);
    Ok(der)
}

pub(crate) fn verify_release_record(
    record: &ReleaseRecord,
    trusted_public_key: &str,
) -> Result<()> {
    if record.schema_version != 1 {
        return Err("unsupported release schema".into());
    }
    if record.public_key.trim() != trusted_public_key.trim() {
        return Err("release signer is not the explicitly trusted key".into());
    }
    let mut unsigned = record.clone();
    unsigned.signature = String::new();
    let payload_str = serde_json::to_string(&unsigned)?.replace('+', "\\u002B");
    let payload_bytes = payload_str.into_bytes();
    let raw_sig = decode_base64(record.signature.trim())?;
    let der_sig = if raw_sig.len() == 64 {
        ieee_p1363_to_der(&raw_sig)?
    } else {
        raw_sig
    };

    let temp = tempfile::TempDir::new()?;
    let pub_path = temp.path().join("pub.pem");
    let sig_path = temp.path().join("sig.der");
    let msg_path = temp.path().join("payload.json");
    fs::write(&pub_path, format!("{}\n", trusted_public_key.trim()))?;
    fs::write(&sig_path, &der_sig)?;
    fs::write(&msg_path, &payload_bytes)?;

    let status = Command::new("openssl")
        .args(["dgst", "-sha256", "-verify"])
        .arg(&pub_path)
        .arg("-signature")
        .arg(&sig_path)
        .arg(&msg_path)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()?;
    if !status.success() {
        return Err("release signature is invalid".into());
    }
    Ok(())
}

pub(crate) fn verify_release_file(release_path: &Path, public_key_path: &Path) -> Result<u8> {
    let trusted_key = fs::read_to_string(public_key_path)?;
    let release_bytes = fs::read(release_path)?;
    if release_bytes.len() > MAX_METADATA_BYTES {
        return Err("release metadata exceeds budget".into());
    }
    let record: ReleaseRecord = serde_json::from_slice(&release_bytes)?;
    verify_release_record(&record, &trusted_key)?;
    emit(&json!({
        "schemaVersion": 1,
        "valid": true,
        "name": record.name,
        "version": record.version,
        "planDigest": record.plan_digest,
        "runId": record.run_id,
        "artifactCount": record.artifacts.len(),
        "verifier": "hallmark-cli-rust"
    }))
}

fn read_u16_le(bytes: &[u8], offset: usize) -> Result<u16> {
    let slice = bytes
        .get(offset..offset + 2)
        .ok_or("unexpected end of ZIP archive")?;
    Ok(u16::from_le_bytes([slice[0], slice[1]]))
}

fn read_u32_le(bytes: &[u8], offset: usize) -> Result<u32> {
    let slice = bytes
        .get(offset..offset + 4)
        .ok_or("unexpected end of ZIP archive")?;
    Ok(u32::from_le_bytes([slice[0], slice[1], slice[2], slice[3]]))
}

fn parse_stored_zip(bytes: &[u8]) -> Result<Vec<(String, Vec<u8>)>> {
    if bytes.len() < 22 {
        return Err("invalid ZIP archive: too short".into());
    }
    let search_start = bytes.len().saturating_sub(65_557);
    let eocd_offset = (search_start..=bytes.len() - 22)
        .rev()
        .find(|&i| &bytes[i..i + 4] == b"PK\x05\x06")
        .ok_or("invalid ZIP archive: End of Central Directory not found")?;

    let total_entries = usize::from(read_u16_le(bytes, eocd_offset + 10)?);
    if total_entries > MAX_ENTRIES {
        return Err("bundle entry count exceeds budget".into());
    }
    let cd_offset = usize::try_from(read_u32_le(bytes, eocd_offset + 16)?)?;
    let mut cursor = cd_offset;
    let mut entries = Vec::with_capacity(total_entries);
    let mut seen_names = HashSet::with_capacity(total_entries);

    for _ in 0..total_entries {
        if bytes.get(cursor..cursor + 4) != Some(b"PK\x01\x02") {
            return Err("invalid ZIP central directory header".into());
        }
        let method = read_u16_le(bytes, cursor + 10)?;
        if method != 0 {
            return Err("bundle entries must use uncompressed Stored (method 0) format".into());
        }
        let compressed_size = usize::try_from(read_u32_le(bytes, cursor + 20)?)?;
        let uncompressed_size = usize::try_from(read_u32_le(bytes, cursor + 24)?)?;
        if compressed_size != uncompressed_size {
            return Err("mismatched stored entry size in bundle".into());
        }
        let name_len = usize::from(read_u16_le(bytes, cursor + 28)?);
        let extra_len = usize::from(read_u16_le(bytes, cursor + 30)?);
        let comment_len = usize::from(read_u16_le(bytes, cursor + 32)?);
        let local_offset = usize::try_from(read_u32_le(bytes, cursor + 42)?)?;

        let name_bytes = bytes
            .get(cursor + 46..cursor + 46 + name_len)
            .ok_or("truncated central directory entry name")?;
        let name = String::from_utf8(name_bytes.to_vec())?;
        if !seen_names.insert(name.clone()) {
            return Err("duplicate bundle entry".into());
        }

        if bytes.get(local_offset..local_offset + 4) != Some(b"PK\x03\x04") {
            return Err("invalid ZIP local file header".into());
        }
        let local_name_len = usize::from(read_u16_le(bytes, local_offset + 26)?);
        let local_extra_len = usize::from(read_u16_le(bytes, local_offset + 28)?);
        let data_start = local_offset + 30 + local_name_len + local_extra_len;
        let data_end = data_start
            .checked_add(uncompressed_size)
            .ok_or("ZIP entry size overflow")?;
        let data = bytes
            .get(data_start..data_end)
            .ok_or("truncated ZIP entry payload")?
            .to_vec();

        entries.push((name, data));
        cursor += 46 + name_len + extra_len + comment_len;
    }
    Ok(entries)
}

fn artifact_entry_name(digest: &str) -> Result<String> {
    let hex = digest
        .strip_prefix("sha256:")
        .ok_or("invalid artifact digest prefix")?;
    if hex.len() != 64
        || !hex
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err("invalid artifact SHA-256 digest".into());
    }
    Ok(format!("artifacts/{hex}"))
}

pub(crate) fn verify_bundle_file(bundle_path: &Path, public_key_path: &Path) -> Result<u8> {
    let meta = fs::metadata(bundle_path)?;
    if meta.len() > MAX_BUNDLE_BYTES {
        return Err("bundle exceeds 512 MiB".into());
    }
    let trusted_key = fs::read_to_string(public_key_path)?;
    let bytes = fs::read(bundle_path)?;
    let entries = parse_stored_zip(&bytes)?;
    let find_entry = |name: &str| -> Result<&[u8]> {
        let (_, data) = entries
            .iter()
            .find(|(n, _)| n == name)
            .ok_or_else(|| format!("required bundle entry {name} is missing"))?;
        Ok(data.as_slice())
    };

    let release_bytes = find_entry("release.json")?;
    if release_bytes.len() > MAX_METADATA_BYTES {
        return Err("release.json exceeds metadata budget".into());
    }
    let attested_bytes = find_entry("attested-steps.json")?;
    if attested_bytes.len() > MAX_METADATA_BYTES {
        return Err("attested-steps.json exceeds metadata budget".into());
    }
    let _ = find_entry("run.json")?;
    let _ = find_entry("history.ndjson")?;

    let release: ReleaseRecord = serde_json::from_slice(release_bytes)?;
    verify_release_record(&release, &trusted_key)?;

    if sha256_prefixed(attested_bytes) != release.evidence_digest {
        return Err("attested execution evidence changed".into());
    }

    let mut allowed: HashSet<String> = [
        "release.json".to_owned(),
        "attested-steps.json".to_owned(),
        "run.json".to_owned(),
        "history.ndjson".to_owned(),
    ]
    .into_iter()
    .collect();
    for artifact in &release.artifacts {
        allowed.insert(artifact_entry_name(&artifact.digest)?);
    }
    if entries.iter().any(|(name, _)| !allowed.contains(name)) {
        return Err("unexpected bundle entry".into());
    }

    let mut verified_digests = HashSet::new();
    for artifact in &release.artifacts {
        if verified_digests.insert(artifact.digest.clone()) {
            let entry_name = artifact_entry_name(&artifact.digest)?;
            let data = find_entry(&entry_name)?;
            if sha256_prefixed(data) != artifact.digest {
                return Err("released artifact bytes changed".into());
            }
        }
    }

    emit(&json!({
        "schemaVersion": 1,
        "valid": true,
        "name": release.name,
        "version": release.version,
        "planDigest": release.plan_digest,
        "runId": release.run_id,
        "artifactCount": release.artifacts.len(),
        "authenticatedScope": ["release", "attested-steps", "artifact-bytes"],
        "supplementalScope": ["run", "history"],
        "verifier": "hallmark-cli-rust"
    }))
}
