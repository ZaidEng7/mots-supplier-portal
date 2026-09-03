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
  for (const prefix of prefixesOf(path)) {
    const found = etags.get(prefix)
    if (found) return found
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
