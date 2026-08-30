import { test, expect, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const dirname = path.dirname(fileURLToPath(import.meta.url))

/**
 * NFR-A11Y-001/002/003/007: axe against every real application route, not Storybook components.
 *
 * `tests/e2e/storybook-axe.spec.ts` scans 8 isolated components. It has never scanned a page a
 * supplier or reviewer actually uses - 0 of 18 application routes, confirmed by the Phase 4
 * denominator sweep. This file is what closes that gap, and it exists specifically because that
 * gap was found: a gate that scans nothing real is the shape this whole hardening arc keeps
 * finding, and this suite was written to not become the eighth instance of it.
 */

// ---- THE DENOMINATOR, derived from the router source, not hand-typed ----------------------
//
// Route coverage must fail when it silently shrinks - the same failure class as `tsc --noEmit`
// checking zero files, or the axe suite that inspired this one covering 8 components while
// believing it covered the app. So this list is parsed out of src/router.tsx at test-collection
// time rather than copied by hand into this file: a route added to the router without ever
// reaching this suite is a route this parser will find and a page this suite will then scan,
// automatically. The alternative - a hand-maintained array - is exactly the kind of denominator
// that drifts quietly, which is what this ticket exists to stop.
interface RouteEntry {
  name: string
  fullPath: string
  requiresSupplierAuth: boolean
  requiresReviewerAuth: boolean
}

function extractRoutes(): RouteEntry[] {
  const source = readFileSync(path.join(dirname, '../../src/router.tsx'), 'utf-8')
  const blocks = [...source.matchAll(/const (\w+Route) = createRoute\(\{([\s\S]*?)\n\}\)/g)]

  type Raw = { name: string; body: string; path: string | null; id: string | null; parent: string | null }
  const raw: Raw[] = blocks.map(([, name, body]) => {
    const pathMatch = body.match(/path:\s*'([^']*)'/)
    const idMatch = body.match(/id:\s*'([^']*)'/)
    const parentMatch = body.match(/getParentRoute:\s*\(\)\s*=>\s*(\w+)/)
    return {
      name,
      body,
      path: pathMatch ? pathMatch[1] : null,
      id: idMatch ? idMatch[1] : null,
      parent: parentMatch ? parentMatch[1] : null,
    }
  })

  const byName = new Map(raw.map((r) => [r.name, r]))

  function resolveFullPath(r: Raw): string {
    const own = r.path ?? ''
    if (r.parent === 'rootRoute' || !r.parent) return own
    const parent = byName.get(r.parent)
    if (!parent) return own
    const parentPath = resolveFullPath(parent)
    if (!own) return parentPath
    // Both segments present and non-root: join without doubling the slash.
    return `${parentPath === '/' ? '' : parentPath}${own}`
  }

  // Shell/layout routes render only `<Shell><Outlet /></Shell>` - chrome around a child route,
  // never independent content of their own. Excluded from the page count on that basis, not on
  // whether they happen to declare a `path:` - backOfficeLayoutRoute does, and is still a shell.
  const isShellWrapper = (body: string) => /<\w*Shell>[\s\S]*<Outlet \/>/.test(body)

  return raw
    .filter((r) => !isShellWrapper(r.body))
    .map((r) => {
      const fullPath = resolveFullPath(r)
      const requiresSupplierAuth = r.parent === 'supplierLayoutRoute'
      const requiresReviewerAuth = r.parent === 'backOfficeLayoutRoute'
      return { name: r.name, fullPath, requiresSupplierAuth, requiresReviewerAuth }
    })
}

const routes = extractRoutes()

test('the route denominator is what the router actually declares, not what this file assumes', () => {
  // The assertion this whole suite exists to carry. A router change that silently drops a route
  // out of this count must fail here, loudly, rather than the suite quietly scanning fewer pages
  // than it did yesterday and staying green throughout.
  //
  // 18, matching the ticket's stated number - but arrived at here, not assumed: the extraction
  // logic's own first run asserted '/review' where the router actually composes
  // '/back-office/review' (reviewQueueRoute's parent is the /back-office layout), and this
  // assertion caught that before it ever reached a scan. Left as the record of why this suite
  // trusts the parser's output over a hand-typed path list, including its own.
  expect(routes.length).toBe(18)
  expect(routes.map((r) => r.fullPath)).toEqual(
    expect.arrayContaining(['/login', '/dashboard', '/back-office/review']),
  )
})

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

const SUPPLIER_ID = '01a00000-0000-7000-8000-000000000001'
const supplierToken = fakeJwt({
  sub: '01a00000-0000-7000-8000-0000000000a1',
  email: 'a11y-supplier@example.com',
  supplierId: SUPPLIER_ID,
  perms: ['supplier.profile.edit', 'supplier.documents.upload', 'supplier.users.manage'],
})
const reviewerToken = fakeJwt({
  sub: '01a00000-0000-7000-8000-0000000000a2',
  email: 'a11y-reviewer@example.com',
  perms: ['review.read', 'review.decide'],
})

const REFERENCE_CODE = 'SUP-2026-000001'

const SUPPLIER_PROFILE = {
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

const DOCUMENT_TYPES = [
  { documentTypeId: 'd1', code: 'commercial_registration', nameAr: 'السجل التجاري', nameEn: 'Commercial Registration', isRequired: true, expiryTracked: false, latestDocument: null },
  { documentTypeId: 'd2', code: 'tax_certificate', nameAr: 'الشهادة الضريبية', nameEn: 'Tax Certificate', isRequired: true, expiryTracked: true, latestDocument: { id: 'doc1', version: 1, state: 'Approved', originalFileName: 'tax-cert.pdf', contentType: 'application/pdf', sizeBytes: 102400, issueDate: '2026-01-01', expiryDate: '2027-01-01', rejectReason: null, uploadedAt: '2026-01-01T00:00:00Z', reviewedAt: '2026-01-02T00:00:00Z' } },
  { documentTypeId: 'd3', code: 'chamber_membership', nameAr: 'عضوية الغرفة التجارية', nameEn: 'Chamber Membership', isRequired: false, expiryTracked: true, latestDocument: null },
]

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
async function mockBackend(page: Page) {
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
    if (p === '/api/v1/suppliers/me/users') return route.fulfill({ json: [{ userId: 'u1', email: 'teammate@example.com', fullName: 'Teammate One', isActive: true }] })
    if (p === '/api/v1/auth/sessions') return route.fulfill({ json: [{ familyId: 'f1', ip: '127.0.0.1', userAgent: 'axe-scan', createdAt: '2026-08-01T00:00:00Z', expiresAt: '2026-09-01T00:00:00Z', isCurrent: true }] })
    if (p === '/api/v1/review/queue') return route.fulfill({ json: [{ referenceCode: REFERENCE_CODE, displayNameAr: SUPPLIER_PROFILE.displayNameAr, displayNameEn: SUPPLIER_PROFILE.displayNameEn, onboardingState: 'UnderReview' }] })
    if (p === `/api/v1/review/${REFERENCE_CODE}`) return route.fulfill({ json: { supplier: SUPPLIER_PROFILE, documents: DOCUMENT_TYPES, annotationHistory: [] } })
    if (p === '/api/v1/registrations/verify' && method === 'POST') return route.fulfill({ json: {} })

    // Anything else (mutation endpoints no initial render triggers, unanticipated GETs): benign
    // empty success, so an unmocked call cannot crash the page under scan.
    return route.fulfill({ status: 200, json: {} })
  })
}

// ---- The scan itself, both directions, both locales ---------------------------------------

for (const route of routes) {
  for (const locale of ['ar', 'en'] as const) {
    test(`a11y: ${route.fullPath} [${locale}]`, async ({ page }) => {
      await mockBackend(page)

      let target = route.fullPath
      if (target === '/reset-password' || target === '/verify-email' || target === '/accept-invite') {
        target += '?token=fake-token-for-a11y-scan'
      }
      if (route.name === 'reviewApplicationRoute') {
        target = target.replace('$referenceCode', REFERENCE_CODE)
      }

      await page.goto(`${target}${target.includes('?') ? '&' : '?'}lng=${locale}`, { waitUntil: 'networkidle' })

      // Guards against a false "clean" scan: if the page fell through to the error boundary
      // instead of its real content (a mock gap, a route mismatch), the DOM axe would inspect is
      // the 404/500 screen, not the page under test - a pass there proves nothing about NFR-A11Y.
      await expect(page.getByText(/^(404|500)$/)).toHaveCount(0);

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag22aa'])
        .analyze()

      expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([])
    })
  }
}
