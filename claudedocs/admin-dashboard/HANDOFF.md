# Admin dashboard Phase 1: handoff to the cloud session (2026-10-03)

The owner (ZaidEng7) approved everything below. They are asleep: work autonomously and stop only for the cases listed under "When to stop".

## The plan: 5 PRs
| PR | Content | State |
|---|---|---|
| 1 | Incoterms table on the Reference Data page | merged, #234 (071850f) |
| 2 | Session counts, platform ERP retry/status, ERP audit rows, scan re-run guard | merged, #235 (619bf41) |
| 3 | Sign-in security | branch `fix/admin-sign-in-security` (32c2846, pushed, no PR yet) plus unfinished review fixes on `wip/pr3-review-fixes` |
| 4 | Dashboard backend: GET /api/v1/admin/dashboard, sections, needs-attention, POST /api/v1/admin/scans/retry | skeleton done on `wip/pr4-skeleton` (378c177); sections unfinished on `wip/pr4-*` |
| 5 | Dashboard page plus routing | mock-up APPROVED; build after PR 4 merges |

**Standing permission.** Merge each Phase 1 PR yourself (`gh pr merge <n> --squash`) once its review round and CI are green, and main's CI was green before. GitHub auto-merge is disabled on the repo. Report each merge.

## Branches to pick up (all pushed)
- `fix/admin-sign-in-security`, PR 3 base. It already contains:
  - stored sign-in and session audit rows (SessionAuditActions)
  - `refresh_rotated` no longer written
  - the 10 s refresh grace (`RefreshTokenResult.Superseded`: 401, cookie kept)
  - sign-out revoking the family plus a `logout` row
  - frontend single-flight refresh
  - five other lost audit rows stored
  - the supplier trail excluding session actions
- `wip/pr3-review-fixes`: UNFINISHED and UNTESTED fixes for the 8 confirmed review findings. The full task list is in `pr3-review-fixes-prompt.js`. Verify every item, finish, then merge into `fix/admin-sign-in-security`:
  - **A. The race.** SessionLock serialises rotation, sign-out and revocations per FamilyId. Rotation claims its token atomically. Revocations never overwrite RevokedAt. The `logout` row is written only when something was revoked. Add concurrency tests.
  - **B.** Sign-out always clears the cookie and answers 204, even if the DB throws.
  - **C.** Make ResetPassword and Disable one transaction, or correct the docs.
  - **D.** Single-flight test for a failed shared refresh.
  - **E.** In-window test: a revoked token with no successor gives Invalid with the cookie cleared.
  - **F.** A test pinning the SessionAuditActions literals, plus ActorUserId.
  - **G.** ASVS doc dates: the rows were dropped from 2026-08-29 (MSP-64) until 2026-10-02. Verify with `git log -S`.
  - **H.** Headers and SECURITY-ARCHITECTURE describe the grace; GetAdminOverviewHandler wording; AuditLogger caller list.
  - **I.** Superseded 401 carries `{"error":"refresh_superseded"}`, and the frontend retries the refresh ONCE on that code only, inside the single-flight slot.
- `wip/pr4-skeleton` (378c177): the dashboard frame (endpoint, section results ok/failed/hidden, parallel child scopes, tests). The section branches below are based on it, except scan-retry.
- `wip/pr4-system-health`, `wip/pr4-erp-block`, `wip/pr4-people-access`, `wip/pr4-security-activity`: UNFINISHED. All are based on 378c177.
- `wip/pr4-scan-retry`: UNFINISHED, based on 32c2846.
- `wip/admin-dashboard-handoff`: this folder, documentation only. Never merge it.

## Next steps
1. **PR 3:**
   - Finish and verify `wip/pr3-review-fixes`, then merge it into `fix/admin-sign-in-security`.
   - Merge `origin/main`.
   - Run Unit, Architecture and the full Integration suite, plus frontend vitest, typecheck and lint.
   - Push, open PR 3, check CI, merge when green.
2. **PR 4:**
   - Finish each section on top of the skeleton per `pr4-spec.md`. Where the build list and the QUESTIONS section differ, the QUESTIONS win (thresholds: 5-minute jobs 15 min, hourly 3 h, daily 26 h; and the others listed there).
   - Merge the sections and build needs-attention (item 10, including the review-deadline item and the A-5 amendment in DECISIONS-TAKEN.md).
   - Rebase or merge onto main after PR 3 lands.
   - Run the review round, fix confirmed findings, run the full tests, open the PR, merge when green.
3. **PR 5:** build the page from the approved mock-up (`mockup-Main.dc.html`; canvas https://claude.ai/artifact/93mZRvm3ufAhamo4kB4Rbx).
   - Arabic and English. Light theme, and dark through the OS setting.
   - Five sections, each with its own ok, failed or hidden state.
   - Refresh, "Check storage and scanner", a 60 s auto-refresh, and "Updated at" plus the version from /api/v1/meta.
   - Plan item 12 routing: /back-office/dashboard renders it for admin.users.manage holders; /back-office/admin redirects; remove that nav row; retire GET /api/v1/admin/overview, AdminOverviewPage and its tests while keeping the adminOverview.tables keys; update e2e fixtures and the a11y route count.
   - The sidebar in the mock-up mirrors the real BACK_OFFICE_NAV.
4. After Phase 1, the agreed follow-ups:
   - Audit the 8 unaudited admin actions, and stop "Run now" from starting supplier-erp-push.
   - A task for the two-factor enrolment dead end.
   - A Staff-page toast for ORGANIZATION_REQUIRED.

## How every PR is done (the owner's rules)
- **Plan-first** is already satisfied for Phase 1. Do not widen scope.
- **Comment style** (the owner's explicit rule): comments go ON TOP of the code block they describe, as plain prose, with no comments between lines of code. Match each file's header style. Some tests read prose from source, so run Unit and Architecture after any comment change.
- **Mutation-proof tests:** prove each new test fails against the plausible wrong implementation. Clean up shared integration state.
- **Review round:** several reviewers, each finding checked by an adversarial verifier. Fix the confirmed findings before opening the PR.
- **No** schema changes, **no** new dependencies.
- **Never write to the production ERP** (http://9.160.106.141) by any path. The dashboard must never call any ERP. The only outside calls are the MinIO ping (5 s) and the on-demand probe (10 s).
- **Never show award amounts** in Phase 1.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. PR bodies end with the Claude Code line.
- **Do not poll CI in a loop.** Check it at natural points.

## Build notes
- **Backend:**
  - `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` where the dotnet SDK lives there; otherwise use the system dotnet.
  - Integration tests use Testcontainers and need Docker. Without Docker, run Unit and Architecture plus targeted checks, and rely on CI's Backend job for Integration. Say so in the PR.
- **Frontend:** needs Node 22. `npm ci` in src/frontend, then `npx vitest run`, `npm run -s typecheck`, `npm run -s lint`.
- **Flaky test:** `ProposalEndpointsTests.A_second_line_can_be_priced_without_colliding_with_the_first` failed once under load and passes alone. It is unrelated.
- **The owner's laptop is off:** the local API, the local database (the real supplier registry) and the hourly ERP sync are not reachable from the cloud, and nothing here needs them.

## When to stop and leave a note for the owner
- A CI failure you cannot fix with confidence.
- A security decision that the 15 confirmed answers do not cover.
- Anything that would touch the production ERP, the real registry, or need a schema change.
