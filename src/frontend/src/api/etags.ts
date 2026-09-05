/**
 * §8.1's client half: remember the ETag of every read, and send it back as `If-Match` on the
 * mutations that require one.
 *
 * <p>Kept in one place rather than threaded through each of the ~60 mutation call sites. A caller
 * that forgets is not a subtle bug - the server answers 428 and nothing saves - but "every caller
 * remembers" is not a property anyone can check, whereas "the transport does it" is.</p>
 *
 * <p><b>Why walking the path.</b> An ETag belongs to a resource, and §8.1's guarded mutations sit
 * underneath it: `POST /proposals/{code}/submit` is a transition of `/proposals/{code}`, whose ETag
 * came from a GET of that path. So the lookup walks prefixes longest-first and uses the first one it
 * has a version for.</p>
 *
 * <p>In memory only, and per tab. A version from a previous session is worse than none: it would be
 * stale by definition and turn every first save into a 412.</p>
 */
const etags = new Map<string, string>()

/** Candidate owning-resource paths, longest first. Stops at three segments (`/api/v1/{collection}`). */
function prefixesOf(path: string): string[] {
  const segments = path.split('?')[0].replace(/^\/+/, '').split('/')
  const out: string[] = []
  for (let take = segments.length; take >= 3; take--) {
    out.push('/' + segments.slice(0, take).join('/'))
  }
  return out
}

export function rememberETag(path: string, etag: string | null): void {
  if (!etag) return
  etags.set(prefixesOf(path)[0] ?? path, etag)
}

export function lookupETag(path: string): string | undefined {
  const prefix = ownerPrefixOf(path)
  return prefix === undefined ? undefined : etags.get(prefix)
}

/**
 * Which stored path a write against `path` would take its precondition from - the resource whose
 * version this write asserts.
 *
 * <p><b>Why the transport needs this.</b> T-030 split (2). A child write forgets every prefix and then
 * files the response's fresh ETag under the WRITE path, so after adding an RFQ item the version sits
 * at `/rfqs/RFQ-1/items` and a write to `/rfqs/RFQ-1/requirements` walks up to `/rfqs/RFQ-1`, finds
 * the entry gone, and sends no `If-Match` - a 428 on the officer's second edit. Filing the fresh
 * version back where the old one lived fixes that without the store having to work out where a
 * resource boundary sits inside a path, which it cannot: `/admin/field-config/{category}/{code}` and
 * `/rfqs/{code}/items` put it at different depths and neither is deducible from a segment count.</p>
 *
 * <p>Undefined when nothing was read first. Nothing is invented - filing a version at the collection
 * would offer one aggregate's version as the precondition for another, which is precisely what the
 * prefix walk exists to prevent.</p>
 */
export function ownerPrefixOf(path: string): string | undefined {
  for (const prefix of prefixesOf(path)) {
    if (etags.has(prefix)) return prefix
  }
  return undefined
}

/**
 * A mutation moves the resource on, so the version cached for it is stale the moment it succeeds.
 * Dropping it forces the next write to wait for a fresh read rather than replay a version the row
 * no longer has - which would be a 412 the user cannot explain, on their own second edit.
 */
export function forgetETags(path: string): void {
  for (const prefix of prefixesOf(path)) etags.delete(prefix)
}

export function clearETags(): void {
  etags.clear()
}
