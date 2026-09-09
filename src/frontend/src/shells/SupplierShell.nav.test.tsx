import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'

let currentPath = '/rfqs'
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useRouterState: () => currentPath,
    Link: ({ to, children, ...rest }: { to: string; children: React.ReactNode }) => <a href={to} {...rest}>{children}</a>,
  }
})

const { SupplierShell } = await import('./SupplierShell')

// The shell mounts the ERP banner and the notification bell, both of which fetch. renderPage supplies the
// QueryClient; these two routes keep them from throwing on an undeclared URL.
const ROUTES = { '/api/v1/system/erp-status': { degraded: false }, '/api/v1/notifications/unread-count': { count: 0 } }

/**
 * §D3: twelve flat top-level links on every supplier screen, "Complete Profile" and "Profile" adjacent
 * with nothing to tell them apart, and no visible current page.
 *
 * <p>Two named landmarks and an account cluster now. The names matter more than the visual grouping: a
 * screen-reader user gets somewhere to jump to, which a row of links separated by a hairline does not
 * provide. And `aria-current` says which page you are on, where colour alone would say it only to people
 * who can see it.</p>
 */
describe('the supplier navigation is grouped', () => {
  it('offers named groups rather than one flat list', () => {
    const restore = mockFetch(ROUTES)
    renderPage(<SupplierShell>{null}</SupplierShell>)
    restore()

    const groups = screen.getAllByRole('navigation').map((n) => n.getAttribute('aria-label'))
    expect(groups).toEqual(expect.arrayContaining(['Bidding', 'Your company', 'Account']))
  })

  it('says which page you are on, not only colours it', () => {
    currentPath = '/rfqs'
    const restore = mockFetch(ROUTES)
    renderPage(<SupplierShell>{null}</SupplierShell>)
    restore()

    const current = screen.getAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page')
    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Tenders')
  })

  it('does not mark the dashboard current from a nested route', () => {
    // The denominator for the test above: a prefix match on "/" would make every page the dashboard.
    currentPath = '/rfqs'
    const restore = mockFetch(ROUTES)
    renderPage(<SupplierShell>{null}</SupplierShell>)
    restore()

    const dashboard = screen.getAllByRole('link').find((a) => a.textContent === 'Dashboard')
    expect(dashboard).toBeDefined()
    expect(dashboard).not.toHaveAttribute('aria-current')
  })
})
