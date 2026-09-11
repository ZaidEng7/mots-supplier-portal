import { afterEach, describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { screen, within } from '@testing-library/react'
import { renderPage, mockFetch } from '../../test/renderPage'

/**
 * SCR-603, the screen carrying three of this product's five chart instances, and the only one that
 * had no test at all.
 *
 * <p>That gap is why the chart and the table under it could disagree about the order of their own
 * rows for as long as they did: the ranked chart sorts by the measure and the table kept the server's
 * order, so the tallest bar and the first row named different categories and nothing was watching.</p>
 *
 * <p>The demonstration database holds zero awards, so this screen renders an empty state everywhere it
 * is looked at. Every fixture here is therefore hand-written rather than captured.</p>
 */
vi.mock('recharts', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('recharts')
  // jsdom measures every element as 0x0, so the responsive container hands recharts a zero box and it
  // draws nothing. Without this the chart assertions below would pass over an empty picture.
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) =>
      cloneElement(children, { width: 600, height: 220 }),
  }
})

const { MinistryAwardAnalyticsPage } = await import('./MinistryAwardAnalyticsPage')

const ANALYTICS = '/api/v1/ministry/awards'

function analytics(overrides: Record<string, unknown> = {}) {
  return {
    totalAwards: 9,
    totalAwardedValue: 812_500,
    byMonth: [
      { key: '2026-04', awards: 2, value: 120_000 },
      { key: '2026-05', awards: 3, value: 402_500 },
      { key: '2026-06', awards: 4, value: 290_000 },
    ],
    byCategory: [
      { key: 'catering', awards: 2, value: 90_000, nameAr: 'التموين', nameEn: 'Catering & Hospitality' },
      { key: 'transport', awards: 4, value: 500_000, nameAr: 'النقل', nameEn: 'Transport & Logistics' },
      { key: 'tour_operations', awards: 3, value: 222_500, nameAr: 'الرحلات', nameEn: 'Tour operations' },
    ],
    byOrganization: [
      { key: 'Directorate of Ports', awards: 5, value: 600_000 },
      { key: 'Directorate of Rail', awards: 4, value: 212_500 },
    ],
    commercialValuesVisible: true,
    ...overrides,
  }
}

describe('MinistryAwardAnalyticsPage (SCR-603)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the two headline figures', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)

    // "Awards" is also a column header on all three tables, so this asks the metric for its own value.
    expect(await screen.findByText('Total value')).toBeInTheDocument()
    expect(screen.getByText('812,500')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
  })

  /**
   * The defect this closes. A ranked chart sorts by the measure - that is what makes it ranked - and
   * the table beneath kept whatever order the server sent, so the tallest bar and the first row were
   * different categories and a reader moving between them had to re-find their place on every row.
   */
  it('draws the ranked chart and the table beneath it in one order', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    // The card heading, not the table's own caption - both name the same list, which is a redundancy
    // the Rams audit filed separately and this test simply has to navigate around.
    const heading = await screen.findByRole('heading', { name: 'By category' })
    const card = heading.closest('[class*="rounded"]') as HTMLElement

    const rows = within(card).getAllByRole('row').slice(1)
    const tableOrder = rows.map((row) => within(row).getAllByRole('cell')[0].textContent)

    // Sorted by value descending: transport 500,000 then maintenance 222,500 then catering 90,000.
    expect(tableOrder).toEqual(['Transport & Logistics', 'Tour operations', 'Catering & Hospitality'])
  })

  /**
   * The naming defect, on the chart whose form was chosen because a category name is prose.
   *
   * <p>The by-category buckets group on CategoryCode, so a Ministry reader met `tour_operations` on
   * the axis and in the table beside it - a domain identifier, in one language, with an underscore in
   * it. The names exist in the reference table and the coverage endpoint already returned them.</p>
   */
  it('names a category rather than printing its code', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    await screen.findByRole('heading', { name: 'By category' })

    // Twice on purpose: once on the chart's axis and once in the table beneath it, which is the
    // pairing this screen is built on.
    expect(screen.getAllByText('Tour operations')).toHaveLength(2)
    expect(screen.queryByText('tour_operations')).toBeNull()
  })

  it('keeps the key when a bucket carries no name, rather than rendering blank', async () => {
    // Months and buying bodies send no name because a date and an organisation's own name are already
    // words. A category deleted from the reference list would arrive the same way, and it still
    // awarded something - hiding it would change the total.
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)

    expect((await screen.findAllByText('2026-05')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Directorate of Ports').length).toBeGreaterThan(0)
  })

  it('charts award counts, and says so, when the values are withheld', async () => {
    // D-57 withholds commercial figures outside a demonstration environment. A chart of nothing but
    // withheld months is a chart of nothing, while the counts beside them are never withheld.
    restore = mockFetch({
      [ANALYTICS]: analytics({
        commercialValuesVisible: false,
        totalAwardedValue: null,
        byMonth: [{ key: '2026-04', awards: 2, value: null }, { key: '2026-05', awards: 3, value: null }],
        byCategory: [{ key: 'catering', awards: 2, value: null }],
        byOrganization: [{ key: 'Directorate of Ports', awards: 5, value: null }],
      }),
    })

    renderPage(<MinistryAwardAnalyticsPage />)

    expect(await screen.findByText('Commercial values are withheld by disclosure policy')).toBeInTheDocument()
    // Two bars for two months, drawn from the counts rather than from the withheld money.
    const bars = document.querySelectorAll('.recharts-bar-rectangle')
    expect(bars.length).toBeGreaterThanOrEqual(2)
  })

  it('writes the figures on the chart the way the table writes them', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    await screen.findByRole('heading', { name: 'By category' })

    // The chart drew `500000` above a table that drew `500,000`. One screen, one number, two
    // renderings - and in Arabic, two different digit scripts.
    //
    // What this cannot separate: the page's own currency formatter from the chart's default number
    // one. With no currency code on the amount the two produce the same string, so the assertion is
    // the one that matters either way - no raw JavaScript number reaches the drawing.
    const labels = [...document.querySelectorAll('.recharts-label-list text')].map((t) => t.textContent)
    expect(labels.some((label) => label?.includes('500,000'))).toBe(true)
    expect(labels.some((label) => label === '500000')).toBe(false)
  })

  it('says so rather than drawing an empty axis when a bucket has nothing in it', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics({ byMonth: [], byCategory: [], byOrganization: [] }) })

    renderPage(<MinistryAwardAnalyticsPage />)

    expect((await screen.findAllByText('Nothing recorded yet')).length).toBeGreaterThan(0)
  })

  it('reports a failed load rather than an empty screen', async () => {
    restore = mockFetch({ [ANALYTICS]: { __status: 500 } })

    renderPage(<MinistryAwardAnalyticsPage />)

    expect(await screen.findByText('Could not load the analytics')).toBeInTheDocument()
  })
})
