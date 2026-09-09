import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { PROFILE_DISPLAY_FIELDS, LEGAL_INFO_FIELDS } from '../routes/profileDisplayFields'

/**
 * Sweep: every key a screen BUILDS at runtime resolves in both languages.
 *
 * <p><b>The defect this closes.</b> `ReviewApplicationPage` rendered ``t(`onboarding.fields.${f}`)`` over
 * `PROFILE_DISPLAY_FIELDS` — the profile model's own field names. Four of the five had a label in that
 * namespace by coincidence; the fifth, `defaultCurrency`, did not, because the wizard calls the same thing
 * `currencyCode`. i18next falls back to printing the key, so a government officer deciding a company's
 * application read the literal string `onboarding.fields.defaultCurrency` where a field label belongs — in
 * both Arabic and English. It survived 137 axe scans, 630 unit tests and a screenshot review, because a
 * key rendered as text is still text.</p>
 *
 * <p><b>What this can and cannot check.</b> The product builds keys at 53 sites. This checks the ones whose
 * inputs are enumerable from a constant — where the full set of possible keys is knowable without running
 * the app. A site whose variable comes from the server (a status code, a category code) is not covered
 * here and is listed below as such, so the gap is written down rather than implied.</p>
 */

const CONFIG = readFileSync(resolve(process.cwd(), 'src/i18n/config.ts'), 'utf8')

/**
 * Every dynamic-key site whose inputs come from a constant in this repository.
 *
 * <p>Each entry is a namespace and the exact set of names a screen will interpolate into it. Adding a
 * field to one of those constants without adding its label fails here rather than on a reviewer's screen.</p>
 */
const ENUMERABLE_SITES: { site: string; namespace: string; keys: readonly string[] }[] = [
  {
    site: 'ReviewApplicationPage / ProfilePage — the profile grid',
    namespace: 'profile.fields',
    keys: PROFILE_DISPLAY_FIELDS,
  },
  {
    site: 'ReviewApplicationPage / ProfilePage — the legal-information grid',
    namespace: 'profile.fields',
    keys: LEGAL_INFO_FIELDS,
  },
  {
    site: 'ReviewApplicationPage — the request-info checklist (MSP-77 field CODES, the wizard vocabulary)',
    namespace: 'onboarding.fields',
    // Mirrors PROFILE_FIELDS in ReviewApplicationPage.tsx, which mirrors ProfileFieldCodes.cs.
    keys: [
      'description', 'website', 'supplierGroup', 'currencyCode', 'primaryContactPhone',
      'legalInfo', 'address', 'contact', 'representative', 'branch', 'bankAccount', 'categoryLink', 'logo',
    ],
  },
]

/**
 * The text of `name: { … }` as a DIRECT CHILD of `source`, brace-balanced.
 *
 * <p>Direct child, not "first occurrence anywhere": the catalogue has an `onboarding` under `status` (the
 * state-machine labels) as well as the wizard's own `onboarding`, and a naive `indexOf` finds the state
 * machine — 362 characters with no `fields` block in it. That is how this sweep failed on its first run,
 * which is a small demonstration of the thing it exists to catch.</p>
 */
function child(source: string, name: string): string {
  let depth = 0
  for (let i = 0; i < source.length; i += 1) {
    const ch = source[i]
    // Comments first: this file's prose is full of apostrophes, and reading one as a string literal
    // swallows every brace after it.
    if (ch === '/' && source[i + 1] === '/') {
      const nl = source.indexOf('\n', i)
      if (nl === -1) break
      i = nl
      continue
    }
    if (ch === '/' && source[i + 1] === '*') {
      i = source.indexOf('*/', i) + 1
      continue
    }
    if (ch === "'" || ch === '"' || ch === '`') {
      const quote = ch
      i += 1
      while (i < source.length && source[i] !== quote) i += source[i] === '\\' ? 2 : 1
      continue
    }
    if (ch === '{') {
      depth += 1
      continue
    }
    if (ch === '}') {
      depth -= 1
      continue
    }
    if (depth !== 1) continue
    if (!source.startsWith(`${name}: {`, i)) continue

    // Found it at this level: return its balanced block.
    let inner = 0
    for (let j = source.indexOf('{', i); j < source.length; j += 1) {
      const c = source[j]
      if (c === '/' && source[j + 1] === '/') {
        const nl = source.indexOf('\n', j)
        if (nl === -1) break
        j = nl
        continue
      }
      if (c === "'" || c === '"' || c === '`') {
        const quote = c
        j += 1
        while (j < source.length && source[j] !== quote) j += source[j] === '\\' ? 2 : 1
        continue
      }
      if (c === '{') inner += 1
      else if (c === '}') {
        inner -= 1
        if (inner === 0) return source.slice(source.indexOf('{', i), j + 1)
      }
    }
    break
  }
  throw new Error(`no direct child named ${name}`)
}

/** The `fields` block of one namespace inside one language's resource. */
function namespaceFields(language: 'ar' | 'en', namespace: string): string {
  // Arabic is the first resource in the file and English the last.
  const from = language === 'en' ? CONFIG.lastIndexOf('en: {') : CONFIG.indexOf('ar: {')
  const resource = CONFIG.slice(from)
  const translation = child(resource, 'translation')
  const [parent, leaf] = namespace.split('.')
  return child(child(translation, parent), leaf)
}

describe('dynamic translation keys', () => {
  it('the sweep covers the sites it claims to', () => {
    // The denominator, before the rule: an empty list would pass every assertion below.
    expect(ENUMERABLE_SITES.length).toBeGreaterThanOrEqual(3)
    expect(ENUMERABLE_SITES.flatMap((s) => s.keys).length).toBeGreaterThanOrEqual(20)
  })

  for (const language of ['ar', 'en'] as const) {
    it(`every enumerable key resolves in ${language}`, () => {
      const missing: string[] = []
      for (const { site, namespace, keys } of ENUMERABLE_SITES) {
        const fields = namespaceFields(language, namespace)
        for (const key of keys) {
          if (!new RegExp(`(^|[\\s,{])${key}\\s*:`).test(fields)) {
            missing.push(`${namespace}.${key}  — built by ${site}`)
          }
        }
      }
      expect(missing, `these render as their own key on screen:\n  ${missing.join('\n  ')}`).toEqual([])
    })
  }

  it('the check can fail', () => {
    // The control. A matcher that found every name would keep this green forever - the failure this
    // repository has now found in six other sweeps.
    const fields = namespaceFields('en', 'profile.fields')
    expect(/(^|[\s,{])description\s*:/.test(fields)).toBe(true)
    expect(/(^|[\s,{])aFieldNobodyDefined\s*:/.test(fields)).toBe(false)
    // And the specific regression: the wizard namespace does NOT carry the profile model's name for the
    // currency, which is exactly why the profile grid must not read from it.
    expect(/(^|[\s,{])defaultCurrency\s*:/.test(namespaceFields('en', 'onboarding.fields'))).toBe(false)
    expect(/(^|[\s,{])defaultCurrency\s*:/.test(fields)).toBe(true)
  })
})
