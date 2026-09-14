// Three groups: the session controls, the account read and write, and SCR-903's password change.
//
// SCR-902 mounted this page for every authenticated persona, and the activity-trail card is supplier-scoped - it reads
// GET /suppliers/me/audit, which a procurement officer cannot. So the card renders only when the caller's claims carry a
// supplierId, and a test that wants to see it has to say who is signed in. The fixture said nothing before, which was a
// fixture claiming a supplier's screen while presenting nobody's session. Every test here performs SCR-902's read.
//
// THE SESSION FLOW is Task #19's: revokeMutation's onSuccess - queryClient.invalidateQueries, now invalidateQuietly - was
// one of the eight no-floating-promises findings, so these drive a real revoke through the page and the callback actually
// runs, matching the coverage gap Sonar's new-code ratchet flagged. The revoke-all test seeds two sessions, because the
// "Sign out of all other devices" button is disabled when there is at most one: the guard reasons that a lone visible
// session could still be undercounting a page not yet fetched, but with none loaded there is nothing else TO revoke.
//
// THE ACTIVITY TRAIL is B-1 and FR-AUD-003's. The list AND its export have existed since EPIC-01 and nothing called
// either - a compliance affordance that shipped unreachable, found by the phase 12a sweep. The action shows its own token
// rather than a translated label, because §7 has no table for audit actions and inventing one would put a second
// vocabulary beside the one the trail records. The export is FETCHED and handed to the browser rather than linked: it
// needs the Authorization header, so a plain anchor would arrive unauthenticated and answer 401 - which is why an export
// that existed was never reachable from a screen. And the trail is HIDDEN from a staff persona, which is the guard both
// ways: it reads /suppliers/me/audit, which a procurement officer cannot, so the card has to be absent rather than
// present-and-failing.
//
// THE ACCOUNT saves a new name and language, and the interface follows. No save is offered until something actually
// changes, which is the control for that: the button exists and is refused, so a green save is the edit reaching the
// server rather than the button being clickable at all times.
//
// SCR-903's PASSWORD CHANGE is worth testing rather than clicking because its whole value is WHICH field an error lands
// on: the server distinguishes a wrong current password from a new one that is unchanged or too weak, and a screen that
// funnelled all three into one toast would send the person to re-type the wrong box.
//
// It refuses to submit until both new-password boxes agree, and refuses a new password shorter than the stated minimum -
// the hint says twelve, and a form that says twelve and submits eleven makes the server the only thing enforcing a rule
// the screen already claimed. Once valid it submits both passwords. INCORRECT_CURRENT_PASSWORD lands on the
// current-password field; the two complaints about the NEW password land on that one, and both used to be
// indistinguishable from a wrong current password if they arrived as a toast.
//
// The form is CLEARED after a successful change: three password boxes left populated after a success read as "it did not
// work", and the second attempt would then fail on INCORRECT_CURRENT_PASSWORD because the old one is now wrong.
//
// The last test asserts two halves nothing did before: that the error branch RENDERS, and that the control inside it does
// anything. asyncStateCoverage proves the branch exists in the source; a retry button wired to nothing looks identical to
// one that works.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage, type RecordedRequest, expectRetryableFailure } from '../test/renderPage'

const { SettingsPage } = await import('./SettingsPage')
const { useAuthStore } = await import('../lib/authStore')

const account = { '/api/v1/auth/me': { fullName: 'Layla Haddad', email: 'supplier@example.test', language: 'en', languageChosen: true } }

function signInAsSupplier() {
  useAuthStore.setState({
    accessToken: 'test',
    status: 'authenticated',
    claims: { userId: 'u-1', email: 'supplier@example.test', supplierId: 'sup-1', permissions: [] },
  })
}

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
    expect(await screen.findByText('supplier_submitted')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Download the trail (CSV)' }))

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

    await screen.findByLabelText(/Full name/)
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('hides the supplier-only activity trail from a staff persona', async () => {
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

describe('SettingsPage change password (SCR-903)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  const sessions = { '/api/v1/auth/sessions': listPage([]) }
  const CHANGE = '/api/v1/auth/change-password'

  async function fill(current: string, next: string, confirm: string) {
    await userEvent.type(await screen.findByLabelText(/current password/i), current)
    await userEvent.type(screen.getByLabelText(/^new password/i), next)
    await userEvent.type(screen.getByLabelText(/confirm/i), confirm)
  }

  it('refuses to submit until both new-password boxes agree', async () => {
    signInAsSupplier()
    restore = mockFetch({ ...account, ...sessions })

    renderPage(<SettingsPage />)
    await fill('current-secret', 'a-long-enough-password', 'a-long-enough-passwerd')

    expect(screen.getByRole('button', { name: /change password/i })).toBeDisabled()
    expect(screen.getByText(/do not match|لا يتطابقان/i)).toBeInTheDocument()
  })

  it('refuses a new password shorter than the stated minimum', async () => {
    signInAsSupplier()
    restore = mockFetch({ ...account, ...sessions })

    renderPage(<SettingsPage />)
    await fill('current-secret', 'short-pass', 'short-pass')

    expect(screen.getByRole('button', { name: /change password/i })).toBeDisabled()
  })

  it('submits both passwords once the form is valid', async () => {
    signInAsSupplier()
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ ...account, ...sessions, [CHANGE]: {} }, recorded)

    renderPage(<SettingsPage />)
    await fill('current-secret', 'a-long-enough-password', 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: /change password/i }))

    const post = recorded.find((r) => r.url.includes('change-password'))
    expect(JSON.parse(post!.body)).toEqual({
      currentPassword: 'current-secret',
      newPassword: 'a-long-enough-password',
    })
  })

  it('puts INCORRECT_CURRENT_PASSWORD on the current-password field', async () => {
    signInAsSupplier()
    restore = mockFetch({ ...account, ...sessions, [CHANGE]: { __status: 400, code: 'INCORRECT_CURRENT_PASSWORD' } })

    renderPage(<SettingsPage />)
    await fill('wrong-secret', 'a-long-enough-password', 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: /change password/i }))

    const field = await screen.findByLabelText(/current password/i)
    expect(field).toHaveAccessibleDescription(/not your current password|غير صحيحة/i)
  })

  it.each([
    ['PASSWORD_UNCHANGED', /same|نفس/i],
    ['WEAK_PASSWORD', /weak|ضعيف|requirement/i],
  ])('puts %s on the new-password field', async (code, expected) => {
    signInAsSupplier()
    restore = mockFetch({ ...account, ...sessions, [CHANGE]: { __status: 400, code } })

    renderPage(<SettingsPage />)
    await fill('current-secret', 'a-long-enough-password', 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: /change password/i }))

    const field = await screen.findByLabelText(/^new password/i)
    expect(field).toHaveAccessibleDescription(expected)
  })

  it('clears the form after a successful change', async () => {
    signInAsSupplier()
    restore = mockFetch({ ...account, ...sessions, [CHANGE]: {} })

    renderPage(<SettingsPage />)
    await fill('current-secret', 'a-long-enough-password', 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: /change password/i }))

    await screen.findByText(/password changed|تم تغيير/i)
    expect(await screen.findByLabelText(/current password/i)).toHaveValue('')
    expect(screen.getByLabelText(/^new password/i)).toHaveValue('')
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/auth/me': { __status: 500 } }, recorded)

    renderPage(<SettingsPage />)

    await expectRetryableFailure('/api/v1/auth/me', recorded)
  })
})
