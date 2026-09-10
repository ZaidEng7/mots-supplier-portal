import { describe, expect, it } from 'vitest'
import { segmentTooltipRow, tooltipRow } from './chartTooltip'

/**
 * What a hover readout says.
 *
 * <p>Tested here rather than through a rendered chart because recharts calls its formatter from inside
 * hover machinery that needs real element geometry, and jsdom reports none - so three inline copies of
 * this rule sat in the two chart components with nothing able to reach them. tests/e2e/charts.spec.ts
 * proves the tooltip appears and is legible in a browser; this proves what it writes.</p>
 */
describe('a tooltip row', () => {
  it('writes the figure the way the caller writes figures, then names it', () => {
    const row = tooltipRow((value) => `${value.toLocaleString('en-GB')} SYP`, 'Awarded value')

    expect(row(412000)).toEqual(['412,000 SYP', 'Awarded value'])
  })

  it('passes a non-numeric value through as its own text rather than as NaN', () => {
    // A tooltip can sit on any series, and recharts types the value as unknown. Coercing would put
    // the word "NaN" on screen, which is worse than the value it was hiding.
    const row = tooltipRow((value) => value.toFixed(2), 'Value')

    expect(row('withheld')).toEqual(['withheld', 'Value'])
    expect(row(null)).toEqual(['', 'Value'])
    expect(row(undefined)).toEqual(['', 'Value'])
  })

  it('does not round a figure the caller chose to keep', () => {
    const row = tooltipRow((value) => String(value), 'Value')

    expect(row(0)).toEqual(['0', 'Value'])
    expect(row(-5.5)).toEqual(['-5.5', 'Value'])
  })

})

describe('a tooltip row on a stacked pair', () => {
  const row = segmentTooltipRow(
    (value) => value.toLocaleString('en-GB'),
    (segment) => (segment === 'covered' ? 'Can trade today' : 'Approved, cannot trade today'),
  )

  it('names the segment the pointer is actually over', () => {
    expect(row(14, 'covered')).toEqual(['14', 'Can trade today'])
    expect(row(4, 'remainder')).toEqual(['4', 'Approved, cannot trade today'])
  })

  it('still writes the figure the way the caller writes figures', () => {
    expect(row(12000, 'covered')).toEqual(['12,000', 'Can trade today'])
  })
})
