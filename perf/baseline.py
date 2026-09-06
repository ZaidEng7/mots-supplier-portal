#!/usr/bin/env python3
"""
EPIC-26's first measurement of the read paths.

Deliberately plain: urllib and the standard library, so running it needs nothing installed. A real
load tool (k6, NBomber) models concurrency, ramp-up and think time, and this does none of that - see
BASELINE.md for exactly what this number is and is not. It exists so that the targets stop being
unmeasured, and so the next change to a cross-aggregate read can be compared against something.

Usage:  python3 perf/baseline.py [--iterations 30]
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import statistics
import struct
import subprocess
import time
import urllib.error
import urllib.request

# The personas the measured endpoints belong to. Passwords are the dev seed's - this script only ever
# talks to a local development server, and RUNBOOK.md prints the same values.
PERSONAS = {
    "officer": ("officer@mots.local", "motsdemo2026"),
    "manager": ("manager@mots.local", "motsdemo2026"),
    "supplier": ("supplier@mots.local", "motsdemo2026"),
    "reviewer": ("reviewer@mots.local", "motsreview2026"),
    "ministry": ("ministry@mots.local", "motsdemo2026"),
    # system_admin is the only role in Mfa:RequiredRoles, so its login needs a TOTP code. Included rather
    # than skipped because the admin screens carry the heaviest reads in the product - the jobs monitor
    # probes Hangfire storage, and the storage panel probes MinIO and ClamAV on every request.
    "admin": ("admin@mots.local", "motsadmin2026"),
}

TOTP_SECRET_SQL = (
    "select t.\"Value\" from identity.user_token t "
    "join identity.app_user u on u.\"Id\" = t.\"UserId\" "
    "where u.\"Email\" = 'admin@mots.local' and t.\"Name\" = 'AuthenticatorKey';"
)


def admin_totp() -> str | None:
    """The seeded authenticator key, read from the dev database.

    Identity will only generate that key, never accept a chosen one, so there is no way to know it without
    reading it back - the same reason AdminSeeder prints it once. Local development only.
    """
    container = os.environ.get("PG_CONTAINER", "mots-supplier-portal-postgres-1")
    database = os.environ.get("PG_DATABASE", "mots_supplier_portal")
    try:
        secret = subprocess.run(
            ["docker", "exec", container, "psql", "-U", "postgres", "-d", database, "-t", "-c", TOTP_SECRET_SQL],
            capture_output=True, text=True, timeout=20, check=True,
        ).stdout.strip()
    except (subprocess.SubprocessError, FileNotFoundError):
        return None

    if not secret:
        return None

    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    # SHA-1 because TOTP specifies SHA-1 (RFC 6238 section 1.2, RFC 4226 section 5.3), and the server this
    # code is generating for uses it. Not a hash of anything secret being stored - it is an HMAC keyed by a
    # shared secret, which SHA-1's collision weakness does not affect. Changing it would only produce codes
    # the server rejects.
    digest = hmac.new(key, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()  # NOSONAR S4790
    offset = digest[19] & 0xF
    return "%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000)

# (label, persona, path). The cross-aggregate reads the sizing calls "the interesting cases" are first:
# the procurement dashboard and the comparison matrix both fan out across aggregates.
ENDPOINTS = [
    ("procurement dashboard", "officer", "/api/v1/procurement/dashboard"),
    ("comparison matrix", "officer", "/api/v1/rfqs/RFQ-DEMO-0004/comparison"),
    ("rfq list", "officer", "/api/v1/rfqs?pageSize=25"),
    ("rfq detail", "officer", "/api/v1/rfqs/RFQ-DEMO-0004"),
    ("evaluation read", "officer", "/api/v1/rfqs/RFQ-DEMO-0004/evaluation"),
    ("supplier dashboard", "supplier", "/api/v1/suppliers/me/dashboard"),
    ("supplier profile", "supplier", "/api/v1/suppliers/me"),
    ("my proposals", "supplier", "/api/v1/proposals"),
    ("review queue", "reviewer", "/api/v1/review/queue?pageSize=25"),
    ("review dashboard", "reviewer", "/api/v1/review/dashboard"),
    ("ministry overview", "ministry", "/api/v1/ministry/overview"),
    ("search (one term)", "officer", "/api/v1/search?q=demo"),
    # audit.read is system_admin's, not the manager's. Measured as the admin rather than left as a 403:
    # a fast refusal is not a fast read, and a baseline full of them would look excellent.
    ("audit search", "admin", "/api/v1/audit?pageSize=25"),
    ("jobs monitor", "admin", "/api/v1/admin/jobs"),
    ("outbox monitor", "admin", "/api/v1/admin/outbox"),
    ("erp sync monitor", "admin", "/api/v1/admin/erp-sync"),
    # Probes the object store and the virus scanner on every call, so it is expected to be the slowest
    # read in the product - and worth knowing by how much.
    ("storage settings", "admin", "/api/v1/admin/storage"),
    ("security posture", "admin", "/api/v1/admin/security"),
]


def post_json(path: str, payload: dict) -> dict:
    request = urllib.request.Request(
        endpoint_url(path),
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode())


def token_for(email: str, password: str, totp: str | None = None,
              attempts: int = 3) -> tuple[str | None, str]:
    """(token, reason). The reason is reported rather than swallowed: a baseline missing its slowest
    endpoint is worse than one that says why, and the first run of this script reported "could not sign in"
    for what was actually a 429 from the auth limiter - NFR-SEC-009 allows ten attempts a minute, and a
    harness that signs in six times alongside any other activity can reach it."""
    payload = {"email": email, "password": password}
    if totp:
        payload["totpCode"] = totp

    for attempt in range(attempts):
        try:
            return post_json("/api/v1/auth/login", payload)["accessToken"], "ok"
        except urllib.error.HTTPError as error:
            error.read()
            if error.code == 429 and attempt < attempts - 1:
                # The limiter's window is a minute. Waiting is the correct response to being asked to slow
                # down, and retrying immediately would just deepen the hole.
                time.sleep(20)
                continue
            return None, f"HTTP {error.code}"
        except urllib.error.URLError as error:
            return None, f"unreachable ({error.reason})"
        except KeyError:
            return None, "no accessToken in response"

    return None, "gave up after retries"


def measure(path: str, token: str, iterations: int) -> tuple[list[float], int]:
    """Latencies in milliseconds, plus the status of the last response."""
    samples: list[float] = []
    status = 0
    for _ in range(iterations):
        request = urllib.request.Request(endpoint_url(path), headers={"Authorization": f"Bearer {token}"})
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                response.read()
                status = response.status
        except urllib.error.HTTPError as error:
            error.read()
            status = error.code
        except urllib.error.URLError:
            status = 0
        samples.append((time.perf_counter() - started) * 1000)
    return samples, status


def percentile(samples: list[float], fraction: float) -> float:
    """Nearest-rank percentile. Not interpolated: with 30 samples an interpolated p95 invents a value
    between two measurements, and a made-up number is the wrong thing to put in a baseline."""
    ordered = sorted(samples)
    index = max(0, min(len(ordered) - 1, int(round(fraction * len(ordered))) - 1))
    return ordered[index]


# The API this measures, as a constant rather than a flag - see endpoint_url.
API_ORIGIN = "http://localhost:5080"


def endpoint_url(path: str) -> str:
    """The URL for one endpoint on the local API, built from a module constant and a literal path.

    No part of this address comes from outside the file, and that is the point.

    The script authenticates as five personas and replays reads with their bearer tokens, so whatever names
    the host decides where those credentials get sent. It began as `--base`, a free-form string concatenated
    onto a path: a typo in a copied invocation was enough to post real credentials to someone else's server.

    Validating that string - in main(), then again at the point of use - fixed the hole and kept the smell:
    an address assembled from caller input, safe only because of a check the reader has to go and find.
    Narrowing the flag to an integer port removed the hole properly but kept the same shape.

    So there is no flag. This measures the local development API, whose port RUNBOOK.md fixes at 5080, and
    the one documented invocation only ever passes --iterations. Nothing outside this file can influence
    where a token is sent, which is a stronger statement than any amount of validation, and measuring a
    different server is a one-line edit above by someone who has read this.
    """
    return API_ORIGIN + path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--iterations", type=int, default=30)
    parser.add_argument("--warmup", type=int, default=3,
                        help="Discarded requests per endpoint. The first call to an EF query pays for its "
                             "compiled-query cache and its connection, and reporting that as latency would "
                             "measure the process starting up.")
    arguments = parser.parse_args()

    code = admin_totp()
    if code is None:
        print("!! could not read the admin's TOTP secret from the dev database - admin endpoints are skipped")

    logins = {
        name: token_for(email, password, code if name == "admin" else None)
        for name, (email, password) in PERSONAS.items()
    }
    tokens = {name: token for name, (token, _) in logins.items()}
    for name, (token, reason) in logins.items():
        if token is None:
            print(f"!! could not sign in as {name} ({reason}) - its endpoints are skipped")

    print(f"\n{'endpoint':<26} {'persona':<9} {'status':>6} {'n':>4} "
          f"{'p50':>8} {'p95':>8} {'max':>8}")
    print("-" * 78)

    results = []
    for label, persona, path in ENDPOINTS:
        token = tokens.get(persona)
        if token is None:
            print(f"{label:<26} {persona:<9} {'skip':>6}")
            continue

        measure(path, token, arguments.warmup)
        samples, status = measure(path, token, arguments.iterations)

        row = (label, persona, status, len(samples),
               statistics.median(samples), percentile(samples, 0.95), max(samples))
        results.append(row)
        print(f"{label:<26} {persona:<9} {status:>6} {len(samples):>4} "
              f"{row[4]:>8.1f} {row[5]:>8.1f} {row[6]:>8.1f}")

    non_2xx = [r for r in results if not 200 <= r[2] < 300]
    if non_2xx:
        # A fast 404 is not a fast read. Called out because a baseline full of them would look excellent.
        print("\n!! endpoints that did not return 2xx - their timings measure a refusal, not a read:")
        for row in non_2xx:
            print(f"   {row[0]} -> {row[2]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
