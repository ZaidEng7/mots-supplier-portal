// T-084. The whole unauthenticated auth surface had no component tests, and this page is the one someone reaches when
// they are already locked out - the worst place for a silent failure.
//
// The confirmation says nothing about whether the account exists, and the wording is the point: "if that account exists"
// is what stops this page being a user-enumeration oracle, and a test that only checked for a success panel would let
// someone "improve" it into one.
//
// It says so when the request could not be sent, which is the defect this closes: the page awaited the call and set
// `sent` after it, so a rejection left the form sitting there as though the click had never registered - and someone
// locked out clicks again. It must NOT claim success at the same time.
//
// An address that is not one is refused without asking the server, and the control that makes that mean something is
// that the server was never called: without it the test would pass on a page that submitted anything and showed an error
// later.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

const { ForgotPasswordPage } = await import('./ForgotPasswordPage')

describe('ForgotPasswordPage', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    vi.unstubAllGlobals()
  })

  it('confirms without saying whether the account exists', async () => {
    restore = mockFetch({ '/api/v1/auth/forgot-password': {} })

    renderPage(<ForgotPasswordPage />)
    await userEvent.type(screen.getByLabelText(/Email/), 'someone@example.test')
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(await screen.findByRole('status')).toHaveTextContent(/if that account exists/i)
  })

  it('says so when the request could not be sent', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('network down') }))

    renderPage(<ForgotPasswordPage />)
    await userEvent.type(screen.getByLabelText(/Email/), 'someone@example.test')
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be sent/i)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('refuses an address that is not one, without asking the server', async () => {
    const fetchSpy = vi.fn(async () => new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchSpy)

    renderPage(<ForgotPasswordPage />)
    await userEvent.type(screen.getByLabelText(/Email/), 'not-an-address')
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(fetchSpy).not.toHaveBeenCalled()
  })
})
