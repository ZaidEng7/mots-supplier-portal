import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from './auth'
import { clearETags } from './etags'
import { getProposal, patchProposal } from './proposals'
import { getReviewerSupplierView } from './review'
import { approveDocument } from './documents'

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

/**
 * The supplier's proposal workspace reads at one path and writes at another, so the store's prefix
 * walk — which climbs a path and never sideways — could not reach the version from a write. Every
 * guarded write in that workspace answered 428 on its first attempt; reproduced in the browser before
 * this test was written, and the fix is `getProposal` filing the read's ETag under the proposal path
 * as well as its own.
 */
describe('the proposal read files its version where the writes will look for it', () => {
  let calls: { url: string; ifMatch: string | null }[]

  beforeEach(() => {
    clearETags()
    calls = []
  })
  afterEach(() => vi.unstubAllGlobals())

  function stubProposalServer() {
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      calls.push({ url: String(url), ifMatch: headers.get('If-Match') })
      return new Response(JSON.stringify({ proposalCode: 'PRP-2026-000001', state: 'Draft' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: '"AAAAAQ"' },
      })
    }))
  }

  it('sends If-Match on a write whose path shares no prefix with the read', async () => {
    stubProposalServer()

    await getProposal('RFQ-2026-000001')
    await patchProposal('PRP-2026-000001', { commercialTerms: { warranty: '12 months' } })

    expect(calls[0].url).toContain('/api/v1/rfqs/RFQ-2026-000001/proposals')
    expect(calls[1].url).toContain('/api/v1/proposals/PRP-2026-000001')
    // Null before the fix: the version sat under the rfq-scoped path and the walk from
    // `/api/v1/proposals/...` had nothing to find, so the server answered 428 and nothing saved.
    expect(calls[1].ifMatch).toBe('"AAAAAQ"')
  })

  it('does not invent a version for a proposal it never read', async () => {
    stubProposalServer()

    // The control. Without this the assertion above would pass just as happily if the transport
    // attached some version to every write regardless of what was read.
    await patchProposal('PRP-2026-000002', { commercialTerms: { warranty: '6 months' } })

    expect(calls[0].ifMatch).toBeNull()
  })
})

/**
 * The same shape again, one aggregate over: the reviewer reads an application at `/review/{code}`
 * and approves its documents at `/suppliers/{code}/documents/{doc}/approve`.
 *
 * Found by a reviewer pressing Approve on a document and being told "this resource requires the ETag
 * of the version you are editing". Both document transitions declare RequireIfMatch, the read does
 * issue an ETag, and the store walks a path upwards and never sideways - so the version sat under
 * `/api/v1/review/...` where a write under `/api/v1/suppliers/...` could not reach it. Every document
 * approval and rejection answered 428, on the one screen where a reviewer decides on documents.
 */
describe('the reviewer read and the document write', () => {
  let calls: { url: string; ifMatch: string | null }[]

  beforeEach(() => {
    clearETags()
    calls = []
  })
  afterEach(() => vi.unstubAllGlobals())

  function stubReviewServer() {
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      calls.push({ url: String(url), ifMatch: headers.get('If-Match') })
      return new Response(JSON.stringify({ supplier: {}, documents: [], annotationHistory: [] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: '"AAAAJQ.a6fe2f2e"' },
      })
    }))
  }

  it('sends If-Match when approving a document read through the review view', async () => {
    stubReviewServer()

    await getReviewerSupplierView('SUP-2026-000001')
    await approveDocument('SUP-2026-000001', 'DOC-2026-000001')

    expect(calls[0].url).toContain('/api/v1/review/SUP-2026-000001')
    expect(calls[1].url).toContain('/api/v1/suppliers/SUP-2026-000001/documents/DOC-2026-000001/approve')
    // Null before the fix.
    expect(calls[1].ifMatch).toBe('"AAAAJQ.a6fe2f2e"')
  })

  it('does not invent a version for a supplier whose application was never opened', async () => {
    stubReviewServer()

    await approveDocument('SUP-2026-000002', 'DOC-2026-000009')

    expect(calls[0].ifMatch).toBeNull()
  })
})
