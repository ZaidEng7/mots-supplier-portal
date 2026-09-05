import { beforeEach, describe, expect, it } from 'vitest'
import { clearETags, forgetETags, lookupETag, ownerPrefixOf, rememberETag } from './etags'

describe('etag store', () => {
  beforeEach(() => clearETags())

  it('sends a resource ETag on a transition underneath it', () => {
    // §8.1's guarded mutations sit under the resource whose version they assert:
    // POST /proposals/{code}/submit is a transition of /proposals/{code}.
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-1/submit')).toBe('"AAAAAQ"')
    expect(lookupETag('/api/v1/proposals/PRP-1')).toBe('"AAAAAQ"')
  })

  it('does not send one resource ETag for a different resource', () => {
    // The control. Without it the prefix walk could pass any stored tag to anything.
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-2/submit')).toBeUndefined()
    expect(lookupETag('/api/v1/rfqs/RFQ-1')).toBeUndefined()
  })

  it('forgets a version once its resource has been mutated', () => {
    // The row has moved on; replaying the old version would 412 the user's own second edit.
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    forgetETags('/api/v1/proposals/PRP-1/submit')

    expect(lookupETag('/api/v1/proposals/PRP-1')).toBeUndefined()
  })

  it('ignores a query string when matching', () => {
    rememberETag('/api/v1/proposals/PRP-1?expand=items', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-1')).toBe('"AAAAAQ"')
  })

  it('files a child write\'s fresh version where the precondition it used was stored', () => {
    // T-030 split (2), and a latent defect in split (3). A child write forgets every prefix and then
    // files the fresh ETag under the WRITE path - so after editing a contact the version sits at
    // `/suppliers/me/contacts`, and a write to `/suppliers/me/addresses` walks to `/suppliers/me`,
    // finds nothing, and sends no If-Match at all. The server answers 428 and the supplier's second
    // edit fails with nothing on screen to explain it.
    //
    // `ownerPrefixOf` is what lets the transport put the new version back where the old one lived,
    // without the store having to guess where a resource boundary is inside a path.
    rememberETag('/api/v1/suppliers/me', '"AAAAAQ"')

    const owner = ownerPrefixOf('/api/v1/suppliers/me/contacts')
    expect(owner).toBe('/api/v1/suppliers/me')

    forgetETags('/api/v1/suppliers/me/contacts')
    rememberETag(owner!, '"AAAAAg"')

    expect(lookupETag('/api/v1/suppliers/me/addresses')).toBe('"AAAAAg"')
    expect(lookupETag('/api/v1/suppliers/me')).toBe('"AAAAAg"')
  })

  it('has no owner prefix to file against when nothing was read first', () => {
    // The control. A guarded write with no prior read is a 428 by design, and the store must not
    // invent a home for a version - filing it at the collection would hand one aggregate's version to
    // another, which is the hazard the prefix walk exists to avoid.
    expect(ownerPrefixOf('/api/v1/rfqs/RFQ-1/items')).toBeUndefined()

    rememberETag('/api/v1/rfqs/RFQ-1', '"AAAAAQ"')
    expect(ownerPrefixOf('/api/v1/rfqs/RFQ-2/items')).toBeUndefined()
  })
})
