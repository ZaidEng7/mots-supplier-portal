/**
 * Task #41: country-code dropdown for phone fields.
 *
 * The backend stores phone as a free string with no format regex (RegistrationEndpoints.cs only
 * enforces NotEmpty + MaximumLength(30)) - but every phone already in the DB was written as
 * `+<dial code><digits>`, no spaces or dashes (e.g. "+963988112233"). This module composes new
 * entries into that same convention and parses existing ones back into {countryCode, localNumber}
 * for editing, without ever changing the wire format the backend expects.
 *
 * Ordered longest-prefix-first isn't needed here - none of these dial codes are prefixes of each
 * other - but the list itself IS the parse order, so a new entry must go in as its own distinct
 * code, not as a substring of an existing one.
 */
export const COUNTRY_DIAL_CODES = [
  { code: '963', country: 'SY' }, // Syria
  { code: '962', country: 'JO' }, // Jordan
  { code: '961', country: 'LB' }, // Lebanon
  { code: '964', country: 'IQ' }, // Iraq
  { code: '966', country: 'SA' }, // Saudi Arabia
  { code: '971', country: 'AE' }, // UAE
  { code: '974', country: 'QA' }, // Qatar
  { code: '965', country: 'KW' }, // Kuwait
  { code: '973', country: 'BH' }, // Bahrain
  { code: '968', country: 'OM' }, // Oman
  { code: '970', country: 'PS' }, // Palestine
  { code: '90', country: 'TR' }, // Turkey
  { code: '20', country: 'EG' }, // Egypt
] as const

export const OTHER_COUNTRY_CODE = 'other'
export const DEFAULT_COUNTRY_CODE = '963'

export interface ParsedPhone {
  countryCode: string
  localNumber: string
}

/**
 * Splits a stored phone value into a dial code + local number for the editing UI.
 *
 * Guarantees round-tripping: for ANY existing value, `composePhone(parsePhone(v))` reproduces `v`
 * exactly when the local number is left untouched - matched values reconstruct from code+digits,
 * and anything that doesn't start with "+" or doesn't match a known code falls back to OTHER_COUNTRY_CODE
 * with the original string preserved verbatim, so unrecognized/legacy formats are never corrupted.
 */
export function parsePhone(raw: string | null | undefined): ParsedPhone {
  if (!raw) return { countryCode: DEFAULT_COUNTRY_CODE, localNumber: '' }
  if (raw.startsWith('+')) {
    const digits = raw.slice(1)
    const match = COUNTRY_DIAL_CODES.find((c) => digits.startsWith(c.code))
    if (match) return { countryCode: match.code, localNumber: digits.slice(match.code.length) }
  }
  return { countryCode: OTHER_COUNTRY_CODE, localNumber: raw }
}

/**
 * Composes a dial code + local number back into the stored wire format.
 *
 * `OTHER_COUNTRY_CODE` passes `localNumber` through verbatim - it holds a full number a user typed
 * themselves (or an unrecognized existing value being round-tripped unedited), so it must not be
 * reformatted. Every real dial code strips non-digits from the local part before concatenating, so
 * a user pasting "988-112-233" or "988 112 233" still produces "+963988112233".
 */
export function composePhone(countryCode: string, localNumber: string): string {
  const trimmed = localNumber.trim()
  if (trimmed === '') return ''
  if (countryCode === OTHER_COUNTRY_CODE) return trimmed
  return `+${countryCode}${trimmed.replace(/\D/g, '')}`
}
