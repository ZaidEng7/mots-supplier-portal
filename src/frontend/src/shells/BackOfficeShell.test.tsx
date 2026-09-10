import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { renderPage } from '../test/renderPage'

/**
 * The router is mocked, not wired. renderPage deliberately excludes it (see its own doc comment), and what
 * this file is about is which links a shell DECIDES to render — a decision made from the token's
 * permissions, with no routing involved. A real route tree here would make a nav test depend on every page
 * in the product.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: ReactNode }) => <a href={to}>{children}</a>,
  // Present because the module exports it; these shells take their content as `children`.
  Outlet: () => null,
  // The shell asks the router where it is so the rail can mark the current row. A path that matches no
  // destination keeps these tests about permissions, which is what they are for.
  useRouterState: () => '/back-office/nowhere-in-particular',
}))

const { BackOfficeShell } = await import('./BackOfficeShell')
const { useAuthStore } = await import('../lib/authStore')

/**
 * T-084. Both shells were among the fifteen untested components, and a shell is where a permission mistake
 * is most visible: a link shown to someone who cannot use it produces a 403 they cannot explain, and a link
 * hidden from someone who can produces a feature nobody finds.
 */
describe('BackOfficeShell navigation', () => {
  afterEach(() => {
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  // `null` rather than `undefined` for "no buying body": passing undefined to a parameter that has a
  // default gets the default, so the first version of this helper silently gave every caller org-1.
  function signInWith(permissions: string[], organizationId: string | null = 'org-1') {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'staff@example.test', permissions, organizationId: organizationId ?? undefined },
    })
  }

  const hrefs = () => [...document.querySelectorAll('a')].map((a) => a.getAttribute('href'))

  it('shows an administrator the admin-only destinations', () => {
    signInWith(['admin.users.manage', 'audit.read'])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).toContain('/back-office/settings')
    expect(hrefs()).toContain('/back-office/audit')
    // Batch 11's own additions, which are gated the same way.
    expect(hrefs()).toContain('/back-office/operations')
    expect(hrefs()).toContain('/back-office/ui-strings')
  })

  it('hides every one of them from a persona without the permission', () => {
    // The control, and the half that actually protects anyone: an officer holds rfq.read and nothing
    // administrative, so none of the admin destinations may appear.
    signInWith(['rfq.read'])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).not.toContain('/back-office/settings')
    expect(hrefs()).not.toContain('/back-office/audit')
    expect(hrefs()).not.toContain('/back-office/operations')
    expect(hrefs()).not.toContain('/back-office/ui-strings')
  })

  it('shows every persona the things that are theirs regardless of permission', () => {
    // Account, help and search are ungated on purpose - every signed-in user owns their own account, and
    // what a search returns is decided per entity on the server. This asserts that the gating above did not
    // accidentally sweep them up.
    signInWith([])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).toContain('/back-office/account')
    expect(hrefs()).toContain('/back-office/help')
    expect(hrefs()).toContain('/back-office/search')
  })

  it('hides the procurement destinations from an account that belongs to no buying body', () => {
    // A system_admin holds every permission and belongs to no organization, and BRULE-029 scopes every
    // procurement query by organization - so these screens answer 404 for them. The links were offered
    // anyway, and "Couldn't load the dashboard - Try again" invites a retry that cannot succeed.
    signInWith(['rfq.read', 'offering.search', 'evaluation.template.manage', 'audit.read'], null)

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).not.toContain('/back-office/procurement')
    expect(hrefs()).not.toContain('/back-office/rfqs')
    expect(hrefs()).not.toContain('/back-office/offerings')
    expect(hrefs()).not.toContain('/back-office/evaluation-templates')
    // The control: what this persona CAN reach is untouched.
    expect(hrefs()).toContain('/back-office/audit')
  })

  it('still shows them to staff who are in one', () => {
    signInWith(['rfq.read', 'offering.search', 'evaluation.template.manage'])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).toContain('/back-office/procurement')
    expect(hrefs()).toContain('/back-office/rfqs')
  })

  it('renders the page it wraps, so anything inside it is reachable at all', () => {
    // The shell takes its content as `children` rather than rendering an Outlet - checked in the component
    // rather than assumed from the router mock, which is why the mock's Outlet is unused here.
    signInWith(['rfq.read'])
    renderPage(<BackOfficeShell><div data-testid="page" /></BackOfficeShell>)
    expect(screen.getByTestId('page')).toBeInTheDocument()
  })

  it('shows an evaluator the link to their own dashboard, and hides the review link they cannot use', () => {
    // Found by signing in as the seeded evaluator: they landed on the shared placeholder dashboard, their nav
    // had no link to /evaluation at all, and it DID offer Supplier Application Review - a 403 waiting to
    // happen, on the only work-shaped link they had.
    signInWith(['evaluation.score', 'evaluation.submit', 'rfq.clarify'])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).toContain('/evaluation')
    expect(hrefs()).not.toContain('/back-office/review')
  })

  it('still shows the review link to a reviewer', () => {
    // The control: the link was ungated, so hiding it from an evaluator must not hide it from the persona
    // whose whole job it is.
    signInWith(['supplier.review'])

    renderPage(<BackOfficeShell><div /></BackOfficeShell>)

    expect(hrefs()).toContain('/back-office/review')
    expect(hrefs()).not.toContain('/evaluation')
  })
})
