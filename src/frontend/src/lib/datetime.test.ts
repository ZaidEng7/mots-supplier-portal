import { describe, expect, it } from 'vitest'
import { BUSINESS_TIME_ZONE, formatCurrency, formatDate, formatDateTime, formatDeadline, formatRelative } from './datetime'

/**
 * T2-29. These tests are only meaningful when the runner's own zone is NOT Damascus - otherwise
 * they would pass just as well against the `toLocaleString()` code this replaced. The first test
 * asserts that precondition rather than assuming it, so running the suite under a Damascus TZ fails
 * loudly instead of going quietly green.
 *
 * CI/local invocation: `TZ=America/New_York npx vitest run src/lib/datetime.test.ts` and
 * `TZ=UTC npx vitest run src/lib/datetime.test.ts`.
 */
describe('ambient timezone precondition', () => {
  it('is not running in Asia/Damascus, so the assertions below actually prove something', () => {
    expect(Intl.DateTimeFormat().resolvedOptions().timeZone).not.toBe(BUSINESS_TIME_ZONE)
  })
})

/**
 * 21:00 UTC is 00:00 the NEXT day in Damascus (+03) and 17:00 the SAME day in New York (-04).
 * A formatter that leaked the ambient zone would therefore print a different calendar date, not
 * merely a different hour - which is why this instant was chosen.
 */
const LATE_EVENING_UTC = '2026-08-30T21:00:00Z'

describe('formatDate', () => {
  it('renders the Damascus calendar date regardless of the ambient zone', () => {
    expect(formatDate(LATE_EVENING_UTC, 'en')).toBe('31 Aug 2026')
  })

  /**
   * Reverses an earlier assertion. RESPONSIVE-AND-RTL §6.1 states Western digits as the default,
   * but every Arabic example in UX-WRITING.md renders Eastern ones - §3.1's «حجم هذا الملف ١٤
   * ميغابايت» and «٣٠ أغسطس ٢٠٢٦، الساعة ١٤:٠٠ (+٣)» - and the product owner ruled for the
   * examples. Both directions are asserted so a silent regression to `latn` fails here.
   */
  it('renders Eastern Arabic digits and Arabic month names under ar', () => {
    const out = formatDate(LATE_EVENING_UTC, 'ar')

    expect(out).toMatch(/[٠-٩]/)
    expect(out).toContain('٣١')
    expect(out).toContain('٢٠٢٦')
    expect(out).not.toMatch(/[0-9]/)
  })

  it('keeps Western digits under en', () => {
    expect(formatDate(LATE_EVENING_UTC, 'en')).not.toMatch(/[٠-٩]/)
  })

  it('returns an empty string for null/undefined/invalid rather than "Invalid Date"', () => {
    expect(formatDate(null, 'en')).toBe('')
    expect(formatDate(undefined, 'en')).toBe('')
    expect(formatDate('not a date', 'en')).toBe('')
  })
})

describe('formatDateTime', () => {
  it('renders Damascus wall-clock time, 24h', () => {
    expect(formatDateTime(LATE_EVENING_UTC, 'en')).toBe('31 Aug 2026, 00:00')
  })

  it('renders midday unambiguously', () => {
    expect(formatDateTime('2026-03-10T09:30:00Z', 'en')).toBe('10 Mar 2026, 12:30')
  })
})

describe('formatDeadline', () => {
  /**
   * The suffix is `(+03)`, not Intl's `shortOffset` ("GMT+3"). RESPONSIVE-AND-RTL §6.2 shows
   * *"30 Aug 2026, 14:00 (+03)"* and UX-WRITING §3.1 shows the same form in both languages, one of
   * them a copy specification - two documents agreeing on a shape is a format, not an illustration.
   */
  it('carries date + time + the documented (+03) suffix, per RESPONSIVE-AND-RTL §6.2', () => {
    expect(formatDeadline(LATE_EVENING_UTC, 'en')).toBe('31 Aug 2026, 00:00 (+03)')
  })

  /**
   * The two documents DISAGREE on zero-padding and each locale follows its own: §6.2's English is
   * `(+03)`, §3.1's Arabic is «(+٣)» - one digit, unpadded, Eastern. Preserved rather than
   * reconciled silently; reported as a documented conflict.
   */
  it('renders the Arabic suffix unpadded and in Eastern digits, as UX-WRITING §3.1 shows it', () => {
    const out = formatDeadline(LATE_EVENING_UTC, 'ar')

    expect(out).toContain('(+٣)')
    expect(out).not.toContain('(+03)')
    expect(out).not.toContain('(+٠٣)')
    expect(out).toContain('٠٠:٠٠')
  })

  it('never emits Intl\'s shortOffset form, in either locale', () => {
    expect(formatDeadline(LATE_EVENING_UTC, 'en')).not.toMatch(/GMT/)
    expect(formatDeadline(LATE_EVENING_UTC, 'ar')).not.toMatch(/GMT|غرينتش/)
  })
})

/**
 * Syria abolished seasonal clock changes in October 2022 and has observed UTC+03 year-round since.
 * There is therefore no present-day DST boundary to cross - this asserts the absence rather than
 * claiming a boundary test that cannot exist, and would fail if tzdata ever reintroduced one.
 * The 2021 case is kept as evidence the formatter reads real tzdata rather than a hardcoded +3.
 */
describe('Damascus DST', () => {
  it('has no seasonal shift in the current era - January and July are both +03', () => {
    expect(formatDeadline('2026-01-15T12:00:00Z', 'en')).toContain('(+03)')
    expect(formatDeadline('2026-07-15T12:00:00Z', 'en')).toContain('(+03)')
    expect(formatDateTime('2026-01-15T12:00:00Z', 'en')).toBe('15 Jan 2026, 15:00')
    expect(formatDateTime('2026-07-15T12:00:00Z', 'en')).toBe('15 Jul 2026, 15:00')
  })

  it('still honours the historic +02 winter offset from before the 2022 abolition', () => {
    expect(formatDateTime('2021-01-15T12:00:00Z', 'en')).toBe('15 Jan 2021, 14:00')
    expect(formatDateTime('2021-07-15T12:00:00Z', 'en')).toBe('15 Jul 2021, 15:00')
  })

  /**
   * The suffix is derived from tzdata, not hardcoded: the same formatter must print (+02) for a
   * 2021 winter date. A regex over Intl's output, or a constant, would both pass the 2026 cases
   * above and fail here.
   */
  it('derives the suffix from the zone, printing (+02) for a pre-2022 winter deadline', () => {
    expect(formatDeadline('2021-01-15T12:00:00Z', 'en')).toBe('15 Jan 2021, 14:00 (+02)')
    expect(formatDeadline('2021-01-15T12:00:00Z', 'ar')).toContain('(+٢)')
  })
})

describe('formatRelative', () => {
  const now = new Date('2026-08-30T12:00:00Z')

  it('counts forward in English', () => {
    expect(formatRelative('2026-09-02T12:00:00Z', 'en', now)).toBe('in 3 days')
  })

  it('counts backward in English', () => {
    expect(formatRelative('2026-08-30T10:00:00Z', 'en', now)).toBe('2 hours ago')
  })

  it('counts in Arabic, with Eastern digits', () => {
    const out = formatRelative('2026-09-02T12:00:00Z', 'ar', now)

    expect(out).not.toBe('')
    expect(out).not.toContain('in 3 days')
    expect(out).not.toMatch(/[0-9]/)
  })

  it('returns an empty string for a missing value', () => {
    expect(formatRelative(null, 'en', now)).toBe('')
  })
})

describe('formatCurrency', () => {
  // The product owner's ruling reversing R-1: money renders in Eastern Arabic digits like dates,
  // counts and quantities. Before this the two catalogue pages pinned `ar-SY-u-nu-latn`, so a
  // supplier saw Arabic layout with Western digits - the one place in the UI that disagreed.
  it('renders Eastern Arabic digits in Arabic', () => {
    const formatted = formatCurrency(1250, 'SYP', 'ar')

    expect(formatted).toMatch(/[٠-٩]/)
    expect(formatted).not.toMatch(/[0-9]/)
  })

  it('renders Western digits in English', () => {
    expect(formatCurrency(1250, 'SYP', 'en-GB')).toMatch(/1,250/)
  })

  it('falls back to a bare amount rather than throwing on an unknown currency code', () => {
    expect(formatCurrency(10, 'NOT-A-CODE', 'en-GB')).toBe('10')
  })

  it('is empty for a missing amount', () => {
    expect(formatCurrency(null, 'SYP', 'ar')).toBe('')
    expect(formatCurrency(undefined, 'SYP', 'ar')).toBe('')
  })
})
