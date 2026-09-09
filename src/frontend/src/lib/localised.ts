/**
 * A reference row's name in the reader's language, or the raw code when the catalogue has no row for it.
 *
 * <p>Four screens wrote `match ? (isArabic ? match.nameAr : match.nameEn) : code` inline. The nesting is
 * what made it hard to read: two unrelated questions - "do we know this code?" and "which language?" -
 * answered in one expression. The code as a fallback is deliberate and worth keeping: a category the
 * catalogue no longer carries should still render as something a person can quote back.</p>
 */
export function localisedName(
  match: { nameAr: string; nameEn: string } | undefined,
  isArabic: boolean,
  fallback: string,
): string {
  if (!match) return fallback
  return isArabic ? match.nameAr : match.nameEn
}
