import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearETags, lookupETag } from './etags'
import { getOwnSupplier, updateProfile } from './supplier'

/**
 * The supplier profile's own concurrency wiring, which was broken in two independent ways at once and
 * is worth pinning as two separate assertions.
 *
 * <p><b>What was wrong.</b> `api/supplier.ts` hand-built `If-Match: "4"` from the numeric `rowVersion`
 * in the body. That matched until batch 11 changed the ETag to carry the build alongside the row —
 * the real header is now `"AAAABA.1bdf128d"` — so the guess could never match again and every write
 * answered 412. It also OVERRODE the correct value, because `apiFetch` attaches the stored ETag and an
 * explicit `If-Match` wins. Removing it revealed the second fault underneath: the read is at
 * `/suppliers/me` and the write is at `/suppliers/{code}`, and the store walks a path upward but never
 * sideways, so the write then found nothing and answered 428.</p>
 *
 * <p>Both were found by walking onboarding as a supplier: choose a currency, press Save, reload, and
 * watch it come back empty.</p>
 */
describe('supplier profile ETag wiring', () => {
  const original = globalThis.fetch

  beforeEach(() => clearETags())
  afterEach(() => { globalThis.fetch = original; vi.restoreAllMocks() })

  function respondWith(etag: string | null) {
    globalThis.fetch = vi.fn(async () => new Response(
      JSON.stringify({ supplierCode: 'SUP-2026-000001', rowVersion: 4 }),
      { status: 200, headers: etag ? { ETag: etag, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' } },
    )) as unknown as typeof fetch
  }

  it('files the read ETag under the path the WRITE uses', async () => {
    // `me` and the supplier code name one resource, and only this function knows that: the code is
    // not known to the caller until the body arrives.
    respondWith('"AAAABA.1bdf128d"')

    await getOwnSupplier()

    expect(lookupETag('/api/v1/suppliers/SUP-2026-000001')).toBe('"AAAABA.1bdf128d"')
  })

  /**
   * The version a WRITE produced has to reach the `me` spelling too, because that is the one every
   * child collection walks up to.
   *
   * <p>The walkthrough found this the hard way: save the company details, add an address on the next
   * step, and the save was refused as a concurrency conflict. `updateProfile` PATCHes
   * `/suppliers/{code}` and filed its fresh version there; `/suppliers/me/addresses` and
   * `/suppliers/me/branches` reach `/suppliers/me`, which still held the version from the page's own
   * read. One aggregate, two keys, refreshed one at a time.</p>
   */
  it('files a WRITE ETag under the me path as well, which the child collections use', async () => {
    respondWith('"AAAABw.1bdf128d.abc12345"')

    await updateProfile('SUP-2026-000001', { description: 'anything' })

    expect(lookupETag('/api/v1/suppliers/SUP-2026-000001')).toBe('"AAAABw.1bdf128d.abc12345"')
    expect(lookupETag('/api/v1/suppliers/me')).toBe('"AAAABw.1bdf128d.abc12345"')
    // And the precondition a child write would actually send resolves to the fresh one.
    expect(lookupETag('/api/v1/suppliers/me/branches')).toBe('"AAAABw.1bdf128d.abc12345"')
  })

  it('stores the server ETag verbatim, never one rebuilt from rowVersion', async () => {
    // The regression this file exists for. A tag rebuilt as `"4"` is the shape that used to be sent,
    // and it cannot match a server tag that carries the build.
    respondWith('"AAAABA.1bdf128d"')

    await getOwnSupplier()

    const stored = lookupETag('/api/v1/suppliers/SUP-2026-000001')
    expect(stored).not.toBe('"4"')
    expect(stored).toMatch(/^"[^".]+\.[^".]+"$/)
  })

  it('stores nothing when the response carries no ETag', async () => {
    // The control. Without it a store that wrote `undefined` under the key would pass the first test
    // by accident on any later read.
    respondWith(null)

    await getOwnSupplier()

    expect(lookupETag('/api/v1/suppliers/SUP-2026-000001')).toBeUndefined()
  })
})
