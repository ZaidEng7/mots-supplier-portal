import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage } from '../test/renderPage'

const { SettingsPage } = await import('./SettingsPage')
const { useAuthStore } = await import('../lib/authStore')

/**
 * SCR-902 mounted this page for every authenticated persona, and the activity-trail card is
 * supplier-scoped - it reads `GET /suppliers/me/audit`, which a procurement officer cannot. So the
 * card now renders only when the caller's claims carry a supplierId, and a test that wants to see it
 * has to say who is signed in. The fixture said nothing before, which was a fixture claiming a
 * supplier's screen while presenting nobody's session.
 */
/** SCR-902's read, which every test on this page now performs. */
const account = { '/api/v1/auth/me': { fullName: 'Layla Haddad', email: 'supplier@example.test', language: 'en', languageChosen: true } }

function signInAsSupplier() {
  useAuthStore.setState({
    accessToken: 'test',
    status: 'authenticated',
    claims: { userId: 'u-1', email: 'supplier@example.test', supplierId: 'sup-1', permissions: [] },
  })
}

/**
 * Task #19: revokeMutation's onSuccess (queryClient.invalidateQueries, now invalidateQuietly) was
 * one of the eight no-floating-promises findings. Drives a real revoke through the page so the
 * callback actually runs, matching the coverage gap Sonar's new-code ratchet flagged.
 */
describe('SettingsPage session revoke flow', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows a success toast once a session is revoked', async () => {
    restore = mockFetch({
      ...account,
      '/api/v1/auth/sessions': listPage([
        { familyId: 'family-1', ip: '1.2.3.4', userAgent: 'Other Device', createdAt: new Date().toISOString(), expiresAt: new Date().toISOString(), isCurrent: false },
      ]),
    })

    renderPage(<SettingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Session signed out')).toBeInTheDocument()
  })

  it('shows a success toast once all other sessions are revoked', async () => {
    restore = mockFetch({
      ...account,
      '/api/v1/auth/sessions/revoke-all': { revokedCount: 2 },
      // Two sessions: the "Sign out of all other devices" button is disabled when there is at
      // most one (the guard reasons that a lone visible session could still be undercounting a
      // page not yet fetched, but with none loaded there is nothing else TO revoke).
      '/api/v1/auth/sessions': listPage([
        { familyId: 'family-1', ip: '1.2.3.4', userAgent: 'Other Device', createdAt: new Date().toISOString(), expiresAt: new Date().toISOString(), isCurrent: false },
        { familyId: 'family-2', ip: '5.6.7.8', userAgent: 'This Device', createdAt: new Date().toISOString(), expiresAt: new Date().toISOString(), isCurrent: true },
      ]),
    })

    renderPage(<SettingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Sign out of all other devices' }))

    expect(await screen.findByText('Signed out of all other devices')).toBeInTheDocument()
  })

  it('shows the supplier their own activity trail and offers the CSV', async () => {
    signInAsSupplier()
    // B-1/FR-AUD-003. The list AND its export have existed since EPIC-01 and nothing called either - a
    // compliance affordance that shipped unreachable, found by the phase 12a sweep.
    const created = vi.fn()
    const clicked = vi.fn()
    const originalCreate = URL.createObjectURL
    const originalRevoke = URL.revokeObjectURL
    URL.createObjectURL = vi.fn(() => { created(); return 'blob:trail' })
    URL.revokeObjectURL = vi.fn()
    const originalClick = HTMLAnchorElement.prototype.click
    HTMLAnchorElement.prototype.click = clicked

    restore = mockFetch({
      ...account,
      '/api/v1/suppliers/me/audit/export': {},
      '/api/v1/suppliers/me/audit': {
        data: [{
          id: 'a-1', occurredAt: '2026-09-01T10:00:00Z', aggregateType: 'Supplier',
          aggregateId: 's-1', action: 'supplier_submitted', fromState: 'ProfileInProgress',
          toState: 'Submitted', actorLabel: null,
        }],
        pagination: { hasMore: false, nextCursor: null },
      },
      '/api/v1/auth/sessions': { data: [], pagination: { hasMore: false, nextCursor: null } },
      '/api/v1/auth/mfa/status': { enabled: false },
    })

    renderPage(<SettingsPage />)

    expect(await screen.findByText('My account activity')).toBeInTheDocument()
    // The action's own token, not a translated label: §7 has no table for audit actions, and inventing
    // one would put a second vocabulary beside the one the trail records.
    expect(await screen.findByText('supplier_submitted')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Download the trail (CSV)' }))

    // Fetched and handed to the browser rather than linked: the export needs the Authorization header, so
    // a plain anchor would arrive unauthenticated and answer 401 - which is why an export that existed
    // was never reachable from a screen.
    await vi.waitFor(() => expect(created).toHaveBeenCalled())
    expect(clicked).toHaveBeenCalled()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:trail')

    URL.createObjectURL = originalCreate
    URL.revokeObjectURL = originalRevoke
    HTMLAnchorElement.prototype.click = originalClick
  })

  it('saves a new name and language, and the interface follows', async () => {
    signInAsSupplier()
    restore = mockFetch({
      ...account,
      '/api/v1/auth/sessions': listPage([]),
    })

    renderPage(<SettingsPage />)

    const name = await screen.findByLabelText(/Full name/)
    await userEvent.clear(name)
    await userEvent.type(name, 'Layla H. Haddad')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Changes saved')).toBeInTheDocument()
  })

  it('offers no save until something actually changes', async () => {
    signInAsSupplier()
    restore = mockFetch({ ...account, '/api/v1/auth/sessions': listPage([]) })

    renderPage(<SettingsPage />)

    // The control for the test above: the button exists and is refused, so a green save there is the
    // edit reaching the server rather than the button being clickable at all times.
    await screen.findByLabelText(/Full name/)
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('hides the supplier-only activity trail from a staff persona', async () => {
    // Guard both ways. The trail reads `/suppliers/me/audit`, which a procurement officer cannot, so
    // the card has to be absent rather than present-and-failing.
    useAuthStore.setState({
      accessToken: 'test',
      status: 'authenticated',
      claims: { userId: 'u-2', email: 'officer@example.test', organizationId: 'org-1', permissions: [] },
    })
    restore = mockFetch({ ...account, '/api/v1/auth/sessions': listPage([]) })

    renderPage(<SettingsPage />)

    await screen.findByLabelText(/Full name/)
    expect(screen.queryByText('My account activity')).not.toBeInTheDocument()
  })
})
