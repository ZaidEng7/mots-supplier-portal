import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage } from '../test/renderPage'

const { SettingsPage } = await import('./SettingsPage')

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
})
