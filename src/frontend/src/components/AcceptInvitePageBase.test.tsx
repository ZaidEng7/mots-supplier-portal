// B-1. This one component IS the password-reset page, the staff-invite acceptance page and the team-invite acceptance
// page - three of the fifteen untested components, and three of the four screens an unauthenticated user is sent to by
// an email link. Nothing asserted what an expired token, a used invitation or a too-short password does.
//
// It also corrects a phase-12a finding: ResetPasswordPage was reported as having "no loading, error or validation
// handling of any kind". It is a nineteen-line wrapper, and the handling is all here.
//
// The happy path posts the token from the URL and the password the user typed, in that order, which is what all three
// endpoints expect, and the form is then gone - a second submission would consume a token that is now spent.
//
// A refused token says the link is invalid. An expired token, a used invitation and a token for a deleted account all
// arrive the same way and all three are the same message, because the user cannot act on the difference; the form stays
// up, because a fresh link is the next step and this one may have been mistyped.
//
// A short password is refused in the browser and the endpoint is never called. A missing token is treated as an invalid
// link rather than posted: the link arrives without one when a mail client mangles the URL, and posting an empty token
// would spend a request and answer with the same message anyway.

import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage } from '../test/renderPage'

let searchToken: string | undefined = 'a-token'
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a', useSearch: () => ({ token: searchToken }) }
})

const { AcceptInvitePageBase } = await import('./AcceptInvitePageBase')

describe('AcceptInvitePageBase', () => {
  afterEach(() => { searchToken = 'a-token'; vi.restoreAllMocks() })

  const render = (onSubmitToken: (token: string, password: string) => Promise<unknown>) =>
    renderPage(
      <AcceptInvitePageBase
        onSubmitToken={onSubmitToken}
        title="Set your password"
        successMessage="Your password is set."
        invalidMessage="That link is invalid or has expired."
        submitLabel="Set password"
        passwordFieldLabel="New password"
        mapPasswordError={(raw) => (raw ? 'Password is too short' : undefined)}
        loginLinkLabel="Sign in"
      />,
    )

  it('sets the password and then offers the way in', async () => {
    const submit = vi.fn().mockResolvedValue(undefined)
    render(submit)

    await userEvent.type(await screen.findByLabelText(/New password/), 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByText('Your password is set.')).toBeInTheDocument()
    expect(submit).toHaveBeenCalledWith('a-token', 'a-long-enough-password')
    expect(screen.queryByRole('button', { name: 'Set password' })).not.toBeInTheDocument()
  })

  it('says the link is invalid when the endpoint refuses the token', async () => {
    const submit = vi.fn().mockRejectedValue(new Error('invalid_or_expired_token'))
    render(submit)

    await userEvent.type(await screen.findByLabelText(/New password/), 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('That link is invalid or has expired.')
    expect(screen.getByRole('button', { name: 'Set password' })).toBeInTheDocument()
  })

  it('refuses a short password in the browser and never calls the endpoint', async () => {
    const submit = vi.fn()
    render(submit)

    await userEvent.type(await screen.findByLabelText(/New password/), 'short')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByText('Password is too short')).toBeInTheDocument()
    expect(submit).not.toHaveBeenCalled()
  })

  it('treats a missing token as an invalid link rather than posting nothing', async () => {
    searchToken = undefined
    const submit = vi.fn()
    render(submit)

    await userEvent.type(await screen.findByLabelText(/New password/), 'a-long-enough-password')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That link is invalid or has expired.')
    expect(submit).not.toHaveBeenCalled()
  })
})
