import { describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import { renderPage } from '../../test/renderPage'
import { CoverageChart } from './CoverageChart'
import { ThresholdTiles } from './ThresholdTiles'

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
    expect(screen.getByText('Approved')).toBeInTheDocument()
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
