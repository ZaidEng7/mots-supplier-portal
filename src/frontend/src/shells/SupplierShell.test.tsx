import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { renderPage, mockFetch } from '../test/renderPage'

/**
 * The router is mocked rather than wired: renderPage deliberately excludes it, and what this file asserts is
 * which links the shell DECIDES to render - a decision made from the token's permissions with no routing in
 * it. `useRouterState` is here because the mobile tab bar inside this shell highlights the current route
 * from it; a fixed pathname is enough, since none of these tests are about which tab looks active.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: ReactNode }) => <a href={to}>{children}</a>,
  Outlet: () => null,
  useRouterState: ({ select }: { select: (state: { location: { pathname: string } }) => unknown }) =>
    select({ location: { pathname: '/dashboard' } }),
}))

const { SupplierShell } = await import('./SupplierShell')
const { useAuthStore } = await import('../lib/authStore')

/**
 * T-084. The supplier's chrome, and the place batch 11 added three destinations (profile, documents,
 * proposals) plus the ERP banner - so it is also where a nav regression would strand a supplier on a
 * dashboard with no way to reach their own work.
 */
describe('SupplierShell navigation', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  function signInAsSupplier() {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'supplier@example.test', supplierId: 'sup-1', permissions: ['rfq.read', 'proposal.create'] },
    })
  }

  const hrefs = () => [...document.querySelectorAll('a')].map((a) => a.getAttribute('href'))

  it('offers every screen a supplier owns', () => {
    signInAsSupplier()
    // The shell polls /system/status for the ERP banner; declared so an undeclared request cannot throw
    // and take the nav assertions with it.
    restore = mockFetch({ '/api/v1/system/status': { erpNotConfigured: false, expiringDocumentCount: 0 } })

    renderPage(<SupplierShell><div /></SupplierShell>)

    // Each of these was a screen that existed with no way to reach it before this batch, which is exactly
    // the failure a nav test catches and a page test cannot.
    expect(hrefs()).toContain('/profile')
    expect(hrefs()).toContain('/documents')
    expect(hrefs()).toContain('/proposals')
    expect(hrefs()).toContain('/settings')
    expect(hrefs()).toContain('/help')
  })

  it('renders the page it wraps', () => {
    signInAsSupplier()
    restore = mockFetch({ '/api/v1/system/status': { erpNotConfigured: false, expiringDocumentCount: 0 } })

    renderPage(<SupplierShell><div data-testid="page" /></SupplierShell>)

    expect(screen.getByTestId('page')).toBeInTheDocument()
  })

  it('renders its chrome even when the status poll fails', () => {
    // The ERP banner is a convenience, and a shell that broke when its own optional poll failed would take
    // the whole supplier area down with it. Asserted rather than assumed from the banner's code.
    restore = mockFetch({})
    signInAsSupplier()

    renderPage(<SupplierShell><div data-testid="page" /></SupplierShell>)

    expect(screen.getByTestId('page')).toBeInTheDocument()
    expect(hrefs()).toContain('/proposals')
  })
})
