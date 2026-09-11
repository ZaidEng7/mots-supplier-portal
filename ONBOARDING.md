# Onboarding — MOTS Supplier Portal

> For an engineer who starts on Monday and has never seen this repository.
>
> This is not an inventory. It is the set of things you cannot infer in a week: why the code is shaped
> the way it is, which rules are actually enforced and by what, and the traps that will each cost you a
> day. Everything here carries a file reference so you can check it rather than believe it.
>
> Sourced from a subsystem-by-subsystem read of the repository on 2026-09-11. Where a claim surprised
> the author it was verified against the source before it was written down; where something could not
> be established it is listed at the end rather than guessed.

---

## 1. What the product is

A procurement portal for the Syrian Ministry of Transport. Two audiences, and almost every design
decision follows from the difference between them:

- **A procurement officer** is inside the product all day, moving a tender from draft to award.
- **A supplier company** uses it a few times a year, under deadline, to complete a legal application
  and bid.

Both languages ship together, Arabic and English, with full right-to-left support. `PRODUCT.md` is the
shortest honest description of scope; `docs/product/` holds the specification.

---

## 2. Day one

`RUNBOOK.md` is the setup document and it was written by running every command against a clean
database. Follow it. Three corrections to it, all costly:

| It says | It is |
| --- | --- |
| Node 20+ | **Node 22.** Node 20 fails with `webidl.util.markAsUncloneable is not a function` from vitest, tsc and Playwright, which reads as a broken dependency. CI pins 22 (`.github/workflows/ci.yml`). There is no `.nvmrc` and no `engines` field to tell you. |
| `dotnet` prefixed with `DOTNET_ROOT` on this machine | Still true, and every backend command needs it. |
| — | `npx playwright test` on a fresh clone fails during collection: three projects read `storybook-static/index.json`, which is gitignored. Run `npm run build-storybook` first. |

`ASPNETCORE_URLS=http://localhost:5080` is not optional. Three ports disagree in the repository and the
SPA hard-defaults to 5080 (`src/frontend/src/api/auth.ts`). The runbook says so; it is worth repeating
because the symptom is a working page where every request fails.

`.claude/launch.json` carries both servers with the right environment already set.

---

## 3. The shape of the system

**Backend.** Four projects, clean layering, enforced by NetArchTest:
`Domain ← Application ← Infrastructure`, and `Api → Application + Infrastructure`.

- **Domain** is sealed aggregate roots with private constructors and private setters. Each owns its
  state machine as a run of explicit guards that throw `DomainException`. No base classes, no domain
  events, no mediator.
- **Application** holds no behaviour at all — DTOs, command records, handler *interfaces*, and result
  unions as `abstract record` hierarchies. All 87 handlers live in **Infrastructure**.
- **Api** is Minimal APIs only, no controllers. One `Map*Endpoints()` extension method per
  resource family, each opening an `app.MapGroup("/api/v1/...")` — 27 groups across the endpoint files.

**The split you must hold in your head:** a rule the aggregate can see, the aggregate enforces. A rule
that needs a second aggregate is checked by the handler *before* it calls the aggregate. Sixteen domain
doc comments state it. Nothing enforces it.

**Frontend.** React 19, TanStack Router and Query, Tailwind 4, Radix, recharts, i18next. One
hand-written route tree (no generated `routeTree`), one fetch wrapper that owns auth retry, ETag
preconditions and idempotency keys, and a two-layer token system.

**Where the reasoning lives.** This repository documents itself in comments, and they are normative
rather than decorative. A comment that reads like an argument usually is one, and usually records a
defect that shipped. Read the comment before changing the line.

---

## 4. Eight rules, and what catches you breaking them

| Rule | What catches you |
| --- | --- |
| Every endpoint states its authorization intent out loud — `.RequirePermission`, `.RequireAuthorization`, or an explicit `.AllowAnonymous()`. The deny-by-default fallback policy is a backstop, not a declaration. | An integration test that enumerates the live endpoint table |
| Never build a problem+json body by hand. Return a plain result and let the middleware conform it. | A per-status integration theory over the real responses |
| Every `string?` filter parameter goes through a `FilterValues.*` guard. A `string?` binds anything, and an unrecognised value used to fall through as "no filter". | A Roslyn source scan in the architecture suite |
| Components read **semantic** tokens only — never `--n-500`, `--brand-500`. Tailwind's own type, weight and shadow scales are banned too. | `src/frontend/src/styles/tokenConformance.test.ts` |
| Every route is offered by a navigation row or carries a written exemption of at least 40 characters. | `src/frontend/src/shells/reachability.test.tsx` |
| `PERMISSIONS.md` is generated. Never edit it. | `PermissionCatalogueTests` fails on exact content mismatch |
| Every sweep asserts its own denominator before it asserts its rule, and ships a control that proves the matchers can still go red. | Convention, written into each test file |
| Exemption lists are typed out by hand with a reason, never pattern-matched, and are checked in both directions. | A dedicated test per sweep |

That last pair is the house style and it is worth understanding before you write a test here. The
recurring defect this repository has paid for is that **a check which measures nothing looks exactly
like a check that passes**. Several guards in this codebase were found asserting facts about files they
no longer read.

---

## 5. The traps

Each of these is real, each has already cost someone a day, and none of them announces itself.

**Row-scoping has no enforcement mechanism.** Every handler re-filters on the caller's own
`SupplierId` or `OrganizationId` from the token. There is no EF query filter, no base handler, no
architecture test — `HasQueryFilter` appears zero times in `AppDbContext`. A new handler that forgets
the predicate fails nothing. It is the risk register's only Critical entry.

**Two personas deliberately have no organisation:** the bootstrap administrator and the Ministry
viewer. Every organisation-scoped handler returns null or an empty page for them. Holding the
permission is not the same as being in scope, and the difference shows up as an empty screen or a 404,
never as a permission error.

**`IsInRole()` returns false for everybody**, including the system administrator. Roles are issued in a
custom `roles` claim and `RoleClaimType` is never set. It compiles, it reads correctly, and it refuses
everyone.

**`fallbackLng` is `'ar'`.** A key present in Arabic and missing from English renders **Arabic text to
an English reader** — no key, no blank, no error. The reverse direction is loud. `keyParity.test.ts` is
the only thing comparing the two catalogues.

**Frontend unit tests run inside the `Backend (.NET)` CI job**, not `Frontend (React)`, because the
Sonar scanner has to read the coverage from disk beside it. A broken vitest test reddens the backend
check, and branch protection binds to job names.

**`npm run build` is the real gate, not vitest.** The repository has twice recorded vitest passing
while the type-checking build failed.

**The integration suite shares one database across all 130 test classes** with no truncation between
tests. A test that asserts a row count or "page one holds exactly these rows" is asserting against
every other test's leftovers. Two fixture settings exist solely to stop quiet false passes —
`DevSeed:Enabled=false` and `Jobs:EnableRecurring=false` — and re-enabling either breaks the suite in
ways that look like product regressions.

**`fixture.CreateClient()` attaches `If-Match` and an idempotency key to every mutation.** A
concurrency test written with it passes for the wrong reason. `CreateRawClient()` is the honest one.

**`ExecuteUpdateAsync` bypasses the change tracker**, so it neither bumps nor guards a row version.
One production path already does this on a versioned root.

**ClamAV is fail-closed and nothing waits for it.** On a fresh machine it accepts TCP before its
signature database has loaded, so a clean PDF is rejected. It is not one of the four readiness checks.

**Adding a route fails a hard-coded count** — `expect(routes.length).toBe(70)` in the accessibility
sweep. That is deliberate: the number moves only when a person also names the new screen.

---

## 6. Changing something safely

Run these from `src/frontend` with Node 22 on `PATH`:

```bash
npx tsc -b --noEmit && npx vitest run && npm run lint && npm run build
```

End-to-end, after a Storybook build:

```bash
npm run build-storybook && npm run test:e2e
```

Backend, from the repository root with `DOTNET_ROOT` set:

```bash
dotnet test src/backend/Tests/Unit/MotsSupplierPortal.Tests.Unit.csproj
dotnet test src/backend/Tests/Integration/MotsSupplierPortal.Tests.Integration.csproj
```

The integration suite takes about eight minutes and needs Docker up.

**Before you open a pull request**, know that the quality gate is a CI step and not the SonarCloud
badge. SonarCloud's own check is advisory; do not read a green one as a clean project. New-code
coverage must clear 45%, a floor that goes up and never down.

---

## 7. Which document answers which question

| Question | Read |
| --- | --- |
| How do I run it? | `RUNBOOK.md` |
| What is it for? | `PRODUCT.md`, then `docs/product/` |
| Who can do X? | `PERMISSIONS.md` (generated) |
| Why is it like this? | `DECISIONS-TAKEN.md`, and the comment above the line |
| What is not finished? | `COMPLETION-INVENTORY.md`, `BACKLOG-REMEDIATION.md` |
| What does the pipeline do? | `.github/workflows/ci.yml`, which reads as a record of instruments found measuring nothing |
| Why is that excluded from analysis? | `.sonar/analysis-scope.properties`, one argument per exclusion |

`docs/` is the specification set and is treated as read-only here. Where the implementation departs
from it, the code says so and says why.

---

## 8. Known stale documentation

Recorded rather than fixed, because each is someone's decision to make.

- **`RUNBOOK.md` says Node 20+.** It is Node 22.
- **"Nine versioned roots" is wrong everywhere it appears.** There are thirteen.
  `AppManagedVersion.cs:9` still says "all nine".
- **Comments mentioning `xmin`** describe the design that was replaced by the application-managed row
  version. One sits directly above a call to the replacement.
- **The migration history no longer matches any database created before 2026-09-08.** Fifty-nine
  migrations were squashed into one baseline; an older database fails partway through
  `dotnet ef database update`.
- **`vitest.config.ts` says there are zero frontend unit tests.** There are 116 test files.
- **`docs/architecture/DATABASE-MODEL.md` §1 prescribes conventions the implementation consciously
  departs from**, and it reads as authoritative.
- **`src/backend/Tests/Integration/TestResults/` holds 343 MB of local `.trx` files.** Gitignored, so
  harmless, but it will confuse any tool you point at the repository.

## 9. What this document could not establish

- The actual GitHub branch-protection configuration. The pipeline asserts which check names are
  required; that assertion is unverifiable from inside the repository.
- Where the Hangfire server runs in a real environment. It is registered in-process with the API
  unconditionally, and no deployment document resolves whether that is intended at scale.
- Whether anything enforces the right-to-left logical-property convention. Zero physical directional
  utilities were found; no guard was found either.
- Why the evaluator layout carries no persona check when both other layouts do. No comment explains
  the asymmetry.
