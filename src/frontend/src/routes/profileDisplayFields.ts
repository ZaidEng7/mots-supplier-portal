import type { SupplierProfile } from '../api/supplier'

/**
 * Fields shown in the reviewer's profile grid.
 *
 * SEPARATE from PROFILE_FIELDS, which holds the MSP-77 flagged-field CODES. The two vocabularies
 * are related but not identical, and conflating them is what broke this page: MSP-77 replaced the
 * display list with the flagged-field catalog, and `legalInfo` - a code in that catalog - resolves
 * to an OBJECT on the DTO. React throws on rendering an object, so the reviewer page crashed for
 * every supplier with a populated LegalInfo, which is every registered supplier.
 *
 * A flagged-field code names something a reviewer can ask a supplier to correct ("legalInfo",
 * "bankAccount"). A display field names a scalar the reviewer can read. Some names appear in both
 * sets, which is precisely why the mistake was easy to make and hard to see - the first five rows
 * rendered correctly and only the sixth crashed.
 */
export const PROFILE_DISPLAY_FIELDS = [
  'description',
  'website',
  'supplierGroup',
  'currencyCode',
  'primaryContactPhone',
] as const satisfies readonly (keyof SupplierProfile)[]

/**
 * Reads one display field as text.
 *
 * Returns a string always, never an object. The previous code indexed the DTO through
 * `as unknown as Record<string, string | null>` - an assertion that the DTO has a shape it does
 * not have, which is what stopped TypeScript catching this. The `satisfies` clause above now makes
 * the compiler check that every listed field really exists on SupplierProfile, and this function
 * guarantees the rendered value is renderable even if a non-scalar ever slips through.
 */
export function profileDisplayValue(supplier: SupplierProfile, field: (typeof PROFILE_DISPLAY_FIELDS)[number]): string {
  const value = supplier[field]
  if (value === null || value === undefined || value === '') return '—'
  // Defensive rather than decorative: rendering an object is the exact crash this module exists to
  // prevent, and a future DTO change could reintroduce one without touching this file.
  if (typeof value === 'object') return '—'
  return String(value)
}
