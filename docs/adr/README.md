# Architecture Decision Records (ADRs)

ADRs capture significant, hard-to-reverse decisions with their context and consequences.
The consolidated baseline lives in
[`../architecture/00-foundational-decisions.md`](../architecture/00-foundational-decisions.md);
individual ADRs below record the reasoning behind specific choices and may supersede one another.

## Format

Each ADR: `NNNN-title.md` with sections **Status · Context · Decision · Consequences · Alternatives**.
Status is one of `Proposed | Accepted | Superseded by NNNN | Deprecated`.

## Index

| ADR | Title | Status |
|---|---|---|
| 0001 | Independent stack from ERPNext (.NET + React + PostgreSQL) | Accepted |
| 0002 | Clean Architecture + Vertical Slices; direct handlers (no MediatR) | Accepted |
| 0003 | Bespoke design system on Tailwind v4 + Radix (no UI template kit) | Accepted |
| 0004 | Async-by-default ERP integration via ACL + Outbox | Accepted |
| 0005 | RBAC via `resource.action` permission claims + policy handlers | Accepted |
| 0006 | Arabic-first, RTL-native UI with CSS logical properties | Accepted |

> Individual ADR files are added as decisions are formalized; the six above are recorded in the
> foundational decisions brief and will be extracted into standalone records during Phase 1.
