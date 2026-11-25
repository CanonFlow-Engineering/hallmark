#!/usr/bin/env python3
"""Observe a completed fixture exercise and current loopback availability.

The exercise is an explicit imperative lifecycle step. This observation does
not submit orders or debit wallets. CanonForge binds the preceding report bytes
and refuses output drift before release.
"""
import argparse
import json
from pathlib import Path
import time

from probe import request

CASES = {
    "wallet": {"authentication", "debit", "idempotent-replay", "conflicting-intent", "invalid-amount", "wrong-method", "body-budget"},
    "orders": {"valid-order", "empty-order", "invalid-quantity", "invalid-product", "invalid-order-id", "missing-route"},
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=CASES)
    parser.add_argument("container")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--probe-image", required=True)
    args = parser.parse_args()
    if args.report.stat().st_size > 131072:
        raise ValueError("Fixture report exceeds 128 KiB")
    report = json.loads(args.report.read_text())
    if (report.get("schemaVersion") != 1 or report.get("profile") != args.profile
            or report.get("container") != args.container or report.get("failed") != 0
            or report.get("passed") != len(CASES[args.profile])):
        raise ValueError("Fixture report identity/count/outcome mismatch")
    cases = report["cases"]
    if len(cases) != len(CASES[args.profile]) or {case["name"] for case in cases} != CASES[args.profile] or any(case.get("passed") is not True for case in cases):
        raise ValueError("Fixture case coverage is incomplete")
    deadline = time.monotonic() + 30
    while True:
        try:
            status, _ = request(args.container, args.port, "GET", "/", probe_image=args.probe_image)
            if status != 404:
                raise ValueError(f"Unexpected availability response: {status}")
            break
        except RuntimeError:
            if time.monotonic() >= deadline:
                raise
            time.sleep(0.25)
    print(json.dumps({"schemaVersion": 1, "profile": args.profile, "exerciseCases": len(cases),
                      "availabilityStatus": status, "observation": "read-only", "passed": True}))


if __name__ == "__main__":
    main()
