import { beforeEach, describe, expect, it } from 'vitest'
import { clearETags, forgetETags, lookupETag, rememberETag } from './etags'

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
})
