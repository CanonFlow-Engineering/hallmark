#!/usr/bin/env python3
"""Stage a real Playground app and identified Hallmark recipe without editing its checkout."""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path

from build import PROFILES


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=PROFILES)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--project", required=True, help="A unique owned cf- project/context name")
    parser.add_argument("--analyzer-package", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"cf-[a-z0-9-]{1,40}", args.project):
        parser.error("project must be a bounded cf- identifier")
    if args.profile == "orders" and (not args.analyzer_package or not args.analyzer_package.is_file()):
        parser.error("orders requires the identified CsAssay.Analyzers.0.2.0-rc.1.nupkg")
    source = args.source.resolve(strict=True)
    destination = args.destination.absolute()
    if destination.exists():
        parser.error("destination must not already exist")
    if source == destination or source in destination.parents:
        parser.error("stage outside the source checkout")
    args.cache.mkdir(parents=True, exist_ok=True)
    home = Path(__file__).resolve().parent
    stage = destination / "source"
    stage.mkdir(parents=True)
    ignored = shutil.ignore_patterns("bin", "obj", ".git", "node_modules", "TestResults")
    sample = "samples/40-wallet" if args.profile == "wallet" else "samples/40-functional-shape-v0.1"
    shutil.copytree(source / sample, stage / sample, ignore=ignored)
    if args.profile == "wallet":
        shutil.copytree(source / "tests/Wallet.Tests", stage / "tests/Wallet.Tests", ignore=ignored)
    for name in ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", ".editorconfig", ".csassay.json", ".fsassayrc"]:
        if (source / name).is_file():
            shutil.copyfile(source / name, stage / name)
    files = [{"path": str(p.relative_to(stage)), "sha256": digest(p)} for p in sorted(stage.rglob("*")) if p.is_file()]
    commit = subprocess.run(["git", "-C", str(source), "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    provenance = {"schemaVersion": 1, "source": str(source), "commit": commit, "files": files}
    if args.profile == "orders":
        package = stage / ".packages/CsAssay.Analyzers.0.2.0-rc.1.nupkg"
        package.parent.mkdir()
        shutil.copyfile(args.analyzer_package, package)
        provenance["analyzerPackageSha256"] = digest(package)
    (destination / "source-provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    shutil.copytree(home / "templates" / args.profile, destination, dirs_exist_ok=True)
    for name in ["build.py", "probe.py", "verify.py"]:
        shutil.copyfile(home / name, destination / name)

    def resolve(value):
        if isinstance(value, str):
            return value.replace("@WORKSPACE@", str(destination)).replace("@NUGET_CACHE@", str(args.cache.resolve())).replace("@PROJECT@", args.project)
        if isinstance(value, list):
            return [resolve(item) for item in value]
        if isinstance(value, dict):
            return {key: resolve(item) for key, item in value.items()}
        return value

    for name in ["hallmark.json", "compose.json"] + (["compose-db.json"] if args.profile == "wallet" else []):
        path = destination / name
        definition = resolve(json.loads(path.read_text()))
        if name == "hallmark.json":
            definition["inputs"].append("source-provenance.json")
            definition["inputs"] += ["source/" + name for name in [".editorconfig", ".csassay.json"] if (stage / name).is_file()]
        path.write_text(json.dumps(definition, indent=2) + "\n")
    print(json.dumps({"workspace": str(destination), "project": args.project, "manifest": str(destination / "hallmark.json"), "sdkImage": PROFILES[args.profile][0]}))


if __name__ == "__main__":
    main()
