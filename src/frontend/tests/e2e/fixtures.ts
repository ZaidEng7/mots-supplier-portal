import type { Page } from '@playwright/test'

/**
 * Shared e2e fixtures for tests that need an authenticated app rendering real pages against a
 * mocked backend. Extracted from app-a11y.spec.ts (Task #22) so app-keyboard.spec.ts and
 * app-error-association.spec.ts can reuse the exact same auth/data setup rather than drifting
 * copies of it - three specs independently maintaining "what SUPPLIER_PROFILE looks like" is
 * exactly the kind of duplication this codebase has already paid for once (MSP-77's two
 * independently-invented flagged-field vocabularies).
 */

// ---- Auth, faked at the network boundary rather than through a real backend ---------------
//
// app-smoke.spec.ts already established the precedent this follows: "a full authenticated login
// is covered by manual/CI integration tests since it needs a seeded backend user; this smoke
// test only needs the frontend dev server." The Frontend (React) CI job has no Postgres, no
// MinIO, no API - only the Vite dev server. Real login is out of reach here by the same
// constraint that shaped the existing suite, not a new one invented for this ticket.
//
// useAuthStore.setSession decodes JWT claims client-side and never checks the signature - the
// API re-validates every permission server-side, this is display/routing only (authStore.ts's
// own comment says so). So an unsigned, structurally-valid token is sufficient to make the app
// believe it is authenticated, without any real credential existing anywhere.
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
  perms: ['review.read', 'review.decide'],
})

export const REFERENCE_CODE = 'SUP-2026-000001'
export const RFQ_REFERENCE_CODE = 'RFQ-2026-000001'
export const PROPOSAL_REFERENCE_CODE = 'PRP-2026-000001'

// R-9: the supplier-facing wire names. These fixtures are untyped by design (they stand in for the
// wire, not for the TS interfaces), so tsc cannot catch a rename here - the a11y run is what does.
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

/**
 * The documented §5.2 list envelope, `{ data, pagination, meta }`. Every list endpoint returns it,
 * and `useInfiniteQuery` reads `pagination.hasMore` before rendering anything - so a route mocked
 * as a bare array (or the old flat `{ items, hasMore, nextCursor }`) crashes the page under axe
 * rather than merely rendering the wrong rows.
 */
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

/**
 * The list endpoints project a narrow list item - reference, both titles, state, createdAt (plus
 * the caller's own invitation status on the supplier side) - not the whole aggregate. Kept separate
 * from the detail fixtures above so a list page that starts reading a field the list does not send
 * fails the a11y run instead of passing on a fixture that is richer than the wire.
 */
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

/**
 * Intercepts every `/api/v1/**` call with representative, structurally-real data so each
 * authenticated page renders its normal DOM rather than an error boundary.
 *
 * Deliberately more permissive than `src/test/renderPage.tsx`'s `mockFetch`, which throws on an
 * undeclared request. That strictness is right for a component test asserting specific behaviour;
 * it would make this suite unmaintainable across 18 routes' worth of endpoints for a purpose that
 * only needs the DOM to render normally, not to prove any particular data flow. Stated as a
 * deliberate difference in philosophy, not an oversight - an unmatched request here gets a benign
 * empty 200 rather than aborting the scan.
 */
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
    // MSP-84: /suppliers/me/users, /auth/sessions and /review/queue return the list envelope.
    if (p === '/api/v1/suppliers/me/users') return route.fulfill({ json: listPage([{ userId: 'u1', email: 'teammate@example.com', fullName: 'Teammate One', isActive: true }]) })
    if (p === '/api/v1/auth/sessions') return route.fulfill({ json: listPage([{ familyId: 'f1', ip: '127.0.0.1', userAgent: 'axe-scan', createdAt: '2026-08-01T00:00:00Z', expiresAt: '2026-09-01T00:00:00Z', isCurrent: true }]) })
    if (p === '/api/v1/review/queue') return route.fulfill({ json: listPage([{ referenceCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'UnderReview' }]) })
    if (p === `/api/v1/review/${REFERENCE_CODE}`) return route.fulfill({ json: { supplier: SUPPLIER_PROFILE, documents: DOCUMENT_TYPES, annotationHistory: [] } })
    if (p === '/api/v1/auth/verify-email' && method === 'POST') return route.fulfill({ json: {} })
    // Task #7/Stage C: list endpoints return real arrays, not the generic {} fallback below -
    // an empty object crashes OrganizationsPage's .map() the same way any list page would.
    // EPIC-19 reports. Real shapes, not {}: the page maps over four arrays, and an empty object
    // renders the error state - which would make an axe scan or a reflow measurement a scan of an
    // error message rather than of the screen.
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
    // SCR-600 and SCR-700. Without these two the pages fall through to the catch-all `{}`, render
    // their error card, and the a11y scan silently covers a failure state instead of the screen.
    // T-060: the public allow-list, and the admin catalogue behind SCR-724.
    if (p === '/api/v1/admin/notification-templates') return route.fulfill({ json: [
      { type: 'rfq.approved', titleAr: 'تمت الموافقة', titleEn: 'RFQ approved', bodyAr: 'تمت الموافقة على {rfqCode}', bodyEn: 'RFQ {rfqCode} was approved', shippedTitleAr: 'تمت الموافقة', shippedTitleEn: 'RFQ approved', shippedBodyAr: 'تمت الموافقة على {rfqCode}', shippedBodyEn: 'RFQ {rfqCode} was approved', isOverridden: false, updatedAt: null, availableTokens: ['rfqCode'] },
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
    // Closure batch (EPIC-01/06): same class of bug - RolesPage's roles.flatMap() and
    // OfferingCatalogPage's offerings.map() both crash on {} the same way OrganizationsPage did.
    // FR-ADM-002 fix: /admin/roles now returns { roles, allPermissions } - allPermissions is the
    // full Permissions.All catalog, not derived from what roles currently hold (see
    // RolesResponse's doc comment for why).
    if (p === '/api/v1/admin/roles') return route.fulfill({ json: { roles: [{ name: 'system_admin', permissions: ['admin.roles.manage'] }], allPermissions: ['admin.roles.manage'] } })
    if (p === '/api/v1/suppliers/me/offerings') return route.fulfill({ json: [] })
    if (p === '/api/v1/reference/units-of-measure') return route.fulfill({ json: [{ code: 'unit', nameAr: 'وحدة', nameEn: 'Unit' }] })
    // FEAT-06.3: buyer-facing offering search - same class of bug, an unmocked list endpoint
    // crashing OfferingSearchPage's results.map() on the generic {} fallback below.
    if (p === '/api/v1/offerings/search') return route.fulfill({ json: [] })
    // FEAT-11.1/EPIC-07: same class of bug - RfqListPage's rfqs.map() and
    // EvaluationTemplatesPage's templates.map() both crash on the generic {} fallback below.
    if (p === '/api/v1/evaluation-templates') return route.fulfill({ json: [EVALUATION_TEMPLATE_FIXTURE] })
    // Buyer and supplier share these two paths, and the real API returns a DIFFERENT shape on each
    // depending on the caller's persona - which is exactly what RfqPersonaShapeTests asserts. A
    // path-only mock cannot tell the two apart, so it serves the union of both shapes.
    //
    // This was invisible until R-9: both personas spelled the code `referenceCode`, so whichever
    // fixture won covered both pages by accident. R-9 conformed the SUPPLIER shapes to §12.4
    // (rfqCode, invitationStatus, submissionDeadline) and deliberately left the unspecified buyer
    // shapes alone, and the a11y run is what found the collision - the fixtures are untyped by
    // design, so tsc could not.
    if (p === '/api/v1/rfqs') {
      return route.fulfill({ json: listPage([{ ...RFQ_LIST_ITEM_FIXTURE, ...SUPPLIER_RFQ_LIST_ITEM_FIXTURE }]) })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}`) {
      return route.fulfill({ json: { ...RFQ_FIXTURE, ...SUPPLIER_RFQ_FIXTURE } })
    }
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/invitations/candidates`) return route.fulfill({ json: [] })
    // EPIC-11: same class of bug - MyEvaluationPage reads evaluation.proposalIds.map() and would
    // crash on the generic {} fallback below; null (200) is the real "not assigned" shape.
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/my-evaluation`) return route.fulfill({ json: null })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/evaluation`) return route.fulfill({ json: null })
    // EPIC-12: same class of bug - ComparisonPage reads comparison.proposals.length and would
    // crash on the generic {} fallback below; an empty-but-real shape is the honest fixture.
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/comparison`) {
      return route.fulfill({ json: { rfqReferenceCode: RFQ_REFERENCE_CODE, rfqTitleAr: 'طلب تجريبي', rfqTitleEn: 'A11y Test RFQ', evaluationState: 'NotStarted', rfqItems: [], proposals: [] } })
    }
    // EPIC-14: same class of bug - AwardPage reads evaluation.results and would crash on the
    // generic {} fallback below; null (200) for /award is the real "no award yet" shape.
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/award`) return route.fulfill({ json: null })
    // EPIC-13: same class of bug - the guided workspace panel embedded on RfqDetailPage reads
    // workspace.stages.map()/workspace.nextActions.map() and would crash on the generic {}
    // fallback below.
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
    // EPIC-08's supplier-facing list/detail used to be mocked here, on the same two paths the buyer
    // block above already matches - so these two lines never ran. Folded into that block as the
    // union rather than left as dead branches that look like coverage.
    // EPIC-09: same class of bug - SupplierProposalPage would fall through to the generic {}
    // fallback below and crash reading proposal.items.
    // §12-A/C2: discovery hangs off the RFQ, the resource itself is code-addressed. Both are
    // mocked because the page uses the first to learn the code it needs for the second.
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/proposals`) return route.fulfill({ json: PROPOSAL_FIXTURE })
    if (p === `/api/v1/proposals/${PROPOSAL_REFERENCE_CODE}`) return route.fulfill({ json: PROPOSAL_FIXTURE })

    // §12-A/Part D: an unmatched GET now FAILS LOUDLY instead of returning `{}`.
    //
    // The generic fallback existed so mutation endpoints no render triggers could not crash a
    // scan, and that part is kept. But for a GET it was actively harmful: a route renamed on the
    // backend without updating this file kept "passing" here, because `{}` is a valid JSON body
    // and a page rendering an empty state looks like a page rendering. That is precisely the silent
    // 404 this batch's discipline exists to catch, and §11's OpenAPI/oasdiff gate - which would
    // otherwise catch it - is documented but unbuilt.
    if (method === 'GET') {
      return route.fulfill({
        status: 500,
        json: { error: 'unmocked_get', detail: `e2e fixtures declare no GET route for ${p}` },
      })
    }

    // Non-GET: benign empty success, unchanged.
    return route.fulfill({ status: 200, json: {} })
  })
}
