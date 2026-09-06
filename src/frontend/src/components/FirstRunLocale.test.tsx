import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'

const { FirstRunLocale } = await import('./FirstRunLocale')
const { useAuthStore } = await import('../lib/authStore')

describe('FirstRunLocale', () => {
  let restore: (() => void) | undefined
  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle', expired: false, lastEmail: null })
  })

  function signedIn() {
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'evaluator@example.test', permissions: [] },
    })
  }

  const account = (languageChosen: boolean) => ({
    '/api/v1/auth/me': { fullName: 'Nadia Karam', email: 'evaluator@example.test', language: 'ar', languageChosen },
  })

  it('asks nobody who has already chosen', async () => {
    // The control. Without it, "the dialog appears" below would be satisfied by a dialog that always
    // appears — which is the one behaviour this screen must not have.
    signedIn()
    restore = mockFetch(account(true))

    renderPage(<FirstRunLocale />)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('asks nobody who is not signed in', async () => {
    restore = mockFetch(account(false))
    renderPage(<FirstRunLocale />)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('asks in both languages, because the reader has not said which they read', async () => {
    signedIn()
    restore = mockFetch(account(false))

    renderPage(<FirstRunLocale />)

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('Choose your language')).toBeInTheDocument()
    expect(screen.getByText('اختر لغة الواجهة')).toBeInTheDocument()
  })

  it('records the choice and closes on the server\'s answer', async () => {
    signedIn()
    // The read flips to chosen only AFTER the POST, so this drives the real sequence: the dialog is up,
    // the user picks, the mutation invalidates, the refetch comes back chosen, and the dialog goes. A
    // fixture that answered "chosen" from the start would prove nothing about the click.
    let chosen = false
    const answered = { fullName: 'Nadia Karam', email: 'evaluator@example.test', language: 'en', languageChosen: true }
    const originalFetch = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.includes('/api/v1/auth/me/language')) {
        chosen = true
        return new Response(JSON.stringify(answered), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      const body = chosen
        ? answered
        : { fullName: 'Nadia Karam', email: 'evaluator@example.test', language: 'ar', languageChosen: false }
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }) as typeof globalThis.fetch
    restore = () => {
      globalThis.fetch = originalFetch
    }

    renderPage(<FirstRunLocale />)

    await screen.findByRole('dialog')
    await userEvent.click(screen.getByRole('button', { name: 'English' }))

    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})
