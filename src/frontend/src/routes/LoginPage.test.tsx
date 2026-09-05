import { describe, expect, it, vi, afterEach, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../test/renderPage'

const navigate = vi.fn()
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return {
    ...actual,
    Link: 'a',
    useNavigate: () => navigate,
    useSearch: () => ({}),
  }
})

const { LoginPage } = await import('./LoginPage')
const { useAuthStore } = await import('../lib/authStore')

/**
 * B-1. The front door had NO component test: nothing asserted what a wrong password, a locked account,
 * an unverified email or a bad TOTP code does. The e2e axe sweep proved this page MOUNTS in both
 * locales, which is a different claim - it says nothing about the four states a user actually hits.
 *
 * Each state is asserted through the rendered output, and each of the specific ones is distinguished
 * from the generic failure: a locked account that reads "invalid email or password" sends the user
 * round the loop that locked them out in the first place.
 */
describe('LoginPage failure states', () => {
  let restore: () => void
  beforeEach(() => {
    navigate.mockClear()
    useAuthStore.setState({ accessToken: null, claims: null })
  })
  afterEach(() => restore?.())

  // Regex matchers: the Field component appends a required marker inside the label, so the accessible
  // name is "Email *" rather than "Email".
  const signIn = async (email = 'user@example.com', password = 'a-password') => {
    await userEvent.type(await screen.findByLabelText(/Email/), email)
    await userEvent.type(screen.getByLabelText(/Password/), password)
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  }

  it('reports a wrong password without saying which half was wrong', async () => {
    // MSP-73's reasoning applied to sign-in: the message must not tell an attacker that the EMAIL was
    // right, so one message covers both halves.
    restore = mockFetch({ '/api/v1/auth/login': { __status: 401, code: 'INVALID_CREDENTIALS' } })

    renderPage(<LoginPage />)
    await signIn()

    expect(await screen.findByText('Invalid email or password')).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('says an account is locked rather than repeating "invalid credentials"', async () => {
    // A-14 keeps 423 distinct from 429 precisely so a client can tell them apart, and this is what that
    // distinction is FOR: a locked user told "invalid email or password" tries again, which is the loop
    // that locked them.
    restore = mockFetch({ '/api/v1/auth/login': { __status: 423, code: 'ACCOUNT_LOCKED' } })

    renderPage(<LoginPage />)
    await signIn()

    expect(await screen.findByText('Account is temporarily locked after repeated failed attempts')).toBeInTheDocument()
  })

  it('sends an unverified user to verify their email instead of retrying', async () => {
    restore = mockFetch({ '/api/v1/auth/login': { __status: 400, code: 'EMAIL_NOT_VERIFIED' } })

    renderPage(<LoginPage />)
    await signIn()

    expect(await screen.findByText('Please verify your email first')).toBeInTheDocument()
  })

  it('asks for a TOTP code when the account requires MFA, and keeps the password to re-submit', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    // The REAL wire shape: ProblemDetailsMiddleware conforms every error and emits §7's `code`, with no
    // `error` key. The page used to read `body.error`, so this never matched and every MFA account -
    // including every system_admin - was shown "Invalid email or password" instead of the code step.
    restore = mockFetch({ '/api/v1/auth/login': { __status: 401, code: 'MFA_REQUIRED' } }, calls)

    renderPage(<LoginPage />)
    await signIn('admin@ministry.example', 'the-password')

    // The second step, not an error: `mfa_required` is a 401 and must not read as a failed sign-in.
    expect(await screen.findByText('Two-factor verification')).toBeInTheDocument()
    expect(screen.queryByText('Invalid email or password')).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Authenticator code/), '123456')
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }))

    // The same credentials plus the code - LoginHandler expects all three on the second call, so a page
    // that dropped the password would fail with a message about the code.
    const second = JSON.parse(calls.filter((c) => c.url.includes('/auth/login')).at(-1)!.body)
    expect(second.email).toBe('admin@ministry.example')
    expect(second.password).toBe('the-password')
    expect(second.totpCode).toBe('123456')
  })

  it('reports a bad TOTP code as a bad code, not as a bad password', async () => {
    // Two DIFFERENT responses from one route: the first leg is the challenge, the second is the wrong
    // code. mockFetch answers a route with one body, so this test installs its own two-stage stub -
    // which is also the honest shape, since the real endpoint answers differently on the second call.
    const original = globalThis.fetch
    let leg = 0
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if (url.includes('/api/v1/auth/login')) {
        leg += 1
        return new Response(JSON.stringify({ status: 401, code: leg === 1 ? 'MFA_REQUIRED' : 'MFA_INVALID' }),
          { status: 401, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<LoginPage />)
    await signIn()

    expect(await screen.findByText('Two-factor verification')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText(/Authenticator code/), '000000')
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }))

    // A bad code is a bad code. Reporting it as "invalid email or password" would send the user back to
    // re-type credentials that were already accepted.
    expect(await screen.findByText('Incorrect code, try again')).toBeInTheDocument()
  })

  it('stores the session and routes a supplier to their dashboard on success', async () => {
    // The control for all of the above: the happy path still works, and a supplier and a staff user land
    // in different shells - the claim that decides it is supplierId, which is also what the router's own
    // guard reads.
    restore = mockFetch({
      '/api/v1/auth/login': {
        // A token whose payload carries a supplierId. Signature is irrelevant here: authStore decodes,
        // it does not verify - the server does that.
        accessToken: `header.${btoa(JSON.stringify({ sub: 'u-1', supplierId: 's-1', perms: [] }))}.sig`,
      },
    })

    renderPage(<LoginPage />)
    await signIn()

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith({ to: '/dashboard' }))
    expect(useAuthStore.getState().accessToken).not.toBeNull()
  })
})
