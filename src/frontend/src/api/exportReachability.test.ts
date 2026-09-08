import { describe, expect, it } from 'vitest'

/**
 * Every capability the API client exposes is used by something, or is named here as deliberately not.
 *
 * <p><b>The defect this catches.</b> `PUT /api/v1/rfqs/{code}` existed, its client function `updateRfq`
 * existed beside it, and no screen called either for months — a tender's own details could not be corrected
 * after it was authored, and the reason was not a missing endpoint but a missing button. Batch 13 found it
 * by hand, from a walkthrough, after an officer typed the wrong date. The mirror image of the router guard:
 * that one asks whether every screen can be reached by clicking, this one asks whether every capability is
 * reachable at all.</p>
 *
 * <p><b>Referenced OUTSIDE `src/api`, and not by a test.</b> Both qualifications are the point. A function
 * used only by its own module is plumbing, not a capability; a function used only by its own test is a
 * capability nobody surfaced, which is exactly the `updateRfq` case — it had tests.</p>
 *
 * <p>Two exemption lists, written by hand for the same reason the router guard's are: a pattern-matched
 * exemption is one the next instance joins without anybody deciding.</p>
 */

/** Plumbing: consumed inside the API layer by other api modules, and no screen's business. */
const USED_ONLY_INSIDE_THE_API_LAYER: Record<string, string> = {
  apiFetch: 'The transport itself. Every api module calls it; a component that called it directly would be bypassing the ETag and problem-body handling that exists to be unbypassable.',
  rememberETag: '§8.1 client half — the four *From helpers file a read ETag under a second path with it.',
  lookupETag: 'Read by apiFetch when it attaches If-Match.',
  ownerPrefixOf: 'Read by apiFetch to decide where a fresh ETag goes back, see etags.ts for why the store cannot deduce it.',
  forgetETags: 'Called by apiFetch after a mutation: a version cached for a row that just moved is a 412 waiting to happen.',
  clearETags: 'Called on sign-out, and by tests that need an empty store. In-memory per tab, so nothing else should be clearing it.',
  problemMessage: 'RFC 9457 rendering, used by the api modules own error classes.',
  hasCode: 'Problem-code predicate used by the api layer error types.',
  getOffering: 'Added in batch 13 so updateOffering and deactivateOffering re-read before they write — the fix for an editor that patched an item it had never read. It is the read half of those two writes rather than a capability of its own.',
}

/**
 * Endpoint wrappers kept without a screen to call them, deliberately. Each entry says why the capability is
 * worth keeping and what would surface it — an empty list is the healthy state, and a growing one is the
 * signal this check exists to give.
 */
const DELIBERATELY_UNSURFACED: Record<string, string> = {}

/** Exported functions and consts in `src/api`, by the module that declares them. */
function collectExports(): Map<string, string> {
  const modules = import.meta.glob('./*.ts', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
  const exports = new Map<string, string>()

  for (const [file, source] of Object.entries(modules)) {
    if (/\.(test|spec)\./.test(file)) continue

    for (const match of source.matchAll(/^export\s+(?:async\s+)?function\s+([A-Za-z0-9_]+)/gm)) {
      exports.set(match[1], file)
    }
    for (const match of source.matchAll(/^export\s+(?:const|let)\s+([A-Za-z0-9_]+)/gm)) {
      exports.set(match[1], file)
    }
  }

  return exports
}

/** Everything the app outside `src/api` says, excluding tests and stories. */
function sourceOutsideTheApiLayer(): string {
  const modules = import.meta.glob('../**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

  return Object.entries(modules)
    // Vite keys a glob relative to the IMPORTER, so this module's own directory arrives as './auth.ts'
    // rather than '../api/auth.ts'. Filtering on the wrong one of those is how this check first passed
    // while reading the api layer as its own audience, which is precisely what it must not count.
    .filter(([file]) => !file.startsWith('./') && !file.startsWith('../api/'))
    .filter(([file]) => !/\.(test|spec|stories)\./.test(file))
    .map(([, source]) => source)
    .join('\n')
}

function isReferenced(name: string, source: string): boolean {
  return new RegExp(`\\b${name}\\b`).test(source)
}

describe('every API capability is reachable from a screen', () => {
  it('has no export that nothing outside the api layer mentions', () => {
    const exports = collectExports()
    const outside = sourceOutsideTheApiLayer()

    // The denominators, before the rule. An empty export map or an empty haystack satisfies every
    // assertion below while checking nothing.
    expect(exports.size, 'the api modules exports must be discoverable by the scanner').toBeGreaterThan(150)
    expect(outside.length, 'the rest of the app must be readable').toBeGreaterThan(100_000)
    expect(isReferenced('getOwnSupplier', outside)).toBe(true)
    expect(isReferenced('thisIsNotAFunctionAnywhere', outside)).toBe(false)

    const unreachable = [...exports]
      .filter(([name]) => !(name in USED_ONLY_INSIDE_THE_API_LAYER) && !(name in DELIBERATELY_UNSURFACED))
      .filter(([name]) => !isReferenced(name, outside))
      .map(([name, file]) => `${name} (${file})`)
      .sort()

    expect(
      unreachable,
      'nothing outside src/api mentions these, so whatever they do cannot be done through the product',
    ).toEqual([])
  })

  it('has no exemption that is stale', () => {
    const exports = collectExports()
    const outside = sourceOutsideTheApiLayer()
    const exempted = { ...USED_ONLY_INSIDE_THE_API_LAYER, ...DELIBERATELY_UNSURFACED }

    // An exemption naming an export that no longer exists is an entry nobody is reading.
    expect(
      Object.keys(exempted).filter((name) => !exports.has(name)),
      'exempted names that are no longer exported',
    ).toEqual([])

    // And one that a screen now uses has stopped being an exemption. Both directions, because a list that
    // only ever grows is a list people stop reading.
    expect(
      Object.keys(exempted).filter((name) => isReferenced(name, outside)),
      'these are used outside the api layer now, so their exemptions should go',
    ).toEqual([])

    // Every entry carries a reason. A bare name tells the next reader nothing about whether it still holds.
    expect(Object.values(exempted).filter((reason) => reason.trim().length < 30)).toEqual([])
  })
})
