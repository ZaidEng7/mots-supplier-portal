# Read-path latency — first measurement (EPIC-26)

Run: `python3 perf/baseline.py --iterations 30`

## What this is

The documented targets — **p95 < 300 ms reads, < 800 ms writes** — appear in four documents and had
never been measured against anything. `COMPLETION-INVENTORY.md` §3.3 recorded that plainly and said no
claim should be made either way. This is the first measurement, so the number stops being unknown.

18 read endpoints, one persona each, 30 samples after 3 discarded warm-up requests. Milliseconds,
nearest-rank percentiles (not interpolated — with 30 samples an interpolated p95 invents a value between
two real measurements).

| endpoint | persona | p50 | p95 | max |
|---|---|---:|---:|---:|
| procurement dashboard | officer | 6.5 | 17.3 | 19.1 |
| comparison matrix | officer | 8.6 | 11.4 | 24.0 |
| rfq list | officer | 1.6 | 2.2 | 3.4 |
| rfq detail | officer | 6.5 | 8.1 | 8.8 |
| evaluation read | officer | 5.0 | 5.8 | 6.1 |
| supplier dashboard | supplier | 11.9 | 15.4 | 21.6 |
| supplier profile | supplier | 7.3 | 10.4 | 11.2 |
| my proposals | supplier | 2.4 | 3.1 | 3.7 |
| review queue | reviewer | 3.8 | 5.2 | 6.5 |
| review dashboard | reviewer | 4.4 | 4.9 | 5.1 |
| ministry overview | ministry | 3.6 | 4.4 | 4.6 |
| search (one term) | officer | 3.2 | 6.9 | 10.5 |
| audit search | admin | 1.8 | 3.2 | **177.8** |
| jobs monitor | admin | 6.4 | 11.1 | 12.7 |
| outbox monitor | admin | 2.5 | 3.8 | 4.9 |
| erp sync monitor | admin | 2.2 | 2.8 | 3.5 |
| storage settings | admin | 6.0 | 12.1 | 16.4 |
| security posture | admin | 2.4 | 4.8 | 6.2 |

Every endpoint returned 2xx. The script prints a warning for any that did not, because a fast 404 or 403
is not a fast read and a baseline full of them would look excellent.

## What this is NOT

**It does not show the targets are met.** Four reasons, and each one alone is enough:

1. **The dataset is tiny.** Six RFQs, nine users, five suppliers, three proposals, one award. The
   interesting reads here — the procurement dashboard and the comparison matrix — fan out across
   aggregates, and their cost is a function of how much there is to fan out over. At this size they are
   measuring query planning, not query work.
2. **There is no concurrency.** One request at a time, sequential. p95 under load is a different
   statistic from p95 of a quiet loop, and it is the one the target means.
3. **It is a developer laptop.** Same machine as Postgres, no network, warm page cache, `Debug`
   configuration.
4. **Writes are not measured at all.** The `< 800 ms` half of the target has no number here. Writes
   change state, so measuring them repeatedly needs either a disposable database per run or a script that
   can undo itself, and neither is written.

So: the read paths are not obviously slow, and nothing here licenses saying they are within target.

## The one number worth looking at

`audit search` has a p50 of 1.8 ms and a **max of 177.8 ms** — a hundred-fold spread that no other
endpoint shows. Almost certainly the first-call cost of a query plan or an index being read in, since the
warm-up discards only three requests and this outlier landed later in the run. It is recorded rather than
explained: guessing at a cause from one sample is how a performance myth starts. Worth a second look with
more iterations before anyone optimises anything.

## Next steps, in the order they matter

1. **A realistic dataset.** Hundreds of RFQs and thousands of proposals, generated. Without it every
   number above is a floor.
2. **A real load tool.** k6 or NBomber, modelling concurrency, ramp-up and think time. This script is
   standard-library-only on purpose — it needs nothing installed and can run anywhere — but it is a
   stopwatch, not a load generator.
3. **Write paths**, once there is a way to reset state between runs.
4. **Scheduled, not gating.** §3.3's own advice, and it is right: latency assertions on a shared CI runner
   produce flakes, and a flaky gate gets disabled. A tracked trend catches regressions without that.

## Note for whoever runs it next

Signing in six times can trip the auth limiter (NFR-SEC-009: ten attempts a minute), which the first run
of this script reported as "could not sign in". It now names the status and waits out the window instead —
if you see a 429, something else has been authenticating against the same server.
