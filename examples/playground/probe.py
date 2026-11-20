#!/usr/bin/env python3
"""Run identical HTTP behavior probes inside an owned application container.

The wallet fixture has an intentional loopback-only HttpListener. Probing via
docker exec preserves that source contract for both delivery systems.
Secrets go to curl through stdin and never appear in process arguments.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import uuid


def request(container, port, method, path, body=None, token=None, probe_image=None):
    transport = (["docker", "run", "--rm", "-i", "--network", f"container:{container}",
                  "--cap-drop", "ALL", "--read-only", "--pids-limit", "32", "--memory", "128m", probe_image]
                 if probe_image else ["docker", "exec", "-i", container])
    command = [*transport, "curl", "-sS", "--max-time", "8",
               "-w", "\n%{http_code}", "-X", method, f"http://127.0.0.1:{port}{path}"]
    config = ""
    if body is not None:
        command += ["-H", "Content-Type: application/json", "--data-binary", json.dumps(body)]
    if token is not None:
        # Generated fixture material is hex; reject curl-config metacharacters.
        if not token or any(character in token for character in '\r\n"\\'):
            raise ValueError("Unsupported fixture token format")
        command += ["--config", "-"]
        config = f'header = "Authorization: Bearer {token}"\n'
    result = subprocess.run(command, input=config, capture_output=True, text=True, timeout=12)
    if result.returncode:
        raise RuntimeError("HTTP transport unavailable")
    text, code = result.stdout.rsplit("\n", 1)
    return int(code), json.loads(text) if text else {}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=["wallet", "orders"])
    parser.add_argument("container")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--probe-image", help="Pinned image containing curl; shares only the target network namespace")
    args = parser.parse_args()
    deadline = time.monotonic() + 45
    while True:
        try:
            request(args.container, args.port, "GET", "/", probe_image=args.probe_image)
            break
        except RuntimeError:
            if time.monotonic() >= deadline:
                raise
            time.sleep(0.25)
    cases = []

    def expect(name, expected, method, path, body=None, token=None):
        status, response = request(args.container, args.port, method, path, body, token, args.probe_image)
        if status != expected:
            raise RuntimeError(f"{name}: expected {expected}, observed {status}: {response}")
        cases.append({"name": name, "status": status, "passed": True})
        return response

    if args.profile == "orders":
        order = {"orderId": str(uuid.uuid4()), "lines": [{"productCode": "SKU-1", "quantity": 2}], "customerNote": "fixture"}
        response = expect("valid-order", 201, "POST", "/orders", order)
        if response["storedLineCount"] != 1 or response["orderId"] != order["orderId"]:
            raise RuntimeError("Order receipt does not match the submitted order")
        expect("empty-order", 422, "POST", "/orders", {**order, "lines": []})
        expect("invalid-quantity", 422, "POST", "/orders", {**order, "lines": [{"productCode": "SKU-1", "quantity": 0}]})
        expect("invalid-product", 422, "POST", "/orders", {**order, "lines": [{"productCode": "", "quantity": 1}]})
        expect("invalid-order-id", 422, "POST", "/orders", {**order, "orderId": str(uuid.UUID(int=0))})
        expect("missing-route", 404, "GET", "/missing")
    else:
        token = os.environ["CF_FIXTURE_TOKEN"]
        debit = {"reference": "cf-" + uuid.uuid4().hex, "wallet": "a", "amount": 10}
        expect("authentication", 401, "POST", "/debit", debit)
        first = expect("debit", 200, "POST", "/debit", debit, token)
        if first.get("status") != "accepted" or first.get("balance", -1) < 0:
            raise RuntimeError("Fixture debit was not accepted with a nonnegative balance")
        duplicate = expect("idempotent-replay", 200, "POST", "/debit", debit, token)
        if duplicate != first:
            raise RuntimeError("Replay changed the persisted debit outcome")
        expect("conflicting-intent", 409, "POST", "/debit", {**debit, "amount": 11}, token)
        expect("invalid-amount", 400, "POST", "/debit", {**debit, "amount": -1}, token)
        expect("wrong-method", 404, "GET", "/debit", token=token)
        expect("body-budget", 413, "POST", "/debit", {**debit, "padding": "x" * 9000}, token)
    report = {"schemaVersion": 1, "profile": args.profile, "container": args.container,
              "passed": len(cases), "failed": 0, "cases": cases}
    args.out.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
