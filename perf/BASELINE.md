# Read- and write-path latency — first measurement (EPIC-26)

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
| procurement dashboard | officer | 7.0 | 8.8 | 12.3 |
| comparison matrix | officer | 5.2 | 7.2 | 7.3 |
| rfq list | officer | 4.6 | 10.1 | 11.3 |
| rfq detail | officer | 7.5 | 10.0 | 10.5 |
| evaluation read | officer | 6.2 | 7.8 | 8.3 |
| supplier dashboard | supplier | 19.3 | 22.2 | 25.3 |
| supplier profile | supplier | 11.5 | 13.3 | 15.2 |
| my proposals | supplier | 2.4 | 3.2 | 4.2 |
| review queue | reviewer | 4.0 | 5.1 | 5.5 |
| review dashboard | reviewer | 5.1 | 6.2 | 6.3 |
| ministry overview | ministry | 6.0 | 7.5 | 16.3 |
| search (one term) | officer | 2.8 | 4.3 | 7.7 |
| audit search | admin | 1.4 | 1.9 | 2.0 |
| jobs monitor | admin | 6.9 | 13.1 | 18.4 |
| outbox monitor | admin | 2.4 | 3.1 | 5.1 |
| erp sync monitor | admin | 2.3 | 3.3 | 3.7 |
| storage settings | admin | 6.1 | 7.7 | 9.9 |
| security posture | admin | 1.7 | 2.2 | 2.3 |

Every endpoint returned 2xx, and this time that is a measured fact rather than a reading of the status
column. The script prints a warning for any that did not, because a fast 404 or 403 is not a fast read and
a baseline full of them would look excellent.

**Two things the previous table got wrong**, both found by re-running it during a walkthrough rather than
by reading it:

- **`evaluation read` was a 404.** It addressed `RFQ-DEMO-0004`, which the dev seed leaves at
  SubmissionOpen — an evaluation does not exist until a tender reaches UnderEvaluation. Its 5.0 ms p50 was
  the cost of the refusal, published as the fastest cross-aggregate read in the product. The row now
  addresses `RFQ-DEMO-0005`, and its first cold call against a warm database took **520 ms** before the
  warm-up discards settled it to the 6.2 ms above. That cold number is not in the table (the table is
  warm, deliberately), but it is the reason this correction is worth more than the three digits it changed.
- **`reviewer` and `admin` could not sign in.** The script held passwords from before the seeders
  converged on one dev fallback, so eight of the eighteen rows — including every admin read, the slowest
  in the product — were skipped. They are measured here.

## Writes (T-107)

The `< 800 ms` half of the target had no number at all. It has one now, with the same caveats as above
and one of its own.

| write | persona | p50 | p95 | max |
|---|---|---:|---:|---:|
| supplier profile edit (`PATCH /suppliers/{code}`) | supplier | 7.4 | 8.6 | 9.3 |
| notification preferences (`PUT /notifications/preferences`) | officer | 19.0 | 21.6 | 29.1 |

30 samples each, 3 discarded warm-up requests, same laptop.

**The supplier edit no longer brands the row it measures.** It used to set the description to
"Measured by perf/baseline.py", and that string was still sitting on `SUP-DEMO-0001`'s profile screen days
later, in place of the seeded description. It now writes back whatever the profile already held, which is
the same repeatable write without the graffiti.

**Why only two.** Every write measured here is repeatable against the same row: it sets a value to what
it already is, or to one the next iteration overwrites. That rules out the writes a reader would most
like to see — creating a tender, submitting a bid, executing an award — because measuring those thirty
times means leaving thirty tenders behind, and a baseline that changes the dataset it measures is not a
baseline. What is here is the ordinary editing traffic the product carries between those events.

**The ETag fetch is not timed.** §8.1 requires `If-Match` on these routes, and a caller already holds the
version from the read that showed them the thing they are editing. Charging the write for a `GET` it does
not make would measure the harness.

## What this is NOT

**It does not show the targets are met.** Four reasons, and each one alone is enough:

1. **The dataset is small.** 40 tenders, 31 suppliers and 18 awards on the database this run measured -
   larger than the six RFQs and one award of the first run, and still nothing like production. The
   interesting reads here — the procurement dashboard and the comparison matrix — fan out across
   aggregates, and their cost is a function of how much there is to fan out over. At this size they are
   measuring query planning, not query work.
2. **There is no concurrency.** One request at a time, sequential. p95 under load is a different
   statistic from p95 of a quiet loop, and it is the one the target means.
3. **It is a developer laptop.** Same machine as Postgres, no network, warm page cache, `Debug`
   configuration.
4. **The writes measured are the cheap half.** Two repeatable edits, not the transactional writes that
   matter — award execution touches an aggregate, an outbox row and an audit row in one transaction, and
   none of that is here. See "Why only two" above for why, and what it would take.

So: neither path is obviously slow, and nothing here licenses saying either is within target.

## The one number worth looking at

The previous run's `audit search` outlier — p50 1.8 ms against a max of 177.8 ms — **did not reproduce**.
This run has it at 1.4 / 1.9 / 2.0, the flattest row in the table. One sample, on a laptop, that never came
back: recorded here so nobody optimises against it, and not carried forward as a finding.

What replaces it is smaller and steadier: `supplier dashboard` is the slowest read at 19.3 ms p50, three
times the median row, on a dashboard that fans out across proposals, invitations and documents for one
supplier. Still two orders of magnitude inside the 300 ms target at this dataset size, so it is a note, not
a problem.

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
