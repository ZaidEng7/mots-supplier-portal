#!/usr/bin/env python3
"""EPIC-26's first measurement of the read and write paths.

Deliberately plain: urllib and the standard library, so running it needs nothing installed. A real
load tool (k6, NBomber) models concurrency, ramp-up and think time, and this does none of that. See
BASELINE.md for exactly what this number is and is not. It exists so the targets stop being
unmeasured, and so the next change to a cross-aggregate read can be compared against something.

Usage:  python3 perf/baseline.py [--iterations 30]

THE ACCOUNTS. Passwords are the dev seed's. This script only ever talks to a local development
server, and docs/handbook/RUNBOOK.md prints the same values. There is ONE password for every seeded account,
DevDataSeeder.Password. This script once held two others, motsreview2026 and motsadmin2026, from
before the seeders converged on a single fallback, and both were refused. The harness reported that
as "could not sign in" and carried on, so eight of the eighteen measured reads (the reviewer's two
and the admin's six, the slowest in the product) were silently absent from the table rather than
marked as failing. If they ever disagree again, read the constant rather than this list:
src/backend/Infrastructure/Identity/DevDataSeeder.cs.

system_admin is the only role in Mfa:RequiredRoles, so its login needs a TOTP code. It is included
rather than skipped because the admin screens carry the heaviest reads in the product: the jobs
monitor probes Hangfire storage, and the storage panel probes MinIO and ClamAV on every request. The
code is generated with SHA-1 because TOTP specifies SHA-1 (RFC 6238 §1.2, RFC 4226 §5.3) and the
server being measured uses it. Nothing secret is being hashed: it is an HMAC keyed by a shared
secret, which SHA-1's collision weakness does not affect. Changing it would only produce codes the
server rejects.

THE READS are (label, persona, path). The cross-aggregate reads the sizing calls "the interesting
cases" come first, because the procurement dashboard and the comparison matrix both fan out across
aggregates. Three rows need their own note.

The evaluation read uses RFQ-DEMO-0005 rather than -0004 like its neighbours, because an evaluation
exists only once a tender reaches UnderEvaluation and -0004 is seeded SubmissionOpen. That row once
measured the 404 instead, at 5ms, the fastest "read" in the table, which is the precise failure the
note under the table warns about, sitting inside the table. The real read is two orders of magnitude
slower.

The audit search is measured as the admin rather than the manager, because audit.read is
system_admin's permission. Left as the manager it would have measured a 403, and a fast refusal is
not a fast read: a baseline full of them would look excellent.

The storage settings read probes the object store and the virus scanner on every call, so it is
expected to be the slowest read in the product, and it is worth knowing by how much.

THE WRITES are T-107's half. The read table has existed since EPIC-26; the sub-800ms write target had
no number at all. Every write here is REPEATABLE against the same row: each sets a value to what it
already is, or to one the next iteration overwrites. That rules out the writes a person would most
like to see, such as creating a tender or submitting a bid, because measuring those means leaving
thirty drafts behind, and a baseline that changes the dataset it measures is not a baseline. What is
here is the ordinary editing traffic the product carries between those events.

Writes marked `needs_etag` fetch the current version first, and that GET is NOT timed: §8.1 makes it
part of the caller's flow rather than part of the write. The supplier edit's payload is filled in at
run time from the profile's own current description, so the write sets the field to exactly what it
already held. The constant that used to sit there, "Measured by perf/baseline.py", was left behind in
the demonstration database, where it had replaced SUP-DEMO-0001's seeded description and showed on
the supplier profile screen. A baseline that brands the data it measures is the same defect as one
that changes it.

The supplier's own code is read once at run time and substituted into the path, because §12-A/C3
addresses the profile by code and the harness must not hard-code one belonging to whoever seeded the
database.

On a 429, the script waits. The limiter's window is a minute, waiting is the correct response to
being asked to slow down, and retrying immediately would only deepen the hole.

WHAT THE HELPERS DO, since their bodies now carry no prose of their own.

admin_totp: The seeded authenticator key, read from the dev database. Identity will only generate that key, never accept a chosen one, so there is no way to know it without reading it back - the same reason AdminSeeder prints it once. Local development only.

token_for: (token, reason). The reason is reported rather than swallowed: a baseline missing its slowest endpoint is worse than one that says why, and the first run of this script reported "could not sign in" for what was actually a 429 from the auth limiter - NFR-SEC-009 allows ten attempts a minute, and a harness that signs in six times alongside any other activity can reach it.

measure: Latencies in milliseconds, plus the status of the last response.

get_json: The body and the ETag, for writes that need a precondition.

measure_write: Latencies in milliseconds for a repeatable write, plus the status of the last response. The ETag fetch is deliberately outside the timer. §8.1 requires If-Match on these routes, and a caller already holds the version from the read that showed them the thing they are editing - charging the write for a GET it does not make would measure the harness rather than the endpoint.

percentile: Nearest-rank percentile. Not interpolated: with 30 samples an interpolated p95 invents a value between two measurements, and a made-up number is the wrong thing to put in a baseline.

endpoint_url: The URL for one endpoint on the local API, built from a module constant and a literal path. No part of this address comes from outside the file, and that is the point. The script authenticates as five personas and replays reads with their bearer tokens, so whatever names the host decides where those credentials get sent. It began as `--base`, a free-form string concatenated onto a path: a typo in a copied invocation was enough to post real credentials to someone else's server. Validating that string - in main(), then again at the point of use - fixed the hole and kept the smell: an address assembled from caller input, safe only because of a check the reader has to go and find. Narrowing the flag to an integer port removed the hole properly but kept the same shape. So there is no flag. This measures the local development API, whose port docs/handbook/RUNBOOK.md fixes at 5080, and the one documented invocation only ever passes --iterations. Nothing outside this file can influence where a token is sent, which is a stronger statement than any amount of validation, and measuring a different server is a one-line edit above by someone who has read this.
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

PERSONAS = {
    "officer": ("officer@mots.local", "motsdemo2026"),
    "manager": ("manager@mots.local", "motsdemo2026"),
    "supplier": ("supplier@mots.local", "motsdemo2026"),
    "reviewer": ("reviewer@mots.local", "motsdemo2026"),
    "ministry": ("ministry@mots.local", "motsdemo2026"),
    "admin": ("admin@mots.local", "motsdemo2026"),
}

TOTP_SECRET_SQL = (
    "select t.\"Value\" from identity.user_token t "
    "join identity.app_user u on u.\"Id\" = t.\"UserId\" "
    "where u.\"Email\" = 'admin@mots.local' and t.\"Name\" = 'AuthenticatorKey';"
)

def admin_totp() -> str | None:
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
    digest = hmac.new(key, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()  # NOSONAR S4790
    offset = digest[19] & 0xF
    return "%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000)

ENDPOINTS = [
    ("procurement dashboard", "officer", "/api/v1/procurement/dashboard"),
    ("comparison matrix", "officer", "/api/v1/rfqs/RFQ-DEMO-0004/comparison"),
    ("rfq list", "officer", "/api/v1/rfqs?pageSize=25"),
    ("rfq detail", "officer", "/api/v1/rfqs/RFQ-DEMO-0004"),
    ("evaluation read", "officer", "/api/v1/rfqs/RFQ-DEMO-0005/evaluation"),
    ("supplier dashboard", "supplier", "/api/v1/suppliers/me/dashboard"),
    ("supplier profile", "supplier", "/api/v1/suppliers/me"),
    ("my proposals", "supplier", "/api/v1/proposals"),
    ("review queue", "reviewer", "/api/v1/review/queue?pageSize=25"),
    ("review dashboard", "reviewer", "/api/v1/review/dashboard"),
    ("ministry overview", "ministry", "/api/v1/ministry/overview"),
    ("search (one term)", "officer", "/api/v1/search?q=demo"),
    ("audit search", "admin", "/api/v1/audit?pageSize=25"),
    ("jobs monitor", "admin", "/api/v1/admin/jobs"),
    ("outbox monitor", "admin", "/api/v1/admin/outbox"),
    ("erp sync monitor", "admin", "/api/v1/admin/erp-sync"),
    ("storage settings", "admin", "/api/v1/admin/storage"),
    ("security posture", "admin", "/api/v1/admin/security"),
]

WRITES = [
    ("supplier profile edit", "supplier", "PATCH", "/api/v1/suppliers/{supplierCode}",
     {"description": None}, True),
    ("notification preferences", "officer", "PUT", "/api/v1/notifications/preferences",
     {"mutedTypes": []}, False),
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
    payload = {"email": email, "password": password}
    if totp:
        payload["totpCode"] = totp

    for attempt in range(attempts):
        try:
            return post_json("/api/v1/auth/login", payload)["accessToken"], "ok"
        except urllib.error.HTTPError as error:
            error.read()
            if error.code == 429 and attempt < attempts - 1:
                time.sleep(20)
                continue
            return None, f"HTTP {error.code}"
        except urllib.error.URLError as error:
            return None, f"unreachable ({error.reason})"
        except KeyError:
            return None, "no accessToken in response"

    return None, "gave up after retries"

def measure(path: str, token: str, iterations: int) -> tuple[list[float], int]:
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

def get_json(path: str, token: str) -> tuple[dict | None, str | None]:
    request = urllib.request.Request(endpoint_url(path), headers={"Authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode()), response.headers.get("ETag")
    except (urllib.error.HTTPError, urllib.error.URLError, json.JSONDecodeError):
        return None, None

def measure_write(method: str, path: str, payload: dict, token: str,
                  iterations: int, needs_etag: bool) -> tuple[list[float], int]:
    samples: list[float] = []
    status = 0
    for _ in range(iterations):
        headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
        if needs_etag:
            _, etag = get_json(path, token)
            if etag:
                headers["If-Match"] = etag

        request = urllib.request.Request(
            endpoint_url(path), data=json.dumps(payload).encode(), headers=headers, method=method)

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
    ordered = sorted(samples)
    index = max(0, min(len(ordered) - 1, int(round(fraction * len(ordered))) - 1))
    return ordered[index]

API_ORIGIN = "http://localhost:5080"

def endpoint_url(path: str) -> str:
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

    supplier_profile, _ = get_json("/api/v1/suppliers/me", tokens.get("supplier") or "")
    supplier_code = (supplier_profile or {}).get("supplierCode")
    supplier_description = (supplier_profile or {}).get("description")

    print(f"\n{'write':<26} {'persona':<9} {'status':>6} {'n':>4} "
          f"{'p50':>8} {'p95':>8} {'max':>8}")
    print("-" * 78)

    for label, persona, method, path, payload, needs_etag in WRITES:
        token = tokens.get(persona)
        if token is None or ("{supplierCode}" in path and supplier_code is None):
            print(f"{label:<26} {persona:<9} {'skip':>6}")
            continue

        resolved = path.replace("{supplierCode}", supplier_code or "")
        if "description" in payload and payload["description"] is None:
            payload = {**payload, "description": supplier_description}
        measure_write(method, resolved, payload, token, arguments.warmup, needs_etag)
        samples, status = measure_write(method, resolved, payload, token, arguments.iterations, needs_etag)

        row = (label, persona, status, len(samples),
               statistics.median(samples), percentile(samples, 0.95), max(samples))
        results.append(row)
        print(f"{label:<26} {persona:<9} {status:>6} {len(samples):>4} "
              f"{row[4]:>8.1f} {row[5]:>8.1f} {row[6]:>8.1f}")

    non_2xx = [r for r in results if not 200 <= r[2] < 300]
    if non_2xx:
        print("\n!! endpoints that did not return 2xx - their timings measure a refusal, not a read:")
        for row in non_2xx:
            print(f"   {row[0]} -> {row[2]}")

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
