// Two requests that meet an expired access token at the same moment share ONE refresh.
//
// Every 401 used to send its own refresh, each carrying the same cookie. The server rotated the token on the
// first and took the second for a replayed stolen token, so two requests fired together - a dashboard loading its
// tiles as the token ran out - ended the session. The fix on this side is a single-flight refresh in auth.ts, and
// what is asserted here is what the user meets: two apiFetch calls answered 401, exactly one POST to the refresh
// endpoint, and BOTH requests retried with the token that one refresh returned.
//
// The refresh answer is held back until both requests have been refused, so the second caller is certain to ask
// for a refresh while the first is still on the wire. Without that hold the fake server could answer the first
// refresh before the second 401 arrived, and the test would pass on a transport that sends one refresh per 401.
//
// The control is a refresh asked for after the shared one has settled: it goes to the server again rather than
// being handed the old answer, so the single flight is per refresh and not once per page.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch, refresh } from './auth'
import { clearETags } from './etags'
import { useAuthStore } from '../lib/authStore'

const REFRESHED = 'header.eyJzdWIiOiJ1MSJ9.refreshed'

describe('a refresh shared by concurrent 401s', () => {
  let calls: { url: string; method: string; authorization: string | null }[]
  let releaseRefresh: () => void

  beforeEach(() => {
    clearETags()
    calls = []
    useAuthStore.getState().setSession('header.eyJzdWIiOiJ1MSJ9.expired')

    const refreshAnswered = new Promise<void>((resolve) => {
      releaseRefresh = resolve
    })

    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      const authorization = new Headers(init?.headers).get('Authorization')
      calls.push({ url: String(url), method, authorization })

      if (String(url).endsWith('/api/v1/auth/refresh')) {
        await refreshAnswered
        return new Response(JSON.stringify({ accessToken: REFRESHED, accessTokenExpiresAt: '2030-01-01T00:00:00Z' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      return authorization === `Bearer ${REFRESHED}`
        ? new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response('{}', { status: 401 })
    }))
  })

  afterEach(() => {
    releaseRefresh()
    vi.unstubAllGlobals()
    useAuthStore.getState().clearSession()
  })

  const refreshPosts = () => calls.filter((c) => c.url.endsWith('/api/v1/auth/refresh') && c.method === 'POST')
  const refusals = () => calls.filter((c) => !c.url.endsWith('/api/v1/auth/refresh') && c.authorization !== `Bearer ${REFRESHED}`)

  it('sends one refresh for two concurrent 401s and retries both with its token', async () => {
    const first = apiFetch('/api/v1/suppliers/me')
    const second = apiFetch('/api/v1/notifications')

    await vi.waitFor(() => expect(refusals()).toHaveLength(2))
    await new Promise((resolve) => setTimeout(resolve, 0))
    releaseRefresh()

    const [a, b] = await Promise.all([first, second])

    expect(refreshPosts()).toHaveLength(1)
    expect(a.status).toBe(200)
    expect(b.status).toBe(200)

    const retries = calls.filter((c) => c.authorization === `Bearer ${REFRESHED}`).map((c) => c.url)
    expect(retries).toHaveLength(2)
    expect(retries).toEqual(expect.arrayContaining([
      expect.stringMatching(/\/api\/v1\/suppliers\/me$/),
      expect.stringMatching(/\/api\/v1\/notifications$/),
    ]))
    expect(useAuthStore.getState().accessToken).toBe(REFRESHED)
    expect(useAuthStore.getState().expired).toBe(false)
  })

  it('sends a new refresh once the shared one has settled', async () => {
    releaseRefresh()

    await refresh()
    await refresh()

    expect(refreshPosts()).toHaveLength(2)
  })
})
