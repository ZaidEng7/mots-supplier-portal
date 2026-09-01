import { describe, expect, it } from 'vitest'
import { AT_RISK_HOURS, OVERDUE_HOURS, ageTone, formatAge } from './ReviewQueuePage'

/** FEAT-03.6/FR-ONB-012: the age-badge logic had zero test coverage before this - the backend
 * sourcing (EnteredQueueAt) is tested in ReviewQueuePaginationTests.cs, but the tone thresholds
 * and the AR/EN formatting that turn hours into what a reviewer actually reads were not. */
describe('ageTone', () => {
  it('is success below the at-risk threshold', () => {
    expect(ageTone(0)).toBe('success')
    expect(ageTone(AT_RISK_HOURS - 1)).toBe('success')
  })

  it('is warning at and above the at-risk threshold, below overdue', () => {
    expect(ageTone(AT_RISK_HOURS)).toBe('warning')
    expect(ageTone(OVERDUE_HOURS - 1)).toBe('warning')
  })

  it('is danger at and above the overdue threshold', () => {
    expect(ageTone(OVERDUE_HOURS)).toBe('danger')
    expect(ageTone(OVERDUE_HOURS + 1000)).toBe('danger')
  })
})

describe('formatAge', () => {
  it('renders whole hours below one day, in both locales', () => {
    expect(formatAge(5.9, false)).toBe('5h')
    expect(formatAge(5.9, true)).toBe('5 ساعة')
  })

  it('renders zero hours as 0h/0 ساعة rather than a negative or NaN value', () => {
    expect(formatAge(0, false)).toBe('0h')
    expect(formatAge(0, true)).toBe('0 ساعة')
  })

  it('clamps a negative input (clock skew) to zero rather than printing a negative age', () => {
    expect(formatAge(-2, false)).toBe('0h')
  })

  it('switches to whole days at 24h, in both locales', () => {
    expect(formatAge(24, false)).toBe('1d')
    expect(formatAge(24, true)).toBe('1 يوم')
    expect(formatAge(47.9, false)).toBe('1d')
    expect(formatAge(240, false)).toBe('10d')
  })
})
