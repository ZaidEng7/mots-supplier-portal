// T-030 split (2), and the same shape three aggregates later. The store's prefix walk is unit-tested in
// etags.test.ts; what is asserted here is the behaviour a user actually meets - two child writes in a row against
// the same aggregate - because the defect this closes lived in the seam between the store and the transport, and
// neither half's own tests could see it. The fake server answers every request with a version that advances, the
// way a guarded aggregate does.
//
// CONSECUTIVE CHILD WRITES. The second write sends the version the previous one produced rather than nothing: read
// the aggregate for the first precondition, add an item, which asserts v1 and gets v2 back, then add a requirement
// - a DIFFERENT child collection of the same aggregate. Before split (2) that was null, because the fresh version
// had been filed under /items and the walk from /requirements found the aggregate's own entry deleted, so the
// server answers 428.
//
// A refused write leaves the tab able to try again. 412 means the row moved since this tab read it - another tab of
// the same product, another person, or a background job - and the write is correctly refused and NOT replayed,
// because replaying it would overwrite whatever moved the row. But the tab used to be left holding the dead
// version, so every later save on that screen was refused too and only a page reload helped. Reported as "I switch
// away, come back, and cannot edit anything until I refresh". The recovery is a re-read of the aggregate after the
// refusal, and the next attempt carries the version that read returned rather than the dead one - asserted on the
// last WRITE, because the recovery read is itself a call and it lands after the write that triggered it.
//
// The control: one aggregate's version is still never sent for another. The fix files the fresh version one level
// up, and the thing that must not happen is it landing at the collection, where RFQ-1's version would become
// RFQ-2's precondition.
//
// And a stale version is not sent after a write that returned none. A guarded route with no WithFreshETag returns
// no ETag, and the version it asserted is spent, so the next write must go without one - a 428 the transport logs -
// rather than replay a version the row no longer has, which would be a 412 on the user's own second edit.
//
// THE PROPOSAL WORKSPACE reads at one path and writes at another, so the store's prefix walk, which climbs a path
// and never sideways, could not reach the version from a write. Every guarded write in that workspace answered 428
// on its first attempt; it was reproduced in the browser before this test was written, and the fix is getProposal
// filing the read's ETag under the proposal path as well as its own. The If-Match now travels on a write whose path
// shares no prefix with the read - null before the fix, because the version sat under the rfq-scoped path and the
// walk from /api/v1/proposals/... had nothing to find. The control is that no version is invented for a proposal
// the client never read: without it the assertion above would pass just as happily if the transport attached some
// version to every write regardless of what was read.
//
// THE REVIEWER AND THE DOCUMENT WRITE are the same shape one aggregate over: the reviewer reads an application at
// /review/{code} and approves its documents at /suppliers/{code}/documents/{doc}/approve. Found by a reviewer
// pressing Approve on a document and being told "this resource requires the ETag of the version you are editing".
// Both document transitions declare RequireIfMatch, the read does issue an ETag, and the store walks a path upwards
// and never sideways - so the version sat under /api/v1/review/... where a write under /api/v1/suppliers/... could
// not reach it. Every document approval and rejection answered 428, on the one screen where a reviewer decides on
// documents. Null before the fix, with the same no-invention control beside it.
//
// OFFERINGS are the fourth appearance of the same shape in one afternoon. Found by a supplier pressing Deactivate
// on their own offering: 428, "this resource requires the ETag of the version you are editing". The catalogue lists
// offerings through a GET that issues no ETag, and both writes on that row - edit and deactivate - declare
// RequireIfMatch. The one route that does issue an offering's version is the single-item GET, and nothing called
// it, so the deactivate now reads the offering for its version first. Null before the fix, because the list read
// carries no ETag and the walk found nothing.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from './auth'
import { clearETags } from './etags'
import { getProposal, patchProposal } from './proposals'
import { getReviewerSupplierView } from './review'
import { approveDocument } from './documents'
import { deactivateOffering } from './offerings'

describe('If-Match across consecutive child writes', () => {
  let calls: { url: string; ifMatch: string | null }[]

  beforeEach(() => {
    clearETags()
    calls = []
  })
  afterEach(() => vi.unstubAllGlobals())

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

    await apiFetch('/api/v1/rfqs/RFQ-2026-000001')
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/items', { method: 'POST', body: '{}' })
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/requirements', { method: 'POST', body: '{}' })

    expect(calls[1].ifMatch).toBe('"AAAAAQ"')
    expect(calls[2].ifMatch).toBe('"AAAAAg"')
  })

  it('re-reads the resource after a 412 so the next attempt has a live version', async () => {
    const versions = ['"AAAAAQ"', '"AAAAAw"']
    let next = 0
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      const method = init?.method ?? 'GET'
      calls.push({ url: String(url), ifMatch: headers.get('If-Match') })
      if (method === 'PATCH') return new Response('{}', { status: 412 })
      return new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: versions[Math.min(next++, versions.length - 1)] },
      })
    }))

    await apiFetch('/api/v1/rfqs/RFQ-1')
    await apiFetch('/api/v1/rfqs/RFQ-1/items', { method: 'PATCH' })

    expect(calls.map((c) => c.url)).toContain('http://localhost:5080/api/v1/rfqs/RFQ-1')
    expect(calls.filter((c) => c.url.endsWith('/rfqs/RFQ-1')).length).toBe(2)

    await apiFetch('/api/v1/rfqs/RFQ-1/items', { method: 'PATCH' })
    const writes = calls.filter((c) => c.url.endsWith('/items'))
    expect(writes[writes.length - 1].ifMatch).toBe('"AAAAAw"')
  })

  it('still refuses to send one aggregate\'s version for another', async () => {
    stubServer(['"AAAAAQ"', '"AAAAAg"'])

    await apiFetch('/api/v1/rfqs/RFQ-2026-000001')
    await apiFetch('/api/v1/rfqs/RFQ-2026-000001/items', { method: 'POST', body: '{}' })
    await apiFetch('/api/v1/rfqs/RFQ-2026-000009/items', { method: 'POST', body: '{}' })

    expect(calls[2].ifMatch).toBeNull()
  })

  it('does not send a stale version after a write that returned none', async () => {
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
    expect(calls[1].ifMatch).toBe('"AAAAAQ"')
  })

  it('does not invent a version for a proposal it never read', async () => {
    stubProposalServer()

    await patchProposal('PRP-2026-000002', { commercialTerms: { warranty: '6 months' } })

    expect(calls[0].ifMatch).toBeNull()
  })
})

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
    expect(calls[1].ifMatch).toBe('"AAAAJQ.a6fe2f2e"')
  })

  it('does not invent a version for a supplier whose application was never opened', async () => {
    stubReviewServer()

    await approveDocument('SUP-2026-000002', 'DOC-2026-000009')

    expect(calls[0].ifMatch).toBeNull()
  })
})

describe('offering writes take their version from the item read', () => {
  let calls: { url: string; method: string; ifMatch: string | null }[]

  beforeEach(() => {
    clearETags()
    calls = []
  })
  afterEach(() => vi.unstubAllGlobals())

  function stubOfferingServer() {
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const headers = new Headers(init?.headers)
      calls.push({ url: String(url), method: init?.method ?? 'GET', ifMatch: headers.get('If-Match') })
      return new Response(JSON.stringify({ id: 'of-1', nameEn: 'Hot school meals' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json', ETag: '"AAAABQ.a6fe2f2e"' },
      })
    }))
  }

  it('reads the offering for its version before deactivating it', async () => {
    stubOfferingServer()

    await deactivateOffering('of-1')

    expect(calls[0].method).toBe('GET')
    expect(calls[1].url).toContain('/offerings/of-1/deactivate')
    expect(calls[1].ifMatch).toBe('"AAAABQ.a6fe2f2e"')
  })
})
