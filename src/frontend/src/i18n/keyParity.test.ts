import { describe, expect, it } from 'vitest'
import i18n from './config'

/**
 * The Arabic and English catalogues must hold exactly the same keys.
 *
 * <p><b>Why this is not obvious, and why it is not cosmetic.</b> `config.ts` sets
 * `fallbackLng: 'ar'`. So a key present in Arabic and missing from English does not render the key, and
 * does not render blank — it renders <b>Arabic text to an English reader</b>, in an English page, with no
 * error anywhere. A missing string is normally the loudest kind of bug; this configuration makes it one of
 * the quietest.</p>
 *
 * <p>It was found by accident. A two-part edit added `rfq.cancelWarning` to both blocks; the first half
 * failed on a stale expected value and never wrote, the second half succeeded, and the cancel dialog
 * rendered <i>"الإلغاء نهائي…"</i> inside an English test. Every other test in the file passed. Nothing in
 * this repository compared the two key sets, so nothing could have caught it — which is the same shape as
 * the defects this batch has been closing: an instrument that measures nothing.</p>
 *
 * <p>The reverse direction matters too, though it fails loudly rather than quietly: a key present in
 * English and missing from Arabic falls back to Arabic, finds nothing, and prints the raw key.</p>
 */

/** Every leaf key in a resource tree, as dotted paths. Arrays are leaves; there are none today. */
function leafKeys(node: unknown, prefix = ''): string[] {
  if (node === null || typeof node !== 'object' || Array.isArray(node)) return [prefix]
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([key, value]) => leafKeys(value, prefix ? `${prefix}.${key}` : key))
}

describe('the two catalogues hold the same keys', () => {
  const ar = leafKeys(i18n.getResourceBundle('ar', 'translation'))
  const en = leafKeys(i18n.getResourceBundle('en', 'translation'))

  it('measures both catalogues, and they are the size this product actually has', () => {
    // The denominator. A parity check that passes because it read two empty objects is worse than no
    // check, so this asserts there was something to compare before comparing it.
    expect(ar.length).toBeGreaterThan(1500)
    expect(en.length).toBeGreaterThan(1500)
  })

  it('has no key that exists in Arabic and not in English', () => {
    const englishKeys = new Set(en)
    const missing = ar.filter((key) => !englishKeys.has(key))

    expect(missing, 'fallbackLng is "ar", so each of these renders ARABIC TEXT to an English reader').toEqual([])
  })

  it('has no key that exists in English and not in Arabic', () => {
    const arabicKeys = new Set(ar)
    const missing = en.filter((key) => !arabicKeys.has(key))

    expect(missing, 'each of these prints its raw key to an Arabic reader').toEqual([])
  })

  it('the check can fail', () => {
    // Revert-to-red, in the shape the real defect took: a key on one side only. Deliberately built from
    // fixed lists rather than from the real catalogues, so that a genuine parity failure produces one
    // clear failure above rather than two.
    const arabicSide = ['rfq.cancelTitle', 'rfq.cancelWarning']
    const englishSide = new Set(['rfq.cancelTitle'])

    expect(arabicSide.filter((key) => !englishSide.has(key))).toEqual(['rfq.cancelWarning'])
  })
})
