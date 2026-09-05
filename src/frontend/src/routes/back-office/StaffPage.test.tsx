import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { StaffPage } = await import('./StaffPage')

function account(overrides: Record<string, unknown> = {}) {
  return {
    userId: 'u-1',
    email: 'reviewer@ministry.example',
    fullName: 'A Reviewer',
    role: 'onboarding_reviewer',
    isActive: true,
    mfaEnabled: false,
    lockoutEnd: null,
    activeSessionCount: 0,
    ...overrides,
  }
}

const page = (accounts: Record<string, unknown>[]) => ({
  data: accounts,
  pagination: { hasMore: false, nextCursor: null },
})

/** T-077/SCR-701/702. `system_admin` could invite an account and then never see it again - so one created
 * in error could not be removed, which is the half of this that is a security gap. */
describe('StaffPage accounts', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists accounts with the facts that make a row actionable', async () => {
    restore = mockFetch({
      '/api/v1/staff': page([
        account(),
        account({ userId: 'u-2', email: 'admin@ministry.example', fullName: 'An Admin', role: 'system_admin', mfaEnabled: true, activeSessionCount: 2 }),
      ]),
    })

    renderPage(<StaffPage />)

    expect(await screen.findByText('A Reviewer')).toBeInTheDocument()
    expect(screen.getByText('admin@ministry.example')).toBeInTheDocument()
    // MFA enrolment and live sessions: a deactivation that left sessions alive would only stop the NEXT
    // sign-in, so the count is on the row rather than implied.
    expect(screen.getByText('Two-factor enrolled')).toBeInTheDocument()
    expect(screen.getByText('Active sessions: 2')).toBeInTheDocument()
  })

  it('deactivates an account and reactivates a deactivated one', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/staff': page([account({ isActive: false })]),
      '/api/v1/staff/u-1/reactivate': account(),
    }, calls)

    renderPage(<StaffPage />)

    // A deactivated row offers the opposite action - the control that proves the button is bound to the
    // row's state rather than fixed.
    expect(await screen.findByText('Deactivated')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reactivate' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/reactivate'))).toBe(true))
  })

  it('resets the second factor', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/staff': page([account({ mfaEnabled: true })]),
      '/api/v1/staff/u-1/reset-mfa': account({ mfaEnabled: false }),
    }, calls)

    renderPage(<StaffPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Reset two-factor' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/reset-mfa'))).toBe(true))
  })

  it('says why when the platform refuses to remove its last administrator', async () => {
    restore = mockFetch({
      '/api/v1/staff': page([account({ role: 'system_admin' })]),
      // The wire shape the SERVER sends: ProblemDetailsMiddleware turns the handler's `error` token into
      // §7's SCREAMING_SNAKE code. A fixture carrying the lower-case token would test a response the API
      // never produces.
      '/api/v1/staff/u-1/deactivate': { __status: 422, code: 'WOULD_LOCK_OUT_ADMINISTRATION' },
    })

    renderPage(<StaffPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))

    // The refusal is a thing to understand, not to retry, so it is named rather than reported as a
    // generic failure.
    expect(await screen.findByText('The last active system administrator cannot be deactivated.')).toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the list fails', async () => {
    restore = mockFetch({ '/api/v1/staff': { __status: 500 } })

    renderPage(<StaffPage />)

    expect(await screen.findByText('Could not load the staff accounts')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
