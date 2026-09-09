import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

const { SessionExpiredOverlay } = await import('./SessionExpiredOverlay')
const { useAuthStore } = await import('../lib/authStore')

/**
 * SCR-040. The point of the overlay is that the page underneath is NOT remounted, so the assertions
 * here are about when it appears and what it does to the store — the survival of the work below it is
 * a property of where it is mounted (outside the Outlet), which the router owns.
 */
describe('SessionExpiredOverlay', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  /** A working session, the way the store looks after a successful sign-in. */
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
    // The control for every assertion below. An overlay that rendered unconditionally would pass the
    // next test just as happily.
    renderPage(<SessionExpiredOverlay />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('does not raise an expiry over someone who never signed in', () => {
    // A 401 on a public page must not produce "your session expired". expireSession only marks an
    // expiry when there was a session to lose.
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
    // Phase 4 replaced a hand-rolled overlay that declared role="dialog" aria-modal="true" and trapped
    // nothing, so Tab walked out into a page the reader could no longer save. The replacement refuses
    // Escape, outside pointer-down and outside interaction - and until now that refusal was a claim in
    // a comment. The way out is the sign-out button, which the test below covers.
    signedIn()
    useAuthStore.setState({ expired: true })
    renderPage(<SessionExpiredOverlay />)

    expect(await screen.findByRole('dialog')).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(useAuthStore.getState().expired).toBe(true)
  })

  it('makes the page behind it inert, so an outside click cannot even be delivered', async () => {
    // Not the same claim as "the outside-click handler is prevented", and a stronger one. Radix's modal
    // mode sets pointer-events: none on the body and marks the background aria-hidden, so the page below
    // is unreachable by pointer AND by assistive technology - which is what the hand-rolled overlay this
    // replaced never did, while declaring aria-modal="true" and looking identical.
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
    // A token whose payload decodes: the store reads claims out of it.
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

    // Signing out discards the remembered address as well: the next person at this browser is not
    // the last one.
    expect(useAuthStore.getState().expired).toBe(false)
    expect(useAuthStore.getState().lastEmail).toBeNull()
  })
})
