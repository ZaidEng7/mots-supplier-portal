// SCR-040. The point of the overlay is that the page underneath is NOT remounted, so the assertions here are about when
// it appears and what it does to the store - the survival of the work below it is a property of where it is mounted,
// outside the Outlet, which the router owns.
//
// The control comes first: it stays out of the way until a live session expires, because an overlay that rendered
// unconditionally would pass the next test just as happily. And no expiry is raised over someone who never signed in: a
// 401 on a public page must not produce "your session expired", and expireSession only marks an expiry when there was a
// session to lose.
//
// On expiry it appears, offering the address it already knows.
//
// It cannot be dismissed by Escape, because there is nothing behind it to go back to. Phase 4 replaced a hand-rolled
// overlay that declared role="dialog" aria-modal="true" and trapped nothing, so Tab walked out into a page the reader
// could no longer save; the replacement refuses Escape, outside pointer-down and outside interaction, and until now
// that refusal was a claim in a note. The way out is the sign-out button.
//
// It also makes the page behind it INERT, so an outside click cannot even be delivered. That is not the same claim as
// "the outside-click handler is prevented", and a stronger one: Radix's modal mode sets pointer-events: none on the
// body and marks the background aria-hidden, so the page below is unreachable by pointer AND by assistive technology -
// which is what the hand-rolled overlay this replaced never did, while declaring aria-modal="true" and looking
// identical.
//
// It closes itself when the re-authentication succeeds, driven with a token whose payload decodes, because the store
// reads claims out of it. And it leaves a way out for someone who cannot remember their password: signing out discards
// the remembered address as well, because the next person at this browser is not the last one.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

const { SessionExpiredOverlay } = await import('./SessionExpiredOverlay')
const { useAuthStore } = await import('../lib/authStore')

describe('SessionExpiredOverlay', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  function signedIn() {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'officer@example.test', permissions: [] },
      expired: false,
      lastEmail: 'officer@example.test',
    })
  }

  it('stays out of the way until a live session expires', () => {
    renderPage(<SessionExpiredOverlay />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('does not raise an expiry over someone who never signed in', () => {
    useAuthStore.setState({ status: 'unauthenticated' })
    useAuthStore.getState().expireSession()

    renderPage(<SessionExpiredOverlay />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('appears on expiry, offering the address it already knows', () => {
    signedIn()
    useAuthStore.getState().expireSession()

    renderPage(<SessionExpiredOverlay />)

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('officer@example.test')).toBeInTheDocument()
  })

  it('cannot be dismissed by Escape, because there is nothing behind it to go back to', async () => {
    signedIn()
    useAuthStore.setState({ expired: true })
    renderPage(<SessionExpiredOverlay />)

    expect(await screen.findByRole('dialog')).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(useAuthStore.getState().expired).toBe(true)
  })

  it('makes the page behind it inert, so an outside click cannot even be delivered', async () => {
    signedIn()
    useAuthStore.setState({ expired: true })
    renderPage(<SessionExpiredOverlay />)

    await screen.findByRole('dialog')

    expect(document.body.style.pointerEvents).toBe('none')
    await expect(userEvent.click(document.body)).rejects.toThrow(/pointer-events: none/)
  })

  it('closes itself when the re-authentication succeeds', async () => {
    signedIn()
    useAuthStore.getState().expireSession()
    const payload = btoa(JSON.stringify({ sub: 'u-1', email: 'officer@example.test', perms: [] }))
    restore = mockFetch({ '/api/v1/auth/login': { accessToken: `x.${payload}.y`, accessTokenExpiresAt: new Date().toISOString() } })

    renderPage(<SessionExpiredOverlay />)

    await userEvent.type(screen.getByLabelText(/Password/), 'correct-horse-battery')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(useAuthStore.getState().status).toBe('authenticated')
    expect(useAuthStore.getState().expired).toBe(false)
  })

  it('leaves a way out for someone who cannot remember their password', async () => {
    signedIn()
    useAuthStore.getState().expireSession()

    renderPage(<SessionExpiredOverlay />)
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(useAuthStore.getState().expired).toBe(false)
    expect(useAuthStore.getState().lastEmail).toBeNull()
  })
})
