# Operations: what consumes the telemetry

P12 item 25 states the gap in one line: *traces, metrics and logs are emitted and nothing consumes them.*
This directory is the consumer, committed here rather than configured in a monitoring UI, so the decision
about what is worth waking somebody for can be reviewed beside the code that produces the numbers.

| File | What it is | How it is used |
|---|---|---|
| `alerts/mots-portal.rules.yml` | Prometheus alerting rules | Load with `rule_files:` in `prometheus.yml`. Validate with `promtool check rules ops/alerts/mots-portal.rules.yml` — **not run here**, because `promtool` is not installed in this repository's toolchain; the file has been parsed as YAML and its expressions written against instruments that exist, which is not the same as promtool's own PromQL check |
| `dashboards/mots-portal.json` | Grafana dashboard | Import against the Prometheus datasource that scrapes `/metrics` |

## Wiring it up

The application publishes a Prometheus exposition at **`/metrics`** (`MapPrometheusScrapingEndpoint`,
anonymous — the same posture as `/health`). A scrape config is all that is needed:

```yaml
scrape_configs:
  - job_name: mots-portal
    metrics_path: /metrics
    static_configs:
      - targets: ["mots-portal:8080"]
```

The `job_name` matters: `MotsPortalDown` matches `up{job="mots-portal"}`. The readiness alert additionally
needs a blackbox probe of `/health/ready` — the application cannot report its own unreachability, which is
the whole reason that alert is expressed over a probe rather than a gauge.

## The three instruments these files are built on

Every rule and every panel references a metric this application actually publishes. A rule over an absent
series is worse than no rule at all: it never fires, and on a dashboard it looks exactly like one that never
needed to.

- **`http_server_request_duration_seconds`** — ASP.NET Core's own instrumentation, tagged by route, method and
  status. Request rate, error ratio and latency all come from this one histogram.
- **`mots_rate_limit_rejections_total`** — `AppMetrics.RateLimitRejections`, tagged by surface and layer. A
  security-relevant event rather than an HTTP-shape fact, which is why the app publishes it itself.
- **`mots_outbox_backlog`** — `OutboxBacklogGauge`. A fact about a table, not about a request: Pending rows
  that do not drain mean nobody is being notified, even though nothing has been lost.

## What is deliberately absent

**Write-path latency SLOs.** `perf/BASELINE.md` covers 18 **reads** and says so. There is no measured
write-path p95 (P12 item 22), so the dashboard's latency panel is filtered to `GET` and labelled as reads —
inventing a write threshold would be asserting a target nobody has measured.

**Front-end field metrics.** LCP and INP under load (item 23) need a real browser under real traffic; nothing
in this repository measures them, and a panel with no series behind it is worse than an empty space.

**Business alerting.** Tenders closing with no bids, suppliers suspended by BRULE-023, categories with no
active supplier — those are somebody's morning report, and the Ministry's own screens (SCR-600, SCR-604)
answer them. None of them is a 3am page.

**Anything about the ERP adapter.** The only `IOutboxTransport` is a logging stand-in, asserted deliberately
by `ErpSyncVacuityTests`. An alert on ERP sync failure would be monitoring a stub and reporting health.

## What still needs a person

Alerts and a dashboard are the mechanism. Three P12 items remain, and each needs a human rather than a
config file:

- **OWASP ASVS L2 review** (item 21). Its automatable half now exists — `AuthorizationFuzzTests` sends real
  requests as every persona that lacks each route's permission and requires a refusal, and treats a 5xx as a
  failure because work before the gate is both a leak and a denial-of-service surface. The review itself is
  still a review.
- **WCAG 2.2 AA audit, both languages** (item 24). `axe` runs on every build; an audit is a person with a
  screen reader.
- **A load test to fill in items 22 and 23.** Once one exists, the write-path panel and its alert belong here.
