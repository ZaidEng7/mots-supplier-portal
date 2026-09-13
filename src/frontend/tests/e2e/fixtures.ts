// Shared e2e fixtures for tests that need an authenticated app rendering real pages against a mocked
// backend. Extracted from app-a11y.spec.ts (Task #22) so app-keyboard.spec.ts and
// app-error-association.spec.ts reuse the exact same auth and data setup rather than drifting copies of
// it - three specs independently maintaining "what SUPPLIER_PROFILE looks like" is exactly the kind of
// duplication this codebase has already paid for once, in MSP-77's two independently-invented
// flagged-field vocabularies.
//
// AUTH, faked at the network boundary rather than through a real backend. app-smoke.spec.ts set the
// precedent this follows: a full authenticated login is covered by the integration suite because it
// needs a seeded backend user, and the Frontend (React) CI job has no Postgres, no MinIO and no API -
// only the Vite dev server. Real login is out of reach here by the same constraint that shaped the
// existing suite, not a new one invented for this ticket. useAuthStore.setSession decodes JWT claims
// client-side and never checks the signature - the API re-validates every permission server-side, so
// this is display and routing only, as authStore.ts says of itself. An unsigned, structurally valid
// token is therefore enough to make the app believe it is authenticated, with no real credential
// existing anywhere.
//
// reviewerToken is the staff account every back-office scan runs as. It used to hold `review.read` and
// `review.decide` only, and no navigation row in either shell has ever been gated on those two - so
// every accessibility scan of the back office rendered a rail with two rows in it, and axe never once
// saw the navigation it was supposed to be checking. The list is now exactly the set the sidebar's rows
// are gated on, plus an organization, because BRULE-029 hides the procurement rows from an account that
// belongs to no buying body. It is a scanning fixture and not a claim about any real role: no person
// holds all of these. What it buys is that the scan sees every row the product can draw, which is the
// thing being scanned.
//
// The data fixtures use the supplier-facing wire names of R-9. They are untyped by design - they stand
// in for the wire, not for the TS interfaces - so tsc cannot catch a rename in them, and the a11y run
// is what does.
//
// SUPPLIER_PROFILE carries a documents array because SCR-120's profile screen reads its length. It was
// absent, /profile threw during render, and the a11y scan hit the router's 500 boundary instead of the
// page - caught by the suite's own "no 404/500 on screen" guard. Fixed here rather than by making the
// page defensive: the real API always returns the array, checked against a running server, and a `?? []`
// in the page would hide a genuinely malformed response, which is the opposite of what this fixture's
// fail-loud philosophy is for.
//
// listPage wraps items in the documented §5.2 envelope, `{ data, pagination, meta }`. Every list
// endpoint returns it, and useInfiniteQuery reads `pagination.hasMore` before rendering anything, so a
// route mocked as a bare array - or as the old flat `{ items, hasMore, nextCursor }` - crashes the page
// under axe rather than merely rendering the wrong rows.
//
// The *_LIST_ITEM_FIXTUREs are deliberately separate from the detail fixtures above them. The list
// endpoints project a narrow item - reference, both titles, state, createdAt, plus the caller's own
// invitation status on the supplier side - not the whole aggregate. Keeping them apart means a list page
// that starts reading a field the list does not send fails the a11y run instead of passing against a
// fixture richer than the wire.
//
// mockBackend intercepts every `/api/v1/**` call with representative, structurally real data so each
// authenticated page renders its normal DOM rather than an error boundary. It is deliberately more
// permissive than src/test/renderPage.tsx's mockFetch, which throws on an undeclared request. That
// strictness is right for a component test asserting specific behaviour; it would make this suite
// unmaintainable across every route's worth of endpoints for a purpose that only needs the DOM to render
// normally, not to prove any particular data flow. A difference in philosophy, stated as one rather
// than left as an oversight.
//
// One pattern accounts for most of the routes declared in it, and it is the same bug each time: a page
// maps over a list, so an unmocked endpoint answered with a generic `{}` crashes the render or drops the
// page onto its error card - and an axe scan or a reflow measurement then covers an error message rather
// than the screen. Every list endpoint here therefore answers with its REAL shape. MSP-84 for
// /suppliers/me/users, /auth/sessions and /review/queue; Task #7/Stage C for the organizations screen,
// whose .map() crashed first and named the class; EPIC-19's reports, which map over four arrays; batch
// 11's admin screens; the closure batch's RolesPage.roles.flatMap() and OfferingCatalogPage; FEAT-06.3's
// buyer-facing offering search; FEAT-11.1/EPIC-07's RFQ list and evaluation templates; EPIC-11's
// MyEvaluationPage (null with a 200 is the real "not assigned" shape); EPIC-12's ComparisonPage (an
// empty-but-real shape is the honest fixture); EPIC-14's AwardPage (null with a 200 is the real "no
// award yet"); EPIC-13's guided workspace panel; EPIC-09's SupplierProposalPage; SCR-600 and SCR-700's
// two dashboards; and Phase 3's five screens, which the loud fallback below is what surfaced.
//
// Several routes carry a reason of their own:
//
//   /admin/roles returns { roles, allPermissions } per the FR-ADM-002 fix, where allPermissions is the
//   full Permissions.All catalogue rather than being derived from what roles currently hold - see
//   RolesResponse's own documentation for why.
//
//   The RFQ detail and list paths are shared by buyer and supplier, and the real API returns a DIFFERENT
//   shape on each depending on the caller's persona, which is what RfqPersonaShapeTests asserts. A
//   path-only mock cannot tell the two apart, so it serves the union of both shapes. This was invisible
//   until R-9: both personas spelled the code `referenceCode`, so whichever fixture won covered both
//   pages by accident. R-9 conformed the supplier shapes to §12.4 (rfqCode, invitationStatus,
//   submissionDeadline) and deliberately left the unspecified buyer shapes alone, and the a11y run is
//   what found the collision - the fixtures being untyped, tsc could not.
//
//   EPIC-08's supplier-facing list and detail used to be mocked on two lines of their own, against the
//   same two paths the buyer block already matched, so those lines never ran. They are folded into that
//   block as the union rather than left as dead branches that look like coverage.
//
//   §12-A/C2's discovery hangs off the RFQ while the resource itself is code-addressed, so both are
//   mocked: the page uses the first to learn the code it needs for the second.
//
//   T-060 declares the public allow-list and the admin catalogue behind SCR-724. T-079/SCR-720's audit
//   explorer must not swallow /audit/export - it only reads the search on load. T-080/SCR-710-712's
//   reference-data editor asks per table, so the fixture answers any of the five. A-7's two assignment
//   pickers on the RFQ detail page are asked for on every buyer view.
//
//   Phase 4 / D-66 WITHHOLDS the commercial values. The flag is off outside the demonstration seeder, so
//   the shape the scan renders is the one a fresh environment serves - and the withheld branch is the one
//   most worth having under axe, because it is the branch that says why a number is missing rather than
//   showing a zero.
//
// The account endpoint's language FOLLOWS the page's ?lng=, and that branch is the whole reason it is
// written out. With a hardcoded 'en' there, router.tsx's applyStoredLanguage() fetched this account on
// load and called changeLanguage('en'), overwriting whatever ?lng=ar had set. Every authenticated route
// in the a11y suite therefore ran BOTH of its two scans against the English LTR interface - roughly 60
// of 68 routes - while the suite reported "137 scans in both languages". The Arabic UI of an Arabic-first
// product had never been scanned behind sign-in. Proved by reading document.documentElement.dir under
// this harness: 'ltr' on every authenticated route with ?lng=ar, and 'rtl' on /login, which is
// unauthenticated and so never reaches the branch.
//
// Finally, §12-A/Part D: an unmatched GET now FAILS LOUDLY instead of returning `{}`. The generic
// fallback existed so mutation endpoints with no render trigger could not crash a scan, and for
// non-GET requests it is unchanged - a benign empty success. For a GET it was actively harmful: a route
// renamed on the backend without updating this file kept "passing", because `{}` is a valid JSON body
// and a page rendering an empty state looks like a page rendering. That is precisely the silent 404 this
// batch's discipline exists to catch, and §11's OpenAPI/oasdiff gate, which would otherwise catch it, is
// documented but unbuilt.

import type { Page } from '@playwright/test'


function fakeJwt(claims: Record<string, unknown>): string {
  const b64url = (obj: unknown) =>
    Buffer.from(JSON.stringify(obj)).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url(claims)}.fake-signature-not-verified-client-side`
}

export const SUPPLIER_ID = '01a00000-0000-7000-8000-000000000001'
export const supplierToken = fakeJwt({
  sub: '01a00000-0000-7000-8000-0000000000a1',
  email: 'a11y-supplier@example.com',
  supplierId: SUPPLIER_ID,
  perms: ['supplier.profile.edit', 'supplier.documents.upload', 'supplier.users.manage'],
})
export const reviewerToken = fakeJwt({
  sub: '01a00000-0000-7000-8000-0000000000a2',
  email: 'a11y-reviewer@example.com',
  organizationId: '01a00000-0000-7000-8000-0000000000b1',
  perms: [
    'review.read', 'review.decide',
    'report.read', 'rfq.read', 'evaluation.template.manage', 'evaluation.score', 'supplier.review',
    'supplier.directory.read', 'offering.search', 'governance.read', 'admin.organizations.manage',
    'admin.users.manage', 'admin.roles.manage', 'reference.manage', 'audit.read',
  ],
})

export const REFERENCE_CODE = 'SUP-2026-000001'
export const RFQ_REFERENCE_CODE = 'RFQ-2026-000001'
export const PROPOSAL_REFERENCE_CODE = 'PRP-2026-000001'

export const SUPPLIER_PROFILE = {
  supplierCode: REFERENCE_CODE,
  displayNameAr: 'شركة الاختبار للتوريدات',
  displayNameEn: 'A11y Test Supplies Co',
  description: 'A representative supplier profile used only to render pages for accessibility scanning.',
  website: 'https://example.com',
  logoStorageKey: null,
  supplierGroup: null,
  onboardingState: 'UnderReview',
  lifecycleState: 'Active',
  defaultCurrency: 'SYP',
  legalInfo: {
    legalNameAr: 'شركة الاختبار', legalNameEn: 'A11y Test Co', registrationNumber: 'RC-1234',
    taxId: 'TX-5678', supplierType: 'Company', establishedOn: '2020-01-01',
  },
  categories: ['general'],
  primaryContactPhone: '+963900000000',
  representatives: [{ id: 'r1', fullName: 'Rana Tester', email: 'rana@example.com', phone: '+963900000001', position: 'Manager', isPrimary: true }],
  addresses: [{ id: 'a1', kind: 'HeadOffice', line1: '1 Test Street', line2: null, city: 'Damascus', regionCode: 'DM', country: 'SY', postalCode: null, latitude: null, longitude: null }],
  contacts: [{ id: 'c1', fullName: 'Contact Person', email: 'contact@example.com', phone: '+963900000002', role: 'Sales' }],
  branches: [],
  bankAccounts: [{ id: 'b1', accountHolderName: 'A11y Test Co', bankName: 'Test Bank', branchName: null, maskedAccountNumber: '****1234', swiftBic: null, currencyCode: 'SYP', isDefault: true }],
  categoryCodes: ['general'],
  missingProfileFields: [],
  termsAcceptedVersion: '1.0',
  termsAcceptedAt: '2026-08-01T00:00:00Z',
  rowVersion: 1,
}

export const DOCUMENT_TYPES = [
  { documentTypeId: 'd1', code: 'commercial_registration', nameAr: 'السجل التجاري', nameEn: 'Commercial Registration', isRequired: true, expiryTracked: false, latestDocument: null },
  { documentTypeId: 'd2', code: 'tax_certificate', nameAr: 'الشهادة الضريبية', nameEn: 'Tax Certificate', isRequired: true, expiryTracked: true, latestDocument: { id: 'doc1', version: 1, state: 'Approved', originalFileName: 'tax-cert.pdf', contentType: 'application/pdf', sizeBytes: 102400, issueDate: '2026-01-01', expiryDate: '2027-01-01', rejectReason: null, uploadedAt: '2026-01-01T00:00:00Z', reviewedAt: '2026-01-02T00:00:00Z' } },
  { documentTypeId: 'd3', code: 'chamber_membership', nameAr: 'عضوية الغرفة التجارية', nameEn: 'Chamber Membership', isRequired: false, expiryTracked: true, latestDocument: null },
]

function listPage<T>(items: T[]) {
  return {
    data: items,
    pagination: { mode: 'cursor', nextCursor: null, prevCursor: null, pageSize: 20, totalCount: null, hasMore: false },
    meta: { sort: null, filtersApplied: null },
  }
}

export const RFQ_FIXTURE = {
  referenceCode: RFQ_REFERENCE_CODE, organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state: 'Draft',
  publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
  evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
  items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
}

export const SUPPLIER_RFQ_FIXTURE = {
  rfqCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state: 'Published',
  submissionOpensAt: null, submissionDeadline: null, clarificationDeadlineAt: null,
  items: [], requirements: [], attachments: [], invitationStatus: 'Invited', clarifications: [], addenda: [],
}

export const RFQ_LIST_ITEM_FIXTURE = {
  referenceCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  state: 'Draft', createdAt: '2026-08-01T00:00:00Z',
}

export const SUPPLIER_RFQ_LIST_ITEM_FIXTURE = {
  rfqCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  state: 'Published', invitationStatus: 'Invited', createdAt: '2026-08-01T00:00:00Z',
  submissionDeadline: null,
}

export const PROPOSAL_FIXTURE = {
  proposalCode: 'PRP-2026-000001', rfqCode: RFQ_REFERENCE_CODE, state: 'Draft',
  createdAt: '2026-08-01T00:00:00Z', totals: { currency: null, grandTotal: 0 }, validityDays: null,
  currency: null, paymentTerms: null, incotermCode: null, deliveryTermsAr: null, deliveryTermsEn: null,
  warranty: null, validityStart: null, validityEnd: null, narrativeAr: null, narrativeEn: null,
  submittedAt: null, withdrawnAt: null, withdrawReason: null,
  items: [], documents: [], requirementAnswers: [],
}

export const EVALUATION_TEMPLATE_FIXTURE = {
  id: 'tpl-1', familyId: 'fam-1', version: 1, nameAr: 'قالب', nameEn: 'A11y Test Template',
  status: 'Draft', isReferenced: false, criteria: [],
}

export async function mockBackend(page: Page) {
  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const p = url.pathname
    const method = route.request().method()

    if (p === '/api/v1/auth/refresh' && method === 'POST') {
      return route.fulfill({ json: { accessToken: page.url().includes('/back-office') ? reviewerToken : supplierToken, accessTokenExpiresAt: new Date(Date.now() + 3600_000).toISOString() } })
    }
    if (p === '/api/v1/suppliers/me') return route.fulfill({ json: SUPPLIER_PROFILE })
    if (p === '/api/v1/suppliers/SUP-2026-000001/documents') return route.fulfill({ json: DOCUMENT_TYPES })
    if (p === '/api/v1/suppliers/me/active-annotation') return route.fulfill({ json: null })
    if (p === '/api/v1/reference/currencies') return route.fulfill({ json: [{ code: 'SYP', nameAr: 'ليرة سورية', nameEn: 'Syrian Pound' }] })
    if (p === '/api/v1/reference/regions') return route.fulfill({ json: [{ code: 'DM', nameAr: 'دمشق', nameEn: 'Damascus' }] })
    if (p === '/api/v1/reference/categories') return route.fulfill({ json: [{ code: 'general', nameAr: 'عام', nameEn: 'General' }] })
    if (p === '/api/v1/suppliers/me/users') return route.fulfill({ json: listPage([{ userId: 'u1', email: 'teammate@example.com', fullName: 'Teammate One', isActive: true }]) })
    if (p === '/api/v1/auth/sessions') return route.fulfill({ json: listPage([{ familyId: 'f1', ip: '127.0.0.1', userAgent: 'axe-scan', createdAt: '2026-08-01T00:00:00Z', expiresAt: '2026-09-01T00:00:00Z', isCurrent: true }]) })
    if (p === '/api/v1/review/queue') return route.fulfill({ json: listPage([{ referenceCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'UnderReview' }]) })
    if (p === `/api/v1/review/${REFERENCE_CODE}`) return route.fulfill({ json: { supplier: SUPPLIER_PROFILE, documents: DOCUMENT_TYPES, annotationHistory: [] } })
    if (p === '/api/v1/auth/verify-email' && method === 'POST') return route.fulfill({ json: {} })
    if (p === '/api/v1/reports/procurement') return route.fulfill({ json: {
      rfqsByState: [{ key: 'Draft', count: 6 }, { key: 'Published', count: 12 }],
      cycleTimes: [
        { key: 'ReviewToApproved', sampleSize: 24, medianHours: 18.5 },
        { key: 'EvaluationToAward', sampleSize: 0, medianHours: null },
      ],
      awardsByState: [{ key: 'Recommended', count: 3 }],
      totalRfqs: 18,
      coverageFloor: '2026-06-05T09:00:00Z',
    } })
    if (p === '/api/v1/reports/compliance') return route.fulfill({ json: {
      suppliersByLifecycleState: [{ key: 'Active', count: 41 }],
      documentsByState: [{ key: 'ExpiringSoon', count: 7 }, { key: 'Approved', count: 88 }],
      totalSuppliers: 41,
      documentsExpiringSoon: 7,
      documentsExpired: 2,
    } })
    if (p === '/api/v1/organizations') return route.fulfill({ json: [] })
    if (p === '/api/v1/admin/ui-strings/') return route.fulfill({ json: [
      { key: 'nav.dashboard', language: 'ar', value: 'الرئيسية', updatedAt: '2026-09-01T00:00:00Z' },
    ] })
    if (p === '/api/v1/admin/email-templates/') return route.fulfill({ json: [
      {
        key: 'email.verification', requiredTokens: ['verifyUrl'], optionalTokens: [],
        override: null,
        shipped: {
          key: 'email.verification', subjectAr: 'تفعيل حسابك', subjectEn: 'Verify your account',
          bodyAr: '<p>{verifyUrl}</p>', bodyEn: '<p>{verifyUrl}</p>', updatedAt: '1970-01-01T00:00:00Z',
        },
      },
    ] })
    if (p === '/api/v1/admin/jobs') return route.fulfill({ json: {
      recurringEnabled: true,
      jobs: [{ id: 'outbox-dispatch', registered: true, cron: '*/5 * * * *', lastExecution: '2026-09-01T00:00:00Z', nextExecution: '2026-09-01T00:05:00Z', lastState: 'Succeeded' }],
    } })
    if (p === '/api/v1/admin/outbox') return route.fulfill({ json: {
      counts: { Pending: 0, Sent: 3, Failed: 0 },
      messages: [{ id: '00000000-0000-0000-0000-000000000001', type: 'notification', syncStatus: 'Sent', createdAt: '2026-09-01T00:00:00Z', processedAt: '2026-09-01T00:01:00Z', payloadJson: '{}' }],
    } })
    if (p === '/api/v1/admin/erp-sync') return route.fulfill({ json: {
      transportConfigured: false,
      counts: { NotRequested: 1, Requested: 0, Synced: 0, Failed: 0 },
      awards: [{ rfqReferenceCode: REFERENCE_CODE, erpSyncStatus: 'NotRequested', erpRetryCount: 0, erpSyncedAt: null, externalPurchaseOrderRef: null }],
    } })
    if (p === '/api/v1/admin/storage') return route.fulfill({ json: {
      maxUploadBytes: 20971520,
      allowedTypes: { '.pdf': 'application/pdf' },
      bucket: 'documents',
      objectStorageReachable: true, virusScannerReachable: true,
      documentCount: 3, pendingScanCount: 0,
    } })
    if (p === '/api/v1/admin/security') return route.fulfill({ json: {
      password: { minimumLength: 12, requireDigit: false, requireUppercase: false, requireLowercase: false, requireNonAlphanumeric: false },
      lockout: { maxFailedAttempts: 5, lockoutMinutes: 15 },
      session: { accessTokenMinutes: 15, refreshTokenDays: 30, clockSkewSeconds: 30 },
      mfaRequiredRoles: ['system_admin'],
      rateLimits: [{ policy: 'auth-strict', permitLimit: 10, windowSeconds: 60 }],
      registrationMode: 'open',
    } })
    if (p === '/api/v1/admin/document-type-categories') return route.fulfill({ json: [
      { documentTypeCode: 'commercial_registration', categoryCodes: ['general'] },
    ] })
    if (p === '/api/v1/search') return route.fulfill({ json: { query: 'demo', hits: [], truncated: false } })
    if (p === '/api/v1/meta') return route.fulfill({ json: { version: '1.0.0', commit: null, maintenance: null } })
    if (p === '/api/v1/auth/me') {
      const requested = new URL(page.url()).searchParams.get('lng')
      const language = requested === 'ar' ? 'ar' : 'en'
      return route.fulfill({ json: {
        fullName: 'A11y Scan User', email: 'scan@example.com', language, languageChosen: true,
      } })
    }
    if (p === '/api/v1/proposals') return route.fulfill({ json: [] })
    if (p === '/api/v1/admin/notification-templates') return route.fulfill({ json: [
      { type: 'rfq.approved', titleAr: 'تمت الموافقة', titleEn: 'RFQ approved', bodyAr: 'تمت الموافقة على {rfqCode}', bodyEn: 'RFQ {rfqCode} was approved', shippedTitleAr: 'تمت الموافقة', shippedTitleEn: 'RFQ approved', shippedBodyAr: 'تمت الموافقة على {rfqCode}', shippedBodyEn: 'RFQ {rfqCode} was approved', isOverridden: false, updatedAt: null, availableTokens: ['rfqCode'] },
    ] })
    if (p === '/api/v1/audit') return route.fulfill({ json: {
      data: [{ id: 'a-1', occurredAt: '2026-09-01T10:00:00Z', aggregateType: 'Rfq',
        aggregateId: '01a00000-0000-7000-8000-000000000001', action: 'rfq_reassigned',
        fromState: null, toState: null, actorLabel: 'A Manager' }],
      pagination: { hasMore: false, nextCursor: null, totalCount: null },
      meta: { filtersApplied: null },
    } })
    if (p.endsWith('/assignees')) return route.fulfill({ json: {
      owners: [{ userId: '01a00000-0000-7000-8000-0000000000b1', fullName: 'An Officer' }],
      approvers: [{ userId: '01a00000-0000-7000-8000-0000000000b2', fullName: 'A Manager' }],
    } })
    if (p.startsWith('/api/v1/admin/reference/')) return route.fulfill({ json: [
      { code: 'IT', nameAr: 'تقنية المعلومات', nameEn: 'Information technology', isActive: true, isRequired: null, expiryTracked: null },
      { code: 'FAX', nameAr: 'فاكس', nameEn: 'Fax machines', isActive: false, isRequired: null, expiryTracked: null },
    ] })
    if (p === '/api/v1/suppliers/me/audit') return route.fulfill({ json: {
      data: [{ id: 'a-1', occurredAt: '2026-09-01T10:00:00Z', aggregateType: 'Supplier', aggregateId: 's-1',
        action: 'supplier_submitted', fromState: null, toState: 'Submitted', actorLabel: null }],
      pagination: { hasMore: false, nextCursor: null },
    } })
    if (p === '/api/v1/staff') return route.fulfill({ json: listPage([
      { userId: 'u-1', email: 'reviewer@ministry.example', fullName: 'A Reviewer', role: 'onboarding_reviewer', isActive: true, mfaEnabled: false, lockoutEnd: null, activeSessionCount: 0 },
    ]) })
    if (p === '/api/v1/reference/settings') return route.fulfill({ json: {
      'registration.mode': 'open',
      'proposals.defaultCurrencyCode': 'SYP',
    } })
    if (p === '/api/v1/admin/settings') return route.fulfill({ json: [
      { key: 'registration.mode', kind: 'Choice', value: 'open', defaultValue: 'open', isOverridden: false, updatedAt: null, allowedValues: ['open', 'closed'], minimum: null, maximum: null },
      { key: 'proposals.defaultCurrencyCode', kind: 'ReferenceCode', value: 'SYP', defaultValue: 'SYP', isOverridden: false, updatedAt: null, allowedValues: null, minimum: null, maximum: null },
      { key: 'documents.expiringSoonWindowDays', kind: 'Integer', value: '30', defaultValue: '30', isOverridden: false, updatedAt: null, allowedValues: null, minimum: 1, maximum: 365 },
      { key: 'documents.renewalReminderDays', kind: 'IntegerList', value: '30,14,3', defaultValue: '30,14,3', isOverridden: false, updatedAt: null, allowedValues: null, minimum: 1, maximum: 365 },
    ] })
    if (p === '/api/v1/ministry/overview') return route.fulfill({ json: {
      totalSuppliers: 12,
      suppliersByLifecycleState: [{ key: 'Active', count: 9 }],
      totalRfqs: 7,
      rfqsByState: [{ key: 'SubmissionOpen', count: 4 }],
      totalAwards: 3,
      averageProposalsPerRfq: 2.5,
      totalAwardedValue: null,
      commercialValuesVisible: false,
    } })
    if (p === '/api/v1/admin/overview') return route.fulfill({ json: {
      usersByRole: [{ role: 'system_admin', count: 1 }],
      totalRoles: 8,
      referenceData: [{ table: 'categories', active: 12, inactive: 2 }],
      outbox: { pending: 2, failed: 1, oldestPendingAgeMinutes: 14 },
      jobs: { recurringJobsEnabled: true, expectedJobs: ['rfq-auto-close'], registeredJobs: ['rfq-auto-close'], missingJobs: [] },
      auditRowsLast24Hours: 143,
    } })
    if (p === '/api/v1/admin/roles') return route.fulfill({ json: { roles: [{ name: 'system_admin', permissions: ['admin.roles.manage'] }], allPermissions: ['admin.roles.manage'] } })
    if (p === '/api/v1/suppliers/me/offerings') return route.fulfill({ json: [] })
    if (p === '/api/v1/reference/units-of-measure') return route.fulfill({ json: [{ code: 'unit', nameAr: 'وحدة', nameEn: 'Unit' }] })
    if (p === '/api/v1/offerings/search') return route.fulfill({ json: [] })
    if (p === '/api/v1/evaluation-templates') return route.fulfill({ json: [EVALUATION_TEMPLATE_FIXTURE] })
    if (p === '/api/v1/rfqs') {
      return route.fulfill({ json: listPage([{ ...RFQ_LIST_ITEM_FIXTURE, ...SUPPLIER_RFQ_LIST_ITEM_FIXTURE }]) })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}`) {
      return route.fulfill({ json: { ...RFQ_FIXTURE, ...SUPPLIER_RFQ_FIXTURE } })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/invitations/candidates`) return route.fulfill({ json: [] })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/my-evaluation`) return route.fulfill({ json: null })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/evaluation`) return route.fulfill({ json: null })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/comparison`) {
      return route.fulfill({ json: { rfqReferenceCode: RFQ_REFERENCE_CODE, rfqTitleAr: 'طلب تجريبي', rfqTitleEn: 'A11y Test RFQ', evaluationState: 'NotStarted', rfqItems: [], proposals: [] } })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/award`) return route.fulfill({ json: null })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/workspace`) {
      return route.fulfill({
        json: {
          rfqReferenceCode: RFQ_REFERENCE_CODE, rfqState: 'Draft', isCancelled: false, submittedProposalCount: 0,
          evaluationState: null, awardState: null,
          stages: [{ key: 'Draft', isCurrent: true, isCompleted: false }],
          nextActions: [],
        },
      })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/proposals`) return route.fulfill({ json: PROPOSAL_FIXTURE })
    if (p === `/api/v1/proposals/${PROPOSAL_REFERENCE_CODE}`) return route.fulfill({ json: PROPOSAL_FIXTURE })

    if (p === '/api/v1/supplier-directory') return route.fulfill({ json: listPage([
      { supplierCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, lifecycleState: 'Active', categoryCodes: ['general'], offeringCount: 2, city: 'Damascus', regionCode: 'DM' },
    ]) })
    if (p === '/api/v1/review/suppliers') return route.fulfill({ json: listPage([
      { supplierCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'Approved', lifecycleState: 'Active', createdAt: '2026-01-01T00:00:00Z', expiredDocumentCount: 0, expiringDocumentCount: 1, rejectedDocumentCount: 0 },
    ]) })
    if (p === '/api/v1/ministry/categories') return route.fulfill({ json: {
      categories: [
        { categoryCode: 'general', nameAr: 'عام', nameEn: 'General', approvedSuppliers: 4, activeSuppliers: 3, activeOfferings: 6, tenders: 2, awardedTenders: 1 },
      ],
      categoriesWithNoActiveSupplier: 0,
      categoriesAreFlat: true,
    } })
    if (p === '/api/v1/notifications/preferences') return route.fulfill({ json: {
      types: [
        { type: 'supplier.approved', muteable: false, muted: false, titleAr: 'تم اعتماد التسجيل', titleEn: 'Registration approved' },
        { type: 'rfq.published', muteable: true, muted: false, titleAr: 'طلب عرض جديد', titleEn: 'A new tender' },
      ],
    } })

    if (p === '/api/v1/ministry/rfqs') return route.fulfill({ json: listPage([
      { referenceCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ', state: 'SubmissionOpen', organizationNameAr: 'وزارة النقل', organizationNameEn: 'Ministry of Transport', publishedAt: '2026-09-01T09:00:00Z', submissionClosesAt: '2026-09-20T09:00:00Z', invitedSuppliers: 3, submittedProposals: 2, awardedValue: null, currencyCode: 'SYP' },
    ]) })
    if (p === `/api/v1/ministry/rfqs/${RFQ_REFERENCE_CODE}`) return route.fulfill({ json: {
      summary: { referenceCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ', state: 'SubmissionOpen', organizationNameAr: 'وزارة النقل', organizationNameEn: 'Ministry of Transport', publishedAt: '2026-09-01T09:00:00Z', submissionClosesAt: '2026-09-20T09:00:00Z', invitedSuppliers: 3, submittedProposals: 1, awardedValue: null, currencyCode: 'SYP' },
      descriptionAr: 'وصف', descriptionEn: 'A11y description',
      items: [{ titleAr: 'وجبات', titleEn: 'Meals', categoryCode: 'general', quantity: 500, unitOfMeasureCode: 'unit' }],
      bids: [{ proposalCode: PROPOSAL_REFERENCE_CODE, supplierCode: REFERENCE_CODE, supplierDisplayNameAr: SUPPLIER_PROFILE.displayNameAr, supplierDisplayNameEn: SUPPLIER_PROFILE.displayNameEn, state: 'UnderReview', submittedAt: '2026-09-05T10:00:00Z', totalValue: null, isAwarded: false }],
      commercialValuesVisible: false,
    } })
    if (p === '/api/v1/ministry/suppliers') return route.fulfill({ json: listPage([
      { supplierCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'Approved', lifecycleState: 'Active', categoryCodes: ['general'], submittedProposals: 2, awardsWon: 1, awardedValue: null, registeredAt: '2026-01-01T00:00:00Z' },
    ]) })
    if (p === '/api/v1/ministry/awards') return route.fulfill({ json: {
      totalAwards: 3,
      totalAwardedValue: null,
      byMonth: [{ key: '2026-08', awards: 2, value: null }],
      byCategory: [{ key: 'general', awards: 3, value: null }],
      byOrganization: [{ key: 'Ministry of Transport', awards: 3, value: null }],
      commercialValuesVisible: false,
    } })

    if (method === 'GET') {
      return route.fulfill({
        status: 500,
        json: { error: 'unmocked_get', detail: `e2e fixtures declare no GET route for ${p}` },
      })
    }

    return route.fulfill({ status: 200, json: {} })
  })
}
