// SCR-716: apply the administrator's rewordings over the shipped bundle.
//
// Every string in this product is compiled into i18n/config.ts, so correcting a single word meant a code change and a
// deployment - the wrong shape for copy, since the people who own the wording are not the people who own releases.
// Corrections of exactly that kind accumulate faster than releases do.
//
// MERGED, NOT REPLACED. addResourceBundle with deep and overwrite layers the overrides on top, so an override for one key
// leaves every other string alone. Replacing the bundle would mean an administrator who reworded one label blanked the rest of
// the product.
//
// FAILURE IS SILENCE: if the request fails the shipped strings stand, which is a working product in the language it was built
// in. Blocking startup on this, or showing an error, would let a cosmetic facility break sign-in. The catch is deliberately
// empty for that reason.
//
// THE APPLIED SET guards against a loop. The refresh at the end re-emits languageChanged, which main.tsx listens to - without
// the guard the two would call each other forever, hammering the endpoint. Found by reading the wiring back rather than by
// watching it happen. That refresh is needed at all because addResourceBundle does not itself notify, so without it the
// rewordings would appear only on the next navigation.
//
// THE WIRE FORMAT is flat dotted keys - "proposal.errors.reviseFailed" - because that is how the administrator sees them and
// how the server stores them. i18next wants a nested object, so the expansion happens here rather than making the API carry a
// shape nobody types.
//
// A KEY WHOSE PARENT IS ALREADY A STRING cannot be nested under: "a.b" and "a.b.c" cannot both exist, because the first makes
// `b` a string and the second needs it to be an object. THAT KEY is skipped, and only that key. It used to return, which read
// as skipping but abandoned the whole response - and did so BEFORE addResourceBundle ran, so a single malformed override
// discarded every other rewording in the payload, including the ones already nested. An administrator would have seen one bad
// key silently undo fifty good ones, with nothing in the UI to say why. Caught by a test written from this comment's own claim.

import i18n from './config'
import { API_BASE_URL } from '../api/auth'


const applied = new Set<string>()

export async function applyStringOverrides(language: string): Promise<void> {
  if (applied.has(language)) return
  applied.add(language)

  try {
    const response = await fetch(`${API_BASE_URL}/api/v1/ui-strings/${language}`)
    if (!response.ok) return

    const bundle = (await response.json()) as { language: string; strings: Record<string, string> }
    const entries = Object.entries(bundle.strings)
    if (entries.length === 0) return

    const nested: Record<string, unknown> = {}
    for (const [key, value] of entries) {
      const path = key.split('.')
      let cursor: Record<string, unknown> | null = nested
      for (const segment of path.slice(0, -1)) {
        if (typeof cursor[segment] !== 'object' || cursor[segment] === null) {
          if (segment in cursor) { cursor = null; break }
          cursor[segment] = {}
        }
        cursor = cursor[segment] as Record<string, unknown>
      }
      if (cursor === null) continue
      cursor[path[path.length - 1]] = value
    }

    i18n.addResourceBundle(language, 'translation', nested, true, true)
    await i18n.changeLanguage(i18n.language)
  } catch {
  }
}
