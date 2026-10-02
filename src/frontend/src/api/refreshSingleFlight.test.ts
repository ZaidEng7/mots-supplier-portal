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
//
// A SHARED REFRESH THAT FAILS fails for both callers at once: one POST, both requests answered 401, the session
// marked expired. Both callers report the expiry, and the second report used to switch the expired flag back off,
// which is what this case first caught. The slot is emptied by a failure as well as by a success, so the next
// refresh asked for is a new POST rather than the stored failure handed out again.
//
// A SUPERSEDED REFUSAL is the server saying another tab rotated this cookie a moment ago. That one answer, and no
// other, is retried once, and the retry happens inside the shared slot, so two callers waiting on it still cost
// exactly two POSTs between them. A second superseded answer ends the session after those two POSTs; a plain 401 is
// never retried. The answers are written in the shape the server's error middleware sends them, with the code
// upper-cased and no `error` key.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch, refresh } from './auth'
import { clearETags } from './etags'
import { useAuthStore } from '../lib/authStore'

const REFRESHED = 'header.eyJzdWIiOiJ1MSJ9.refreshed'

const refreshed = () =>
  new Response(JSON.stringify({ accessToken: REFRESHED, accessTokenExpiresAt: '2030-01-01T00:00:00Z' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })

const refusedWith = (code: string) => () =>
  new Response(JSON.stringify({ status: 401, code }), {
    status: 401,
    headers: { 'Content-Type': 'application/problem+json' },
  })

const superseded = refusedWith('REFRESH_SUPERSEDED')
const tokenInvalid = refusedWith('TOKEN_INVALID')

describe('a refresh shared by concurrent 401s', () => {
  let calls: { url: string; method: string; authorization: string | null }[]
  let releaseRefresh: () => void
  let refreshAnswers: (() => Response)[]

  beforeEach(() => {
    clearETags()
    calls = []
    refreshAnswers = []
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
        return (refreshAnswers.shift() ?? refreshed)()
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

  async function twoRequestsMeetingAnExpiredToken() {
    const first = apiFetch('/api/v1/suppliers/me')
    const second = apiFetch('/api/v1/notifications')

    await vi.waitFor(() => expect(refusals()).toHaveLength(2))
    await new Promise((resolve) => setTimeout(resolve, 0))
    releaseRefresh()

    return Promise.all([first, second])
  }

  it('sends one refresh for two concurrent 401s and retries both with its token', async () => {
    const [a, b] = await twoRequestsMeetingAnExpiredToken()

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

  it('fails both callers with one refused refresh, then sends a new one when asked again', async () => {
    refreshAnswers = [tokenInvalid]

    const [a, b] = await twoRequestsMeetingAnExpiredToken()

    expect(refreshPosts()).toHaveLength(1)
    expect(a.status).toBe(401)
    expect(b.status).toBe(401)
    expect(useAuthStore.getState().expired).toBe(true)
    expect(useAuthStore.getState().accessToken).toBeNull()

    await refresh()

    expect(refreshPosts()).toHaveLength(2)
  })

  it('retries a superseded refresh once, inside the shared slot, and keeps the session', async () => {
    refreshAnswers = [superseded, refreshed]

    const [a, b] = await twoRequestsMeetingAnExpiredToken()

    expect(refreshPosts()).toHaveLength(2)
    expect(a.status).toBe(200)
    expect(b.status).toBe(200)
    expect(useAuthStore.getState().accessToken).toBe(REFRESHED)
    expect(useAuthStore.getState().expired).toBe(false)
  })

  it('expires the session after exactly two refreshes when both are superseded', async () => {
    refreshAnswers = [superseded, superseded, refreshed]
    releaseRefresh()

    const res = await apiFetch('/api/v1/suppliers/me')

    expect(refreshPosts()).toHaveLength(2)
    expect(res.status).toBe(401)
    expect(useAuthStore.getState().expired).toBe(true)
  })

  it('never retries a refresh refused for any other reason', async () => {
    refreshAnswers = [tokenInvalid, refreshed]
    releaseRefresh()

    const res = await apiFetch('/api/v1/suppliers/me')

    expect(refreshPosts()).toHaveLength(1)
    expect(res.status).toBe(401)
    expect(useAuthStore.getState().expired).toBe(true)
  })
})
