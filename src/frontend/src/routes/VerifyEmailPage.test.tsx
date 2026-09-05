import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage } from '../test/renderPage'

let searchToken: string | undefined = 'a-token'
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a', useSearch: () => ({ token: searchToken }) }
})

const { VerifyEmailPage } = await import('./VerifyEmailPage')

/**
 * B-1. The fourth screen an email link lands on, and the only one that acts on ARRIVAL rather than on a
 * submit - so a failure here is silent unless the page says something. Nothing tested it.
 */
describe('VerifyEmailPage', () => {
  const original = globalThis.fetch
  afterEach(() => { globalThis.fetch = original; searchToken = 'a-token' })

  it('verifies on arrival and says so', async () => {
    const calls: string[] = []
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      calls.push(url)
      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })
    }) as typeof fetch

    renderPage(<VerifyEmailPage />)

    expect(await screen.findByText('Your email has been verified')).toBeInTheDocument()
    expect(calls.some((u) => u.includes('/api/v1/auth/verify-email'))).toBe(true)
  })

  it('says the link is invalid when the server refuses it, and offers a new one', async () => {
    // An expired or already-used token. The important half is the offer: a dead end here means the user
    // has no way to get a working link, and the account cannot leave Draft without one.
    globalThis.fetch = (async () =>
      new Response('{}', { status: 400, headers: { 'Content-Type': 'application/json' } })) as typeof fetch

    renderPage(<VerifyEmailPage />)

    expect(await screen.findByText(/Could not verify this email/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Resend verification link' })).toBeInTheDocument()
  })

  it('does not post at all when the link carries no token', async () => {
    searchToken = undefined
    let posted = false
    globalThis.fetch = (async () => { posted = true; return new Response('{}', { status: 200 }) }) as typeof fetch

    renderPage(<VerifyEmailPage />)

    expect(await screen.findByText(/Could not verify this email/)).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('answers a resend the same way whether or not the account exists', async () => {
    // Enumeration-safe, the same reasoning as the registration and forgot-password responses: the
    // confirmation must not tell a stranger whether an address is registered.
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      // The verification itself fails, so the resend control is on screen.
      return new Response('{}', { status: url.includes('verify-email') ? 400 : 200 })
    }) as typeof fetch

    renderPage(<VerifyEmailPage />)

    await userEvent.type(await screen.findByLabelText(/Email/), 'someone@example.com')
    await userEvent.click(screen.getByRole('button', { name: 'Resend verification link' }))

    expect(await screen.findByText(/a new verification link has been sent/)).toBeInTheDocument()
  })
})
