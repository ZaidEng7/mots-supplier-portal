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

export const SUPPLIER_PROFILE = {
  referenceCode: REFERENCE_CODE,
  displayNameAr: 'شركة الاختبار للتوريدات',
  displayNameEn: 'A11y Test Supplies Co',
  description: 'A representative supplier profile used only to render pages for accessibility scanning.',
  website: 'https://example.com',
  logoStorageKey: null,
  supplierGroup: null,
  onboardingState: 'UnderReview',
  lifecycleState: 'Active',
  currencyCode: 'SYP',
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

export const RFQ_FIXTURE = {
  referenceCode: RFQ_REFERENCE_CODE, organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state: 'Draft',
  publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
  evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
  items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
}

export const SUPPLIER_RFQ_FIXTURE = {
  referenceCode: RFQ_REFERENCE_CODE, titleAr: 'طلب تجريبي', titleEn: 'A11y Test RFQ',
  descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state: 'Published',
  submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
  items: [], requirements: [], attachments: [], myInvitationStatus: 'Invited', clarifications: [], addenda: [],
}

export const PROPOSAL_FIXTURE = {
  referenceCode: 'PRP-2026-000001', rfqReferenceCode: RFQ_REFERENCE_CODE, state: 'Draft',
  currencyCode: null, paymentTerms: null, incotermCode: null, deliveryTermsAr: null, deliveryTermsEn: null,
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
    if (p === '/api/v1/suppliers/me/documents') return route.fulfill({ json: DOCUMENT_TYPES })
    if (p === '/api/v1/suppliers/me/active-annotation') return route.fulfill({ json: null })
    if (p === '/api/v1/reference/currencies') return route.fulfill({ json: [{ code: 'SYP', nameAr: 'ليرة سورية', nameEn: 'Syrian Pound' }] })
    if (p === '/api/v1/reference/regions') return route.fulfill({ json: [{ code: 'DM', nameAr: 'دمشق', nameEn: 'Damascus' }] })
    if (p === '/api/v1/reference/categories') return route.fulfill({ json: [{ code: 'general', nameAr: 'عام', nameEn: 'General' }] })
    // MSP-84: /suppliers/me/users and /auth/sessions return Page<T> now, not bare arrays.
    if (p === '/api/v1/suppliers/me/users') return route.fulfill({ json: { items: [{ userId: 'u1', email: 'teammate@example.com', fullName: 'Teammate One', isActive: true }], hasMore: false, nextCursor: null } })
    if (p === '/api/v1/auth/sessions') return route.fulfill({ json: { items: [{ familyId: 'f1', ip: '127.0.0.1', userAgent: 'axe-scan', createdAt: '2026-08-01T00:00:00Z', expiresAt: '2026-09-01T00:00:00Z', isCurrent: true }], hasMore: false, nextCursor: null } })
    // MSP-84: /review/queue returns Page<ReviewQueueItemDto> now, not a bare array.
    if (p === '/api/v1/review/queue') return route.fulfill({ json: { items: [{ referenceCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'UnderReview' }], hasMore: false, nextCursor: null } })
    if (p === `/api/v1/review/${REFERENCE_CODE}`) return route.fulfill({ json: { supplier: SUPPLIER_PROFILE, documents: DOCUMENT_TYPES, annotationHistory: [] } })
    if (p === '/api/v1/registrations/verify' && method === 'POST') return route.fulfill({ json: {} })
    // Task #7/Stage C: list endpoints return real arrays, not the generic {} fallback below -
    // an empty object crashes OrganizationsPage's .map() the same way any list page would.
    if (p === '/api/v1/organizations') return route.fulfill({ json: [] })
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
    if (p === '/api/v1/rfqs') return route.fulfill({ json: [RFQ_FIXTURE] })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}`) return route.fulfill({ json: RFQ_FIXTURE })
    if (p === `/api/v1/rfqs/${RFQ_REFERENCE_CODE}/invitations/candidates`) return route.fulfill({ json: [] })
    // EPIC-08: supplier-facing invitation list/detail - same class of bug as above if left
    // unmocked (SupplierRfqListPage/SupplierRfqDetailPage would fall through to the generic {}).
    if (p === '/api/v1/suppliers/me/rfqs') return route.fulfill({ json: [SUPPLIER_RFQ_FIXTURE] })
    if (p === `/api/v1/suppliers/me/rfqs/${RFQ_REFERENCE_CODE}`) return route.fulfill({ json: SUPPLIER_RFQ_FIXTURE })
    // EPIC-09: same class of bug - SupplierProposalPage would fall through to the generic {}
    // fallback below and crash reading proposal.items.
    if (p === `/api/v1/suppliers/me/rfqs/${RFQ_REFERENCE_CODE}/proposal`) return route.fulfill({ json: PROPOSAL_FIXTURE })

    // Anything else (mutation endpoints no initial render triggers, unanticipated GETs): benign
    // empty success, so an unmocked call cannot crash the page under scan.
    return route.fulfill({ status: 200, json: {} })
  })
}
