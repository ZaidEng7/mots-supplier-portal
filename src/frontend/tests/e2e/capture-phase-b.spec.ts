import { expect, test } from '@playwright/test'
import { mockBackend } from './fixtures'

/**
 * A member of staff who holds everything, which the shared reviewer fixture does not.
 *
 * <p>That fixture carries `review.read` and `review.decide`, and no navigation row in either shell has
 * ever been gated on those two - so every accessibility sweep of the back office has been rendering a
 * rail with two rows in it. That is worth knowing on its own and is reported rather than fixed here.
 * For a picture of the rail, the account has to be able to see the rail.</p>
 */
const b64url = (value: object) =>
  Buffer.from(JSON.stringify(value)).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')

const everythingToken = `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url({
  sub: '01a00000-0000-7000-8000-0000000000a3',
  email: 'capture-officer@example.com',
  organizationId: '01a00000-0000-7000-8000-0000000000b1',
  perms: [
    'report.read', 'rfq.read', 'evaluation.template.manage', 'evaluation.score', 'supplier.review',
    'supplier.directory.read', 'offering.search', 'governance.read', 'admin.organizations.manage',
    'admin.users.manage', 'admin.roles.manage', 'reference.manage', 'audit.read',
  ],
})}.fake-signature-not-verified-client-side`

/**
 * Not a test - a capture driver for the Phase B shells, kept beside the suite because it needs the same
 * mocked backend the accessibility scans use.
 *
 * <p>Run it on purpose: `CAPTURE=1 npx playwright test capture-phase-b --project=app-a11y`. It asserts
 * nothing about layout and cannot fail the build on it; what the rail must actually do is asserted in
 * `src/shells/Sidebar.test.tsx`, `TopBar.test.tsx` and `reachability.test.tsx`, and how it reads is
 * asserted by the accessibility and reflow projects.</p>
 */
const OUT = '../../COMP-BUILD/shots-phase-b'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

const SCREENS = [
  ['/back-office/dashboard', 'back-office'],
  ['/back-office/rfqs', 'back-office-tenders'],
  ['/dashboard', 'supplier'],
  ['/back-office/rfqs/RFQ-2026-000001/award', 'workspace-award'],
] as const

for (const locale of ['en', 'ar'] as const) {
  for (const [route, name] of SCREENS) {
    for (const [size, width, height] of [['desktop', 1440, 1000], ['narrow', 320, 1200]] as const) {
      test(`capture ${name} ${size} ${locale}`, async ({ page }) => {
        await page.setViewportSize({ width, height })
        await mockBackend(page)
        // Registered after mockBackend so it wins: Playwright matches routes newest first. Only the
        // refresh call, which is the one the fixture answers with a token - a wildcard over the whole
        // auth namespace swallows the other calls too and the session never establishes.
        if (route.startsWith('/back-office')) {
          await page.route('**/api/v1/auth/refresh', (r) => r.fulfill({
            json: { accessToken: everythingToken, accessTokenExpiresAt: new Date(Date.now() + 3600_000).toISOString() },
          }))
        }
        await page.goto(`${route}?lng=${locale}`)
        // A screenshot of a blank page is worthless as evidence and looks identical to a working one in
        // a file listing, so every capture proves the shell rendered before recording it. The shell's
        // own rail rather than the page's heading: what is being photographed here is the shell, and
        // some screens inside it open on a gate or a skeleton that carries no h1.
        await expect(page.getByRole('navigation').first()).toBeVisible()
        await page.screenshot({ path: `${OUT}/${name}-${size}-${locale}.png`, fullPage: false })
      })
    }
  }
}
