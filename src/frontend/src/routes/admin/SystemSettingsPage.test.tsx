import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { SystemSettingsPage } = await import('./SystemSettingsPage')

const WINDOW = {
  key: 'documents.expiringSoonWindowDays',
  kind: 'Integer',
  value: '30',
  defaultValue: '30',
  isOverridden: false,
  updatedAt: null,
  allowedValues: null,
  minimum: 1,
  maximum: 365,
}

const MODE = {
  key: 'registration.mode',
  kind: 'Choice',
  value: 'open',
  defaultValue: 'open',
  isOverridden: false,
  updatedAt: null,
  allowedValues: ['open', 'closed'],
  minimum: null,
  maximum: null,
}

/** SCR-724. Every one of these settings used to be a const, a seed row or an appsettings key. */
describe('SystemSettingsPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('says whether a setting is overridden or still running on its default', async () => {
    // The distinction the screen exists for: a plain value column cannot tell "nobody decided" from
    // "an administrator chose 30".
    restore = mockFetch({
      '/api/v1/admin/settings': [
        WINDOW,
        { ...MODE, value: 'closed', isOverridden: true, updatedAt: '2026-09-01T10:00:00Z' },
      ],
    })

    renderPage(<SystemSettingsPage />)

    expect(await screen.findByText('Expiring-soon window (days)')).toBeInTheDocument()
    expect(screen.getByText('Using the default (30)')).toBeInTheDocument()
    expect(screen.getByText(/^Changed /)).toBeInTheDocument()
  })

  it('renders the bounds the server sent rather than a second copy of them', async () => {
    restore = mockFetch({ '/api/v1/admin/settings': [WINDOW] })

    renderPage(<SystemSettingsPage />)

    expect(await screen.findByText('Between 1 and 365')).toBeInTheDocument()
  })

  it('saves an edited value and clears the draft', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/admin/settings': [WINDOW],
      '/api/v1/admin/settings/documents.expiringSoonWindowDays': {
        ...WINDOW, value: '45', isOverridden: true, updatedAt: '2026-09-05T09:00:00Z',
      },
    }, calls)

    renderPage(<SystemSettingsPage />)

    const input = await screen.findByLabelText('Value')
    await userEvent.clear(input)
    await userEvent.type(input, '45')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.getByText(/^Changed /)).toBeInTheDocument())
    expect(calls.some((c) => c.url.includes('documents.expiringSoonWindowDays') && c.body.includes('45'))).toBe(true)
  })

  it('names the rule that was broken instead of saying invalid', async () => {
    restore = mockFetch({
      '/api/v1/admin/settings': [WINDOW],
      '/api/v1/admin/settings/documents.expiringSoonWindowDays': {
        __status: 422, error: 'invalid_setting_value', reason: 'value_out_of_range',
      },
    })

    renderPage(<SystemSettingsPage />)

    const input = await screen.findByLabelText('Value')
    await userEvent.clear(input)
    await userEvent.type(input, '400')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('That value is outside the allowed range')).toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the read fails', async () => {
    restore = mockFetch({ '/api/v1/admin/settings': { __status: 500 } })

    renderPage(<SystemSettingsPage />)

    expect(await screen.findByText('Could not load the settings')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
