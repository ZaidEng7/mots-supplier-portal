// §8.1's client half: remember the ETag of every read, and send it back as If-Match on the mutations that
// require one.
//
// Kept in one place rather than threaded through each of the roughly 60 mutation call sites. A caller that
// forgets is not a subtle bug - the server answers 428 and nothing saves - but "every caller remembers" is not
// a property anyone can check, whereas "the transport does it" is.
//
// Why the lookup walks the path. An ETag belongs to a resource, and §8.1's guarded mutations sit underneath it:
// POST /proposals/{code}/submit is a transition of /proposals/{code}, whose ETag came from a GET of that path.
// So prefixesOf lists the candidate owning-resource paths longest-first, stopping at three segments
// (/api/v1/{collection}), and the lookup uses the first one it has a version for.
//
// The store is in memory only, and per tab. A version from a previous session is worse than none: it would be
// stale by definition and turn every first save into a 412.
//
// THE ALIAS. Two prefixes can name ONE resource, so a version filed under either has to be filed under both,
// and the store has to be told. The supplier aggregate answers at two spellings: /suppliers/me, which is what
// the SPA reads, and /suppliers/{code}, which is what the profile PATCH and the document routes write to. The
// prefix walk is textual and upward-only, so those are two unrelated keys holding two versions of one row, and
// whichever a write refreshed, the next write through the other spelling asserted the stale one. The user met
// it as "Could not record acceptance" on a record nobody else had touched, twice from the same screen: upload a
// document, then press Accept on the terms. A page reload cleared it, because a reload re-reads and refiles
// both - which is why it looked intermittent. The pairing is registered by the one function that can learn it,
// the profile parser, which sees the supplier code in the body it just read; every other caller stays ignorant
// of it.
//
// OWNERPREFIXOF answers which stored path a write against a given path would take its precondition from - the
// resource whose version that write asserts. The transport needs it because of T-030 split (2): a child write
// forgets every prefix and then files the response's fresh ETag under the WRITE path, so after adding an RFQ
// item the version sits at /rfqs/RFQ-1/items, and a write to /rfqs/RFQ-1/requirements walks up to /rfqs/RFQ-1,
// finds the entry gone, and sends no If-Match - a 428 on the officer's second edit. Filing the fresh version
// back where the old one lived fixes that without the store having to work out where a resource boundary sits
// inside a path, which it cannot: /admin/field-config/{category}/{code} and /rfqs/{code}/items put it at
// different depths and neither is deducible from a segment count. It answers undefined when nothing was read
// first, and nothing is invented - filing a version at the collection would offer one aggregate's version as
// the precondition for another, which is precisely what the prefix walk exists to prevent.
//
// FORGETTING. A mutation moves the resource on, so the version cached for it is stale the moment it succeeds.
// Dropping it forces the next write to wait for a fresh read rather than replay a version the row no longer
// has, which would be a 412 the user cannot explain, on their own second edit. The twin is dropped with it, for
// the same reason: it holds the same row's version, so leaving it would hand the next write a version the row
// no longer has. Clearing the store clears the pairing too, because the pairing belongs to whoever was signed
// in and the next session's `me` is a different supplier.

const etags = new Map<string, string>()

function prefixesOf(path: string): string[] {
  const segments = path.split('?')[0].replace(/^\/+/, '').split('/')
  const out: string[] = []
  for (let take = segments.length; take >= 3; take--) {
    out.push('/' + segments.slice(0, take).join('/'))
  }
  return out
}

const aliasOf = new Map<string, string>()

export function aliasETagPaths(one: string, other: string): void {
  aliasOf.set(one, other)
  aliasOf.set(other, one)
}

export function rememberETag(path: string, etag: string | null): void {
  if (!etag) return
  const prefix = prefixesOf(path)[0] ?? path
  etags.set(prefix, etag)
  const twin = aliasOf.get(prefix)
  if (twin) etags.set(twin, etag)
}

export function lookupETag(path: string): string | undefined {
  const prefix = ownerPrefixOf(path)
  return prefix === undefined ? undefined : etags.get(prefix)
}

export function ownerPrefixOf(path: string): string | undefined {
  for (const prefix of prefixesOf(path)) {
    if (etags.has(prefix)) return prefix
  }
  return undefined
}

export function forgetETags(path: string): void {
  for (const prefix of prefixesOf(path)) {
    etags.delete(prefix)
    const twin = aliasOf.get(prefix)
    if (twin) etags.delete(twin)
  }
}

export function clearETags(): void {
  etags.clear()
  aliasOf.clear()
}
