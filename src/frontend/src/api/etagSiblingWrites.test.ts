// If-Match across writes to different children of one aggregate, against a fake server that keeps a real version counter.
//
// THE DEFECT. A supplier added a Billing address, then a branch, then a Head Office address, and the third save was
// refused with 412 and could not be retried. The API log for that session read GET /suppliers/me 200, POST addresses 200,
// POST branches 200, POST addresses 412, then the recovery re-read GET /suppliers/me/addresses 405. The first case below
// replays that sequence and failed against the transport as it was, with those same five statuses. The transport filed each
// write's new version under the write path as well as under the aggregate, the branch write moved the aggregate on without
// touching the addresses copy, and the next address write preferred that more specific, stale entry.
//
// THE SERVER here does what the API does and nothing more: every mutation must assert the current version or it is refused
// with 412, a successful one increments it, and only the aggregate itself answers a GET - a child path answers 405, as
// /suppliers/me/addresses does. A refused child write carries NO version: the API detects it as a concurrency exception at
// save and ApiPipeline clears the response before writing the problem, so no ETag survives. Only a whole-profile update is
// refused through StaleVersionResult, which does set one. The server defaults to the first, because that is what the
// reported save hit, and one case switches to the second. Versions are compared on the part before the first dot, the way
// ETag.TryParse compares them, so a test cannot pass by matching a discriminator the server ignores.
//
// THE RECOVERY CASES are the promise made to the user: when a save is refused because the record really did move, pressing
// Save again works - whether the refusal carried the current version or not. The last covers a tab still holding an entry
// from before this fix, under a path with no GET - the state that left a tab stuck - and asserts the next attempt falls
// back to the aggregate and succeeds.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from './auth'
import { clearETags, rememberETag } from './etags'

const ME = '/api/v1/suppliers/me'
const ADDRESSES = '/api/v1/suppliers/me/addresses'
const BRANCHES = '/api/v1/suppliers/me/branches'

describe('If-Match across writes to different children of one aggregate', () => {
  let version: number
  let versionOn412: boolean
  let calls: { method: string; path: string; status: number }[]

  const tag = (v: number) => `"${btoa(String.fromCharCode(0, 0, 0, v)).replace(/=+$/, '')}.build.x"`
  const versionOf = (header: string | null) => (header ? header.replace(/"/g, '').split('.')[0] : null)
  const statuses = () => calls.map((c) => `${c.method} ${c.path} ${c.status}`)

  beforeEach(() => {
    clearETags()
    version = 1
    versionOn412 = false
    calls = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      const path = new URL(String(url)).pathname
      const ifMatch = new Headers(init?.headers).get('If-Match')
      let response: Response
      if (method === 'GET') {
        response = path === ME
          ? new Response('{}', { status: 200, headers: { ETag: tag(version) } })
          : new Response(null, { status: 405 })
      } else if (versionOf(ifMatch) !== versionOf(tag(version))) {
        response = new Response('{}', { status: 412, headers: versionOn412 ? { ETag: tag(version) } : {} })
      } else {
        version += 1
        response = new Response('{}', { status: 200, headers: { ETag: tag(version) } })
      }
      calls.push({ method, path, status: response.status })
      return response
    }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('adds an address, then a branch, then another address, without a 412', async () => {
    await apiFetch(ME)
    await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })
    await apiFetch(BRANCHES, { method: 'POST', body: '{}' })
    await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })

    expect(statuses()).toEqual([
      `GET ${ME} 200`,
      `POST ${ADDRESSES} 200`,
      `POST ${BRANCHES} 200`,
      `POST ${ADDRESSES} 200`,
    ])
  })

  it('refuses a save when the record really moved, and lets the next attempt through', async () => {
    await apiFetch(ME)
    version += 1

    const refused = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })
    const retried = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })

    expect(refused.status).toBe(412)
    expect(retried.status, statuses().join('\n')).toBe(200)
  })

  it('lets the next attempt through when the refusal did carry the current version', async () => {
    await apiFetch(ME)
    version += 1
    versionOn412 = true

    const refused = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })
    const retried = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })

    expect(refused.status).toBe(412)
    expect(retried.status, statuses().join('\n')).toBe(200)
  })

  it('unsticks a tab whose entry has no GET and whose refusal carried no version', async () => {
    await apiFetch(ME)
    rememberETag(ADDRESSES, tag(version - 1))

    const refused = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })
    const retried = await apiFetch(ADDRESSES, { method: 'POST', body: '{}' })

    expect(refused.status).toBe(412)
    expect(statuses()).toContain(`GET ${ADDRESSES} 405`)
    expect(retried.status, statuses().join('\n')).toBe(200)
  })
})
