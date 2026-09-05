import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from './auth'
import { clearETags } from './etags'

/**
 * T-030 split (2). The store's prefix walk is unit-tested in `etags.test.ts`; what is asserted here is
 * the behaviour a user actually meets — two child writes in a row against the same aggregate — because
 * the defect this closes lived in the seam between the store and the transport, and neither half's own
 * tests could see it.
 */
describe('If-Match across consecutive child writes', () => {
  let calls: { url: string; ifMatch: string | null }[]

  beforeEach(() => {
    clearETags()
    calls = []
  })
  afterEach(() => vi.unstubAllGlobals())

  /** Answers every request with a version that advances, the way a guarded aggregate does. */
  function stubServer(versions: string[]) {
    let next = 0
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      calls.push({ url: String(url), ifMatch: headers.get('If-Match') })
      return new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: versions[Math.min(next++, versions.length - 1)] },
      })
    }))
  }

  it('sends the version the previous child write produced, not nothing', async () => {
    stubServer(['"AAAAAQ"', '"AAAAAg"', '"AAAAAw"'])

    // Read the aggregate: this is where the first precondition comes from.
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001')
    // Add an item — asserts v1, and the response carries v2.
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/items', { method: 'POST', body: '{}' })
    // Add a requirement. A DIFFERENT child collection of the same aggregate.
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/requirements', { method: 'POST', body: '{}' })

    expect(calls[1].ifMatch).toBe('"AAAAAQ"')
    // Before split (2) this was null: the fresh version had been filed under `/items`, and the walk
    // from `/requirements` found the aggregate's own entry deleted. The server answers 428.
    expect(calls[2].ifMatch).toBe('"AAAAAg"')
  })

  it('still refuses to send one aggregate\'s version for another', async () => {
    // The control. The fix files the fresh version one level up, and the thing that must not happen is
    // it landing at the collection — where RFQ-1's version would become RFQ-2's precondition.
    stubServer(['"AAAAAQ"', '"AAAAAg"'])

    await apiFetch('/api/v1/rfqs/RFQ-2026-000001')
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/items', { method: 'POST', body: '{}' })
    await apiFetch('/api/v1/rfqs/RFQ-2026-000009/items', { method: 'POST', body: '{}' })

    expect(calls[2].ifMatch).toBeNull()
  })

  it('does not send a stale version after a write that returned none', async () => {
    // A guarded route with no WithFreshETag returns no ETag, and the version it asserted is spent. The
    // next write must go without one — a 428 the transport logs — rather than replay a version the row
    // no longer has, which would be a 412 on the user's own second edit.
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      calls.push({ url: String(url), ifMatch: headers.get('If-Match') })
      const isRead = (init?.method ?? 'GET') === 'GET'
      return new Response('{}', {
        status: 200,
        headers: isRead
          ? { 'Content-Type': 'application/json', ETag: '"AAAAAQ"' }
          : { 'Content-Type': 'application/json' },
      })
    }))

    await apiFetch('/api/v1/rfqs/RFQ-2026-000001')
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/items', { method: 'POST', body: '{}' })
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/requirements', { method: 'POST', body: '{}' })

    expect(calls[1].ifMatch).toBe('"AAAAAQ"')
    expect(calls[2].ifMatch).toBeNull()
  })
})
