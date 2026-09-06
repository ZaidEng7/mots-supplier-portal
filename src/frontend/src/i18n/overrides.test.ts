import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from './config'

/**
 * SCR-716's client half. Three properties matter and none of them is about happy-path merging:
 * failure is SILENT (a cosmetic facility must not be able to break sign-in), the merge is a LAYER
 * rather than a replacement, and one malformed key must not cost the rest.
 */
describe('applyStringOverrides', () => {
  let applyStringOverrides: (language: string) => Promise<void>
  const original = globalThis.fetch

  beforeEach(async () => {
    // Fresh module per test: the function memoises which languages it has fetched, which is what
    // stops it from looping against its own languageChanged listener.
    vi.resetModules()
    applyStringOverrides = (await import('./overrides')).applyStringOverrides
  })

  afterEach(() => {
    globalThis.fetch = original
    vi.restoreAllMocks()
  })

  function respond(body: unknown, ok = true) {
    globalThis.fetch = vi.fn(async () => ({ ok, json: async () => body })) as unknown as typeof fetch
  }

  it('layers an override over the shipped bundle without disturbing its siblings', async () => {
    // Replacing the bundle would mean an administrator who reworded one label blanked the rest of
    // the product, so this asserts a NEIGHBOUR survives, not just that the target changed.
    const neighbour = i18n.getFixedT('en', 'translation')('common.cancel')
    respond({ language: 'en', strings: { 'common.loading': 'One moment' } })

    await applyStringOverrides('en')

    expect(i18n.getFixedT('en', 'translation')('common.loading')).toBe('One moment')
    expect(i18n.getFixedT('en', 'translation')('common.cancel')).toBe(neighbour)
  })

  it('fetches a language only once', async () => {
    respond({ language: 'en', strings: { 'common.loading': 'One moment' } })

    await applyStringOverrides('en')
    await applyStringOverrides('en')

    expect(globalThis.fetch).toHaveBeenCalledTimes(1)
  })

  it('leaves the shipped strings standing when the request fails', async () => {
    // A working product in the language it was built in. Blocking startup on this, or surfacing an
    // error, would let a cosmetic facility break sign-in.
    const shipped = i18n.getFixedT('en', 'translation')('common.loading')
    globalThis.fetch = vi.fn(async () => { throw new Error('offline') }) as unknown as typeof fetch

    await expect(applyStringOverrides('en')).resolves.toBeUndefined()
    expect(i18n.getFixedT('en', 'translation')('common.loading')).toBe(shipped)
  })

  it('leaves the shipped strings standing on a non-2xx response', async () => {
    const shipped = i18n.getFixedT('en', 'translation')('account.title')
    respond({}, false)

    await applyStringOverrides('en')

    expect(i18n.getFixedT('en', 'translation')('account.title')).toBe(shipped)
  })

  it('applies the other overrides when one key cannot be nested', async () => {
    // "a.b" and "a.b.c" cannot both exist: the first makes `b` a string, and the second needs it to
    // be an object. One malformed key must cost only itself - an administrator who saved a bad key
    // should not silently lose every rewording that came after it in the response.
    respond({
      language: 'en',
      strings: {
        'common.loading': 'One moment',
        'common.loading.nested': 'Impossible',
        'common.cancel': 'Stop',
      },
    })

    await applyStringOverrides('en')

    expect(i18n.getFixedT('en', 'translation')('common.loading')).toBe('One moment')
    expect(i18n.getFixedT('en', 'translation')('common.cancel')).toBe('Stop')
  })

  it('does nothing when the server returns no overrides', async () => {
    const shipped = i18n.getFixedT('en', 'translation')('common.loading')
    respond({ language: 'en', strings: {} })

    await applyStringOverrides('en')

    expect(i18n.getFixedT('en', 'translation')('common.loading')).toBe(shipped)
  })
})
