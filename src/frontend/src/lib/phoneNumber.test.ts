import { describe, expect, it } from 'vitest'
import { parsePhone, composePhone, COUNTRY_DIAL_CODES, OTHER_COUNTRY_CODE, DEFAULT_COUNTRY_CODE } from './phoneNumber'

describe('parsePhone', () => {
  it('defaults to Syria with an empty local number for null/undefined/empty', () => {
    expect(parsePhone(null)).toEqual({ countryCode: DEFAULT_COUNTRY_CODE, localNumber: '' })
    expect(parsePhone(undefined)).toEqual({ countryCode: DEFAULT_COUNTRY_CODE, localNumber: '' })
    expect(parsePhone('')).toEqual({ countryCode: DEFAULT_COUNTRY_CODE, localNumber: '' })
  })

  it('splits a known dial code from the local number', () => {
    expect(parsePhone('+963988112233')).toEqual({ countryCode: '963', localNumber: '988112233' })
    expect(parsePhone('+962791234567')).toEqual({ countryCode: '962', localNumber: '791234567' })
    expect(parsePhone('+905321234567')).toEqual({ countryCode: '90', localNumber: '5321234567' })
  })

  it('falls back to OTHER with the value preserved verbatim when the code is unrecognized', () => {
    expect(parsePhone('+19995551234')).toEqual({ countryCode: OTHER_COUNTRY_CODE, localNumber: '+19995551234' })
  })

  it('falls back to OTHER with the value preserved verbatim when there is no leading +', () => {
    // Real-world legacy/free-form data: nothing in the backend enforces a leading '+', so this
    // must never be silently reformatted or lose digits.
    expect(parsePhone('0988112233')).toEqual({ countryCode: OTHER_COUNTRY_CODE, localNumber: '0988112233' })
  })
})

describe('composePhone', () => {
  it('returns empty string for a blank local number regardless of country', () => {
    expect(composePhone('963', '')).toBe('')
    expect(composePhone('963', '   ')).toBe('')
  })

  it('concatenates dial code and digits, stripping non-digit characters', () => {
    expect(composePhone('963', '988112233')).toBe('+963988112233')
    expect(composePhone('963', '988-112-233')).toBe('+963988112233')
    expect(composePhone('963', '988 112 233')).toBe('+963988112233')
  })

  it('passes OTHER through verbatim, untouched', () => {
    expect(composePhone(OTHER_COUNTRY_CODE, '+19995551234')).toBe('+19995551234')
    expect(composePhone(OTHER_COUNTRY_CODE, '0988112233')).toBe('0988112233')
  })
})

describe('round-tripping', () => {
  it('reproduces every existing stored value exactly when left untouched', () => {
    const existingValues = ['+963988112233', '+962791234567', '+905321234567', '+19995551234', '0988112233', '', null]
    for (const raw of existingValues) {
      const { countryCode, localNumber } = parsePhone(raw)
      expect(composePhone(countryCode, localNumber)).toBe(raw ?? '')
    }
  })

  it('every listed dial code round-trips a freshly composed number back through parsePhone', () => {
    for (const { code } of COUNTRY_DIAL_CODES) {
      const composed = composePhone(code, '5551234')
      expect(parsePhone(composed)).toEqual({ countryCode: code, localNumber: '5551234' })
    }
  })
})
