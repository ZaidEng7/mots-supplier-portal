// The ETag store's own behaviour, one property per test.
//
// A resource's ETag is sent on a transition underneath it, because §8.1's guarded mutations sit under the resource
// whose version they assert: POST /proposals/{code}/submit is a transition of /proposals/{code}. The control beside
// it is that one resource's ETag is never sent for a different resource - without it the prefix walk could pass any
// stored tag to anything.
//
// A version is forgotten once its resource has been mutated: the row has moved on, and replaying the old version
// would 412 the user's own second edit. A query string is ignored when matching.
//
// A child write's fresh version is filed where the precondition it used was stored. That is T-030 split (2), and a
// latent defect in split (3): a child write forgets every prefix and then files the fresh ETag under the WRITE
// path, so after editing a contact the version sits at /suppliers/me/contacts, and a write to
// /suppliers/me/addresses walks to /suppliers/me, finds nothing, and sends no If-Match at all - the server answers
// 428 and the supplier's second edit fails with nothing on screen to explain it. ownerPrefixOf is what lets the
// transport put the new version back where the old one lived, without the store having to guess where a resource
// boundary is inside a path.
//
// The last test is the control on that: with nothing read first there is no owner prefix to file against. A guarded
// write with no prior read is a 428 by design, and the store must not invent a home for a version - filing it at
// the collection would hand one aggregate's version to another, which is the hazard the prefix walk exists to
// avoid.

import { beforeEach, describe, expect, it } from 'vitest'
import { clearETags, forgetETags, lookupETag, ownerPrefixOf, rememberETag } from './etags'

describe('etag store', () => {
  beforeEach(() => clearETags())

  it('sends a resource ETag on a transition underneath it', () => {
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-1/submit')).toBe('"AAAAAQ"')
    expect(lookupETag('/api/v1/proposals/PRP-1')).toBe('"AAAAAQ"')
  })

  it('does not send one resource ETag for a different resource', () => {
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-2/submit')).toBeUndefined()
    expect(lookupETag('/api/v1/rfqs/RFQ-1')).toBeUndefined()
  })

  it('forgets a version once its resource has been mutated', () => {
    rememberETag('/api/v1/proposals/PRP-1', '"AAAAAQ"')

    forgetETags('/api/v1/proposals/PRP-1/submit')

    expect(lookupETag('/api/v1/proposals/PRP-1')).toBeUndefined()
  })

  it('ignores a query string when matching', () => {
    rememberETag('/api/v1/proposals/PRP-1?expand=items', '"AAAAAQ"')

    expect(lookupETag('/api/v1/proposals/PRP-1')).toBe('"AAAAAQ"')
  })

  it('files a child write\'s fresh version where the precondition it used was stored', () => {
    rememberETag('/api/v1/suppliers/me', '"AAAAAQ"')

    const owner = ownerPrefixOf('/api/v1/suppliers/me/contacts')
    expect(owner).toBe('/api/v1/suppliers/me')

    forgetETags('/api/v1/suppliers/me/contacts')
    rememberETag(owner!, '"AAAAAg"')

    expect(lookupETag('/api/v1/suppliers/me/addresses')).toBe('"AAAAAg"')
    expect(lookupETag('/api/v1/suppliers/me')).toBe('"AAAAAg"')
  })

  it('has no owner prefix to file against when nothing was read first', () => {
    expect(ownerPrefixOf('/api/v1/rfqs/RFQ-1/items')).toBeUndefined()

    rememberETag('/api/v1/rfqs/RFQ-1', '"AAAAAQ"')
    expect(ownerPrefixOf('/api/v1/rfqs/RFQ-2/items')).toBeUndefined()
  })
})
