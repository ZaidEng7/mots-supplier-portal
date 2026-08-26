# OWASP ASVS L2 Review — Password & Token Handling

**Date:** 2026-08-26
**Scope:** Password/token handling implemented through Sprint 1 (M0) and Sprint 2 (M1) gap-closure —
registration, email verification, login, refresh-token rotation, forgot/reset password, MFA (TOTP)
enrollment. Cross-referenced against [`SECURITY-ARCHITECTURE.md`](SECURITY-ARCHITECTURE.md) §1
(design intent) and OWASP ASVS 4.x Level 2, chapters V2 (Authentication), V3 (Session Management),
V7 (Error Handling & Logging), V8 (Data Protection).

**Method:** Direct code inspection of `Infrastructure/Auth/*`, `Infrastructure/Identity/*`,
`Api/Endpoints/AuthEndpoints.cs`, `Api/Program.cs`, plus the live curl/browser verification already
performed this session (register→verify→login→refresh→MFA→reset flows against a real Postgres
instance). This is a code-level control review, not a penetration test.

## Findings

| # | ASVS control | Requirement | Status | Evidence |
|---|---|---|---|---|
| 1 | V2.1.1–V2.1.9 | Password length ≥8, complexity, no silent truncation | **PASS** | `Program.cs`: `RequiredLength=10`, upper/lower/digit/non-alphanumeric required. |
| 2 | V2.1.7 | Reject known-breached passwords | **FAIL** | No breach-check (HIBP k-anonymity or local dataset) implemented, despite being called out as an explicit design intent in `SECURITY-ARCHITECTURE.md` §1.4. |
| 3 | V2.2.1 | Anti-automation on authentication endpoints (rate limiting) | **FIXED** | Added a per-IP fixed-window rate limiter (`AddRateLimiter`, 10 req/min, policy `auth-strict`) applied to `/login`, `/forgot-password`, `/reset-password`, and the whole `/registrations` group (register + verify). Verified live: 10 rapid login attempts from the same IP succeed (401 on wrong credentials), the 11th and 12th return `429 Too Many Requests`. |
| 4 | V2.2.1 (lockout) | Account lockout after repeated failures | **PASS** | `MaxFailedAccessAttempts=5`, `DefaultLockoutTimeSpan=15min`, verified live (`login_locked_out` audit event exists in `LoginHandler.cs`). |
| 5 | V2.3.1 | Verified contact info before activation | **PASS** | `RegisterSupplierHandler` requires email confirmation; `LoginHandler` rejects unverified accounts with `email_not_verified` before issuing tokens — verified live via curl this session. |
| 6 | V2.5.1 | Secure password-reset flow, single-use token | **PASS** | `ForgotPasswordHandler`/`ResetPasswordHandler` use `UserManager.GeneratePasswordResetTokenAsync`/`ResetPasswordAsync` (single-use, Identity-validated). |
| 7 | V2.5.1 | No account enumeration via reset/registration responses | **PASS** | `forgot-password` returns the identical `{"message":"if_account_exists_email_sent"}` regardless of whether the account exists — verified live (both an existing and a non-existent email produced byte-identical 200 responses). |
| 8 | V2.5.4 | Reset token short-lived, scoped to purpose | **FIXED** | Registered a dedicated `PasswordReset` `DataProtectorTokenProvider<AppUser>` with `TokenLifespan = 30 minutes`, set as `options.Tokens.PasswordResetTokenProvider`, leaving the default provider (still 24h) exclusively for email-confirmation links as originally designed. Verified live: a token generated after the fix still validates and resets the password successfully. |
| 9 | V2.5.5 | Reset invalidates existing sessions | **PASS** | `ResetPasswordHandler` revokes every active `RefreshToken` for the user on success — verified live (pre-reset refresh cookie returned 401 after reset). |
| 10 | V2.8.1–V2.8.6 | TOTP-based MFA, correct window, recovery codes | **PASS** | `EnrollMfaHandler`/`ConfirmMfaEnrollmentHandler` use ASP.NET Identity's built-in TOTP provider (RFC 6238 compliant) and recovery-code generation — verified live end-to-end with a real generated TOTP code. |
| 11 | V3.2.1 | Session (refresh) token unpredictable, bound to session | **PASS** | `TokenHasher` generates a 256-bit random opaque token; only its hash is persisted. |
| 12 | V3.2.3 | Session tokens not exposed in URL/JS-readable storage | **PASS** | Refresh token travels only as an `HttpOnly`, `Secure` (non-dev), `SameSite=Strict` cookie, path-scoped to `/api/v1/auth`. Access token is short-lived (15 min) and held only in JS memory (Zustand store), never `localStorage`. This was a mid-session fix (originally the refresh token was JSON-body-readable) — now matches the documented design. |
| 13 | V3.3.2 | Re-authentication / token rotation on privilege-relevant events | **PASS** | Refresh-token rotation with family-based reuse detection: presenting an already-rotated token revokes the entire family (`RefreshTokenHandler`). |
| 14 | V6.2 (indirectly, algorithm choice) | Signing algorithm matches documented design | **DEVIATION (accepted)** | `SECURITY-ARCHITECTURE.md` §1 specifies **RS256** (asymmetric, so services can verify without the signing key). The shipped implementation uses **HS256** (`JwtTokenService.cs`) with a single shared symmetric key. Functionally secure for a single-API deployment (matches ASVS V6.2's "approved algorithm" bar), but is a documented-vs-built mismatch that will need revisiting before any second service needs to verify tokens independently. |
| 15 | V4 (row-scoping) | JWT claims correctly enforce row-scoping | **PASS** | Found and fixed a real bug this session: the default JWT inbound-claim mapper silently renamed `sub` to the long `ClaimTypes.NameIdentifier` URI, making `IScopeContext.UserId` always null for every authenticated request. Fixed via `options.MapInboundClaims = false`; re-verified MFA enroll and the existing permission-gated audit endpoint both still enforce correctly post-fix. |
| 16 | V7.1.1–V7.1.4 | No sensitive data (passwords, tokens) in logs | **PARTIAL** | Manual inspection of every `LogAsync`/`ILogger` call site in the auth path found no password/token values logged. However there is **no explicit Serilog destructuring/scrub policy** (deny-list for `password`, `token`, `authorization`, `otp` etc., as specified in `SECURITY-ARCHITECTURE.md` §4) — the current safety is "nobody has logged one yet," not an enforced control. A future accidental `logger.LogInformation("{@request}", request)` on a DTO containing a password would leak it. |
| 17 | V2.1.1 (audit trail) | Auth events audit-logged, append-only | **PASS** | `login_succeeded`, `login_failed`, `login_locked_out`, `password_reset`, and registration events are written to the append-only `AuditLog` via `IAuditLogger`. |

## Summary

- **10 PASS**, **2 FIXED during this review**, **1 open FAIL**, **1 PARTIAL**, **1 accepted
  deviation** (17 controls reviewed).
- #3 (rate limiting) and #8 (reset-token lifespan) were real, concrete gaps against the
  documented design — not edge cases — and have been fixed and re-verified live as part of
  closing out this review, not deferred.
- #15 was a genuine bug (not a documentation gap) found and fixed as a direct result of doing
  this review's live verification pass, not from reading the code alone — the endpoint returned
  401 and looked like an auth failure, but was actually silent authorization-claim breakage.

## Recommendation

Do **not** treat this review as "ASVS L2 passed" outright — it found and fixed two real
control gaps in the process, which is what the review is for, not a rubber stamp:

- **#2 (breach-password check)** is the one remaining open FAIL. It requires an external
  dependency decision (local dataset vs. HIBP range API call) that isn't a five-minute fix and
  touches the `[ASSUMPTION on external call]` flagged in the source doc — recommend tracking
  this as a Sprint 3+ backlog item rather than blocking the gate on it, but it must not be
  silently dropped from the backlog.
- **#16 (log redaction policy)** is a defense-in-depth gap, not an active leak — recommend
  adding an explicit Serilog destructuring/deny-list policy early in Sprint 3 before more log
  call sites accumulate.
- **#14 (HS256 vs RS256)** is a reasonable, documented engineering trade-off for a
  single-API deployment today; revisit when a second service needs independent token
  verification.
