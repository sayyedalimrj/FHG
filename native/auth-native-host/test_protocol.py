"""Synthetic security and native messaging contract tests. Never use real accounts."""
import json
import struct
import subprocess
import sys
from pathlib import Path

exe = Path(sys.argv[1]).resolve()
assert exe.is_file(), "Missing compiled native host"

def run(request, caller="fhg-authenticator@fhg.local"):
    payload = json.dumps(request, separators=(",", ":")).encode()
    proc = subprocess.run(
        [str(exe), str(exe.parent / "FHGNativeHost.json"), caller],
        input=struct.pack("<I", len(payload)) + payload,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20
    )
    return proc

success = run({"action": "list"})
assert success.returncode == 0, f"Native host exited {success.returncode}"
assert len(success.stdout) >= 4, "Missing native framing header"
size, = struct.unpack("<I", success.stdout[:4])
assert size == len(success.stdout) - 4, "Wrong native framing"
response = json.loads(success.stdout[4:])
assert response["ok"] is True and isinstance(response["accounts"], list)
assert not success.stderr, "Host should never log secrets or protocol output to stderr"

for action, origin, path in [
    ("password", "https://not-chatgpt.example", "/log-in/password"),
    ("totp", "http://auth.openai.com", "/mfa-challenge/1"),
    ("email", "https://chatgpt.com", "/evil/untrusted")
]:
    denied = run({"action": action, "origin": origin, "path": path})
    assert denied.returncode == 0
    data = json.loads(denied.stdout[4:])
    assert data.get("error") == "ORIGIN_REJECTED", data

invalid = run({"action": "list"}, caller="unknown-addon@attacker.invalid")
assert invalid.returncode != 0 and not invalid.stdout, "Unauthorized extension caller accepted"
print("PASS: native framing, DPAPI account list, origin allowlist and denied caller")
