# PR 4 spec: admin dashboard backend (items 7, 8, 9, 10, 13)

The owner confirmed every recommended answer below ("all recommended"). PR 1 (#234), PR 2 (#235) are merged; PR 3 (sign-in security: stored session audit rows, SessionAuditActions list, logout row, refresh grace) is on branch fix/admin-sign-in-security and is the base for this work.
Phase 1 never shows award amounts. No schema changes. The dashboard endpoint must NEVER contact the ERP (reads only DB rows and configuration); the only outside call is a MinIO ping capped at 5 s, plus an on-demand storage-and-scanner probe capped at 10 s.

Stage B, backend
7. [changed] GET /api/v1/admin/dashboard, gated on admin.users.manage.
   - Structure: permission flags are read once, then each section runs in parallel in its own child scope and DbContext. Synchronous Hangfire calls go inside Task.Run so the sections really run in parallel. Each section returns ok, failed or hidden. The endpoint never contacts the ERP. The only outside call is a MinIO ping capped at 5 s.
   - Housekeeping: add RowScopeGuard entries and regenerate PERMISSIONS.md.
   - System health:
     - Verdicts for all 8 jobs. Grace is 10 min for the 5-minute jobs, including supplier-erp-push, and 2 h for the hourly and daily jobs, including erp-supplier-sync. A last state of Scheduled counts as retrying. erp-supplier-sync links to /back-office/erp-import.
     - Hangfire queue: Retrying is the Scheduled count minus ErpSupplierSyncJob.RunAgainAsync jobs. Live servers come from heartbeats within 5 min.
     - EmailJobs that failed in the last 7 days, and those retrying.
     - Outbox.
     - SupplierDocuments only (not tender or bid files) in PendingScan with UploadedAt older than 15 min.
     - Pending migrations, by name.
     - Reference lists.
     - The purchase-order transport, labelled as such.
     - An on-demand storage-and-scanner probe capped at 10 s.
   - ERP block, hidden without admin.integrations.manage:
     - Connection: source, enabled, host, https, last test.
     - Hourly sync: last run, outcome, counts. Stale after 3 h while enabled; also flags an import that started and recorded no outcome.
     - Supplier push: switch, group, whether the host is on WriteHosts (yes or no, through a helper shared with ErpSupplierRegistrar.cs:197-209), waiting, Failed and stalled counts.
     - Each ERP sub-section fails on its own when a #230 or #232 migration is missing.
8. [changed] People and access:
   - Staff and supplier accounts counted separately, never summed, active and inactive apart.
   - Per-role counts over active users, plus a distinct-user total.
   - Sessions, using item 2's rule.
   - Pending invitations from unexpired tokens, staff and supplier shown separately.
   - Invited-never-signed-in, staff only: a staff_invited row and no RefreshTokens row ever, split by whether the link is still valid.
   - Locked out means LockoutEnd > now.
   - Two-factor required but missing, labelled "cannot sign in".
   - Organisations by type.
   - Supplier logins on '@erp-import.invalid' addresses, as an info count only.
   - Frontend: a "Locked until" badge on StaffPage.tsx:223-228, from the lockoutEnd the API already returns, with new Arabic and English keys.
9. [changed] Security and recent activity, gated on audit.read, landing after items 4 and 5.
   - Security, counts for 24 h and 7 days: login_failed, login_locked_out, login_mfa_failed, login_blocked_mfa_enrollment_required, refresh_reuse_detected, password_reset, staff_mfa_reset.
   - Sensitive changes: the 10 latest rows from a named allow-list. The list now also includes IntegrationConnectionUpdated, IntegrationSupplierCreationChanged, supplier.erp_push_retried and a manual ErpImportRun.
   - Recent activity: the 24 h count and the latest 10 User or Integration rows. Leave out item 4's session list and system bookkeeping: scheduled ErpImportRun and ErpImportCompleted rows, and supplier.erp_push_attempt_failed. System rows get a count of their own.
   - The actor's name comes from its own projection, not AuditLogEntryDto: a left join to Users, falling back to ActorLabel, then to "system".
   - Every action shown gets an Arabic and an English label, including the 16 new ERP actions.
   - Update the comment at AdminOverviewContracts.cs:9-11.
10. [changed] Needs attention, computed on the server from the other sections' results.
   - "All clear" appears only when every check ran and none fired. Otherwise the list names the checks that could not run.
   - Links go to page roots, and a link is shown only when the viewer holds that page's permission.
   - Jobs, outbox, award syncs and stuck scans go to /back-office/operations.
   - These go to /back-office/integrations, or /back-office/erp-import for holders of supplier.import.run:
     - a failed or stale hourly sync
     - a failed last connection test
     - a plain-http ERP address
     - the push switch on with the host not on WriteHosts, or with no group set
   - Failed or stalled pushes list up to 5 reference codes, each linking to /back-office/review/{code}, for holders of admin.integrations.manage.
   - An empty reference table goes to /back-office/reference.
   - Locked-out accounts, lapsed invitations and "cannot sign in" accounts go to /back-office/staff.
   - The review-deadline item appears only if Q2 is yes.
   - A security spike goes to /back-office/audit, for holders of audit.read.


Stage D
13. [changed] POST /api/v1/admin/scans/retry, gated on admin.users.manage, landing after item 6.
   - Takes up to 100 SupplierDocuments in PendingScan with UploadedAt older than 15 min, oldest first.
   - For each document: skip it if its scan job is Enqueued or Processing; otherwise delete its scan jobs in Failed and in Scheduled (waiting to retry, since Hangfire's default 10 retries apply), enqueue one new scan, and write document_scan_requeued with the caller as actor.
   - Saves once at the end, because AuditLogger does not save. Returns the number requeued and the number still pending.
   - Documents whose quarantine object is already gone (the move succeeded, the save failed) are reported, not requeued.
   - Add a RowScopeGuard exemption and regenerate PERMISSIONS.md.


QUESTIONS:
1. Save the security events? Still yes. It now also covers the five other lost rows in item 4.
2. Review-deadline item (reverses A-5): reworded. If chosen, show it on the admin dashboard only, and count Submitted and UnderReview cases past ReviewSla.TargetFor(enteredQueueAt, review.slaWorkingDays) in working days. Leave out InfoRequested, where the timer pauses, and do not use the queue's 48 h and 120 h calendar tones. Amend A-5 (DECISIONS-TAKEN.md:454-464) in the same PR and keep its "[recommended, awaiting procurement]" tag.
3. Invited-never-signed-in: yes, reworded to staff accounts only. Supplier invites are logged against the supplier, not the person (InviteSupplierUserHandler.cs:55), so they can be counted only while the token lives. The ERP-imported accounts are never counted.
4. The feeds show the actor and are gated on audit.read: yes. Also, the hourly sync's system rows stay out of the feeds and get a count of their own.
5. Thresholds: they still hold, with these additions:
   - The 5-minute jobs, including supplier-erp-push, are late after 15 min.
   - The hourly jobs, including erp-supplier-sync, are late after 3 h, and LastSyncAt older than 3 h while the connection is enabled counts as stale.
   - The daily jobs are late after 26 h.
   - A push is stalled when its next attempt is more than 15 min overdue while the switch is on, or when an in-flight marker is older than 10 min.
   - An import that started and recorded no outcome after 30 min needs attention.
6. Cap the scanner probe at 10 s and stop the Operations page probing when it opens: yes. Also cap the dashboard's MinIO ping at 5 s.
7. Banner reword: yes, and ship it earlier, in item 3, because the current text has been false since #230. Please confirm that naming purchase orders alone is enough: the logging stand-in also holds the SupplierApproved and SupplierProfileChanged outbox events (ApproveApplicationHandler.cs:50, ComplianceReTrigger.cs:47).
8. Audit the 8 unaudited admin actions in their own PR right after Phase 1: yes. They are field-config update, email-template save and delete, job trigger, outbox replay, document-type-categories set, and UI-string override save and delete. Add to that PR: stop the generic "Run now" from starting supplier-erp-push. Today only erp-supplier-sync is barred (RecurringJobs.cs:32-37), so Run now would run the push as "system" under admin.users.manage and write to the ERP whenever the switch is on.
9. Mock-up before PR 11: yes. It should show the ERP block as it is today: push off, N suppliers waiting.
10. NEW: what does the dashboard show about the ERP push while it is switched off? Recommended:
   - Neutral information, "Push to ERP is off: N approved suppliers waiting". It is never a needs-attention item and never a warning colour.
   - The supplier-erp-push job verdict shows normally. While off, the job succeeds without doing anything (SupplierErpPushJob.cs:176-188).
   - The target host is shown with a yes or no for "on Erp:WriteHosts", never the list itself, so you can see at a glance that production cannot be written to.
   - Push alerts (failed, stalled, host not allowed, no group) fire only while the switch is on.
11. NEW: signing out does not end the session on the server (AuthEndpoints.cs:268-271), so even after item 2 the count includes signed-out sessions for up to 30 days. Recommended: revoke the presented token's family on sign-out, in item 4, with a test. The fallback is to label the figure "unexpired sessions".
12. NEW: two refreshes at once can sign a user out. The frontend sends a refresh for every 401 at the same moment, and RefreshTokenHandler.cs:37-43 revokes the whole family for any revoked or expired token. Recommended: inside the 10 s grace, answer 401 without revoking the family and without an audit row; after it, revoke and audit. An expired token, or one ended by sign-out, reset or deactivation, gets a plain Invalid with no reuse row.
13. NEW: accounts that must use two-factor but have not set it up cannot sign in, and cannot set it up either. LoginHandler.cs:94-98 refuses them before any session exists, and the enrolment routes need a session (MfaEndpoints.cs:27). So "Reset MFA" on a system_admin locks that person out. Recommended: the dashboard shows these accounts as "cannot sign in" with no Reset MFA suggestion, and the enrolment fix is logged as a separate task after Phase 1.
14. NEW: should Test connection leave an audit row? Today it reaches the ERP and records nothing (IntegrationHandlers.cs:135-150). Recommended: yes, an IntegrationConnectionTested row with the actor, in item 5.
15. NEW: what appears in a supplier's own audit trail? Recommended: leave out login_*, refresh_*, session_revoked, sessions_revoked_all and document_scan_requeued. Keep password_reset, password_changed and mfa_enrolled, as today.


RISKS:
- Item 4 changes sign-in and refresh behaviour, which is security-sensitive, so every path needs a test. The ASVS doc already marks these events as passing, and it stays wrong until item 4 lands.
- Sign-in audit rows from before item 4 are lost and cannot be recovered. The security counts start from the deploy date, so a 0 must not be read as "nothing happened".
- Saving failed-login rows makes the audit table grow under password-guessing attacks. I did not check whether any retention exists.
- The dashboard must never call the ERP. That is your rule about the real ERP, and a 60 s auto-refresh would otherwise hit it constantly. It reads only database rows and configuration.
- Each request opens about 6 parallel database connections plus Hangfire monitoring queries, every 60 s for each open tab. Watch the connection pool.
- Environments where migrations are applied by hand may be missing the #230 or #232 migrations. The ERP sub-sections must fail on their own rather than fail the whole response.
- "Never signed in" depends on RefreshTokens rows never being deleted. Any future sign-out or cleanup must revoke rows, not delete them.
- Retiring the overview touches CI guards that are easy to break: the generated PERMISSIONS.md, the e2e fixtures, the a11y route count (73) and the route exemptions.
- Some fast-suite tests read prose from source files, so rewording comments in items 1, 3 and 9 can fail them. Run the fast suites.
- Order: 6 before 13; 4 and 5 before 9; 2 before 8; 7 before 10, 11 and 12; 11 before 12.
