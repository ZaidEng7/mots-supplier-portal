import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { RegisterPage } = await import('./RegisterPage')

/** FR-REG-002/T-060. The server refuses a closed registration either way; this is the message. */
describe('RegisterPage registration mode', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('replaces the form with an explanation when registration is closed', async () => {
    restore = mockFetch({
      '/api/v1/reference/settings': { 'registration.mode': 'closed', 'proposals.defaultCurrencyCode': 'SYP' },
    })

    renderPage(<RegisterPage />)

    expect(await screen.findByText('Registration is closed')).toBeInTheDocument()
    expect(screen.getByText(/Contact the Ministry/)).toBeInTheDocument()
    // A form that cannot be submitted must not be on screen.
    expect(screen.queryByLabelText(/Email/i)).not.toBeInTheDocument()
  })

  it('renders the form when registration is open', async () => {
    // The control, and the requirement's own default.
    restore = mockFetch({
      '/api/v1/reference/settings': { 'registration.mode': 'open', 'proposals.defaultCurrencyCode': 'SYP' },
    })

    renderPage(<RegisterPage />)

    expect(await screen.findByLabelText(/Email/i)).toBeInTheDocument()
    expect(screen.queryByText('Registration is closed')).not.toBeInTheDocument()
  })

  it('renders the form when the settings read FAILS', async () => {
    // Deliberate: the setting defaults to open, and a settings endpoint that is briefly unavailable
    // must not look like a closed ministry. The failure mode that silences the front door is worse
    // than the one that shows a form the server would refuse.
    restore = mockFetch({ '/api/v1/reference/settings': { __status: 500 } })

    renderPage(<RegisterPage />)

    expect(await screen.findByLabelText(/Email/i)).toBeInTheDocument()
  })
})
