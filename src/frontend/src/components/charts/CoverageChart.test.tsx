import { describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { screen, within } from '@testing-library/react'
import { renderPage } from '../../test/renderPage'
import { CoverageChart } from './CoverageChart'
import { ThresholdTiles } from './ThresholdTiles'

/**
 * Every assertion in this file about the DRAWING used to be vacuous.
 *
 * <p>recharts measures its container and jsdom reports every element as 0x0, so `ResponsiveContainer`
 * handed its child a width and height of zero and recharts drew nothing at all - no bars, no labels, no
 * axis. The suite passed because it only ever asserted things that live OUTSIDE the SVG: the legend,
 * the empty-state sentence, the `aria-hidden` wrapper. A test that renders an empty chart and asserts
 * the legend beside it is a test of the legend.</p>
 *
 * <p>This is the same stub BarChart.test.tsx has always used. With it, the bars exist and can be
 * counted.</p>
 */
vi.mock('recharts', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('recharts')
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) =>
      cloneElement(children, { width: 600, height: 220 }),
  }
})

const CATEGORIES = [
  { key: 'catering', label: 'Catering', total: 14, covered: 12 },
  { key: 'tours', label: 'Tour operations', total: 1, covered: 0 },
]

/**
 * Approved suppliers against the ones who can actually bid. Both figures were already in the coverage
 * table, two columns apart, and the gap between them is what the screen exists to show.
 */
describe('CoverageChart', () => {
  it('names both series in text, so identity never rests on colour', () => {
    renderPage(<CoverageChart data={CATEGORIES} />)

    expect(screen.getByText('Can trade today')).toBeInTheDocument()
    // Not "Approved". The whole bar is the approved pool, so naming one segment "Approved" said the
    // other one was not - the opposite of what the two segments mean.
    expect(screen.getByText('Approved, cannot trade today')).toBeInTheDocument()
  })

  /**
   * The legend has to survive the `aria-hidden` on the drawing. Recharts' own legend lives inside the
   * SVG, which is hidden from a screen reader by design here - so a legend rendered there would be a
   * legend only sighted readers get, which is the case a legend exists for.
   */
  it('puts the legend outside the part hidden from assistive technology', () => {
    const { container } = renderPage(<CoverageChart data={CATEGORIES} />)

    const legend = screen.getByText('Can trade today')
    expect(legend.closest('[aria-hidden="true"]')).toBeNull()
    expect(container.querySelector('[aria-hidden="true"]')).not.toBeNull()
  })

  it('says so rather than drawing an empty axis when there is nothing to plot', () => {
    renderPage(<CoverageChart data={[]} />)

    expect(screen.getByText('No figures available to chart. The table below carries what there is.')).toBeInTheDocument()
  })

  /**
   * The denominator: one stack per category, and both of its segments present. Two bars per row for a
   * row with a remainder, one for a row without - which is also the assertion that the chart is being
   * drawn at all, the thing this suite could not previously tell.
   */
  it.each([
    [1, 1],
    [3, 3],
    [7, 7],
  ])('draws one stack per category, at %i categories', (count) => {
    const data = Array.from({ length: count }, (_, i) => ({
      key: `c${i}`, label: `Category ${i}`, total: 10, covered: 6,
    }))
    const { container } = renderPage(<CoverageChart data={data} />)

    // Two segments each, because every row here has somebody suspended.
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(count * 2)
  })

  /**
   * The row this screen is opened to find, and the row that had no figures on it.
   *
   * <p>The readout hung off the remainder segment alone. A category with nobody suspended has a
   * remainder of zero, so recharts drew no rectangle and no label with it - on the Ministry's own data
   * that was four rows of six showing no numbers at all, which reads as missing data rather than as a
   * full pool. The fixture here contains both kinds of row, which the old one did not.</p>
   */
  it('writes the readout on every row, whether or not anybody is suspended', () => {
    const { container } = renderPage(
      <CoverageChart
        data={[
          { key: 'gap', label: 'Has a gap', total: 18, covered: 14 },
          { key: 'full', label: 'Fully covered', total: 11, covered: 11 },
          { key: 'none', label: 'Nobody can trade', total: 2, covered: 0 },
        ]}
      />,
    )

    const readouts = [...container.querySelectorAll('.recharts-label-list text')]
      .map((t) => t.textContent)
      .filter((t) => t && t.trim() !== '')

    expect(readouts).toHaveLength(3)
    expect(readouts).toContain('14 of 18')
    expect(readouts).toContain('11 of 11')
    expect(readouts).toContain('0 of 2')
  })

  it('draws the two segments in the two chart tokens, and no other colour', () => {
    const { container } = renderPage(<CoverageChart data={CATEGORIES} />)
    const fills = new Set(
      [...container.querySelectorAll('.recharts-bar-rectangle .recharts-rectangle')]
        .map((r) => r.getAttribute('fill')),
    )

    expect(fills).toEqual(new Set(['var(--color-chart-fill)', 'var(--color-chart-fill-muted)']))
  })
})

/**
 * How a queue is doing against its own thresholds. The ageing was already on the review screen, one
 * badge per row, so "how far behind am I" meant reading every row and counting.
 */
describe('ThresholdTiles', () => {
  const TILES = [
    { key: 'ok', count: 14, label: 'Within target', tone: 'good' as const },
    { key: 'risk', count: 5, label: 'At risk', tone: 'warning' as const },
    { key: 'late', count: 2, label: 'Overdue', tone: 'critical' as const },
  ]

  it('names its counts, and groups them under one label', () => {
    renderPage(<ThresholdTiles tiles={TILES} label="Applications waiting" />)

    const group = screen.getByRole('list', { name: 'Applications waiting' })
    expect(within(group).getAllByRole('listitem')).toHaveLength(3)
    expect(within(group).getByText('14')).toBeInTheDocument()
    expect(within(group).getByText('Overdue')).toBeInTheDocument()
  })

  /**
   * The denominator for "not colour alone". Three tiles that differed only in hue would satisfy every
   * assertion above and be unreadable to the readers who most need "overdue" to stand out, so this
   * asserts the three glyphs are three different shapes.
   */
  it('separates the three states by shape as well as by colour', () => {
    const { container } = renderPage(<ThresholdTiles tiles={TILES} label="Applications waiting" />)

    const glyphs = [...container.querySelectorAll('[aria-hidden="true"]')].map((el) => el.textContent)
    expect(new Set(glyphs).size).toBe(3)
  })
})
