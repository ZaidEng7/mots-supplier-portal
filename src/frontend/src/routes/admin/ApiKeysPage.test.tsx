// The API keys screen.
//
// THE ASSERTION THAT MATTERS MOST IS THAT THE SECRET IS SHOWN WITH ITS WARNING. The server keeps a hash and has
// no route that reads a key back, so an administrator who navigates away without copying it has lost the
// credential. A screen that showed the value without saying so would be technically correct and would cost
// somebody a re-issue, so the sentence is part of the behaviour and is tested as such.
//
// AND THAT IT IS NOT SHOWN AGAIN AFTERWARDS: dismissing the panel removes it, because a secret left on screen
// through the rest of a session is one that ends up in a screenshot or on a projector.
//
// LAST USED IS ASSERTED AS "NEVER" for a key nothing has called, since that is the answer that decides whether
// an unaccounted-for key can be revoked safely, and a blank cell would read as missing data instead.
//
// A REVOKED KEY STAYS IN THE LIST with no revoke button. The audit trail names keys by prefix and needs them to
// keep resolving, so the test asserts the row survives rather than disappearing.
//
// THE FAILED READ OFFERS A RETRY, matching every other admin screen: a blank page tells an administrator
// nothing about whether they have no keys or the request failed - which is why the empty case is asserted
// separately.

import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { ApiKeysPage } = await import('./ApiKeysPage')

const LIVE = {
  id: '0199b0d0-0000-7000-8000-000000000001',
  name: 'Ministry dashboard',
  prefix: 'a1b2c3d4',
  permissions: ['supplier.registry.export'],
  createdAt: '2026-09-20T09:00:00Z',
  expiresAt: '2027-09-20T09:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
}

const SECRET = 'mots_a1b2c3d4_Zm9vYmFyYmF6cXV1eA'

describe('ApiKeysPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('lists a key by its prefix and says it has never been used', async () => {
    restore = mockFetch({ '/api/v1/admin/api-keys': [LIVE] })

    renderPage(<ApiKeysPage />)

    expect(await screen.findByText('Ministry dashboard')).toBeInTheDocument()
    expect(screen.getByText('a1b2c3d4')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByText('Never used')).toBeInTheDocument()
  })

  it('shows a newly issued secret with the warning that it will not be shown again, and hides it on dismiss', async () => {
    restore = mockFetch({
      '/api/v1/admin/api-keys': {
        __byMethod: {
          GET: [],
          POST: { key: LIVE, secret: SECRET },
        },
      },
    })

    renderPage(<ApiKeysPage />)

    await userEvent.type(await screen.findByLabelText('Name'), 'Ministry dashboard')
    await userEvent.click(screen.getByRole('button', { name: 'Issue' }))

    expect(await screen.findByText(SECRET)).toBeInTheDocument()
    expect(screen.getByText(/will not be shown again/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Hide' }))

    expect(screen.queryByText(SECRET)).not.toBeInTheDocument()
  })

  it('says plainly when no key has been issued yet', async () => {
    restore = mockFetch({ '/api/v1/admin/api-keys': [] })

    renderPage(<ApiKeysPage />)

    expect(await screen.findByText('No keys have been issued yet.')).toBeInTheDocument()
  })

  it('offers a retry instead of a blank page when the read fails', async () => {
    restore = mockFetch({ '/api/v1/admin/api-keys': { __status: 500 } })

    renderPage(<ApiKeysPage />)

    expect(await screen.findByText('Could not load the keys')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('keeps a revoked key in the list and offers no way to revoke it again', async () => {
    restore = mockFetch({
      '/api/v1/admin/api-keys': [{ ...LIVE, revokedAt: '2026-09-22T11:00:00Z' }],
    })

    renderPage(<ApiKeysPage />)

    expect(await screen.findByText('Revoked')).toBeInTheDocument()
    expect(screen.getByText('a1b2c3d4')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument()
  })

  it('revokes a key through the route the server exposes', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      '/api/v1/admin/api-keys': [LIVE],
      [`/api/v1/admin/api-keys/${LIVE.id}/revoke`]: { ...LIVE, revokedAt: '2026-09-24T12:00:00Z' },
    }, calls)

    renderPage(<ApiKeysPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Revoke' }))

    await waitFor(() =>
      expect(calls.some((c) => c.url.includes(`/${LIVE.id}/revoke`) && c.method === 'POST')).toBe(true))
  })
})
