#!/usr/bin/env python3
"""Publish an unchanged, staged Playground app with its pinned SDK.

The C# stage must already contain the explicitly identified analyzer package
and matching staged lock files. This script never rewrites source or policy.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tarfile

PROFILES = {
    "wallet": (
        "mcr.microsoft.com/dotnet/sdk:10.0.301@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5",
        "samples/40-wallet/Wallet.Api/Wallet.Api.fsproj",
    ),
    "orders": (
        "mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2@sha256:db7d1f28c0805ea69fe43f73e54cffd2a0689a0c637a6078328e27c5aab2314a",
        "samples/40-functional-shape-v0.1/src/Shape.Api/Shape.Api.csproj",
    ),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=PROFILES)
    parser.add_argument("--cache", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd()
    if not (root / "source/global.json").is_file():
        parser.error("Run in a prepared sample directory containing source/global.json")
    args.cache.mkdir(parents=True, exist_ok=True)
    image, project = PROFILES[args.profile]
    command = [
        "docker", "run", "--rm", "--user", f"{os.getuid()}:{os.getgid()}",
        "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
        "--pids-limit", "512", "--memory", "4g", "--cpus", "3",
        "-e", "DOTNET_CLI_HOME=/tmp/dotnet-home", "-e", "NUGET_PACKAGES=/nuget",
        "-v", f"{root / 'source'}:/src", "-v", f"{root}:/out",
        "-v", f"{args.cache.resolve()}:/nuget", "-w", "/src", image,
        "dotnet", "publish", project, "-c", "Release", "-o", "/out/payload",
        "-p:RestoreLockedMode=true",
    ]
    subprocess.run(command, check=True, timeout=900)
    with tarfile.open(root / "application.tar.gz", "w:gz") as archive:
        for path in sorted((root / "payload").rglob("*")):
            archive.add(path, arcname=str(path.relative_to(root)), recursive=False)
    print(json.dumps({"profile": args.profile, "sdkImage": image, "artifact": "application.tar.gz"}))


if __name__ == "__main__":
    main()
