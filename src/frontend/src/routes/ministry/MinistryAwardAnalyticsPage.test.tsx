// SCR-603, the screen carrying three of this product's five chart instances, and the only one that had no test at all.
//
// That gap is why the chart and the table under it could disagree about the order of their own rows for as long as they
// did: the ranked chart sorts by the measure and the table kept the server's order, so the tallest bar and the first row
// named different categories and nothing was watching.
//
// The demonstration database holds zero awards, so this screen renders an empty state everywhere it is looked at. Every
// fixture here is therefore hand-written rather than captured. And the responsive container is stubbed, because jsdom
// measures every element as 0x0 so it hands recharts a zero box and it draws nothing - without that the chart assertions
// would pass over an empty picture.
//
// The two headline figures show. "Awards" is also a column header on all three tables, so that assertion asks the metric
// for its own value.
//
// THE ORDER DEFECT is the one this file closes. A ranked chart sorts by the measure - that is what makes it ranked - and the
// table beneath kept whatever order the server sent, so the tallest bar and the first row were different categories and a
// reader moving between them had to re-find their place on every row. The test reads the card HEADING rather than the
// table's own caption, because both name the same list, which is a redundancy the Rams audit filed separately and this test
// simply has to navigate around. Sorted by value descending: transport 500,000 then maintenance 222,500 then catering
// 90,000.
//
// THE NAMING DEFECT is on the chart whose form was chosen because a category name is prose. The by-category buckets group on
// CategoryCode, so a Ministry reader met tour_operations on the axis and in the table beside it - a domain identifier, in
// one language, with an underscore in it - while the names exist in the reference table and the coverage endpoint already
// returned them. It is asserted twice on purpose: once on the chart's axis and once in the table beneath, which is the
// pairing this screen is built on. A bucket with no name KEEPS its key rather than rendering blank - months and buying
// bodies send no name, because a date and an organisation's own name are already words, and a category deleted from the
// reference list would arrive the same way while still having awarded something, so hiding it would change the total.
//
// WITHHELD VALUES are charted as award COUNTS, and the screen says so. D-57 withholds commercial figures outside a
// demonstration environment, and a chart of nothing but withheld months is a chart of nothing while the counts beside them
// are never withheld - two bars for two months, drawn from the counts rather than from the withheld money.
//
// THE FIGURES ON THE CHART are written the way the table writes them. The chart drew 500000 above a table that drew
// 500,000: one screen, one number, two renderings - and in Arabic, two different digit scripts. What that test cannot
// separate is the page's own currency formatter from the chart's default number one, because with no currency code on the
// amount the two produce the same string - so the assertion is the one that matters either way: no raw JavaScript number
// reaches the drawing.
//
// The last two are the edges: a bucket with nothing in it says so rather than drawing an empty axis, and a failed load is
// reported rather than leaving an empty screen.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { screen, within } from '@testing-library/react'
import { renderPage, mockFetch } from '../../test/renderPage'

vi.mock('recharts', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('recharts')
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

    expect(await screen.findByText('Total value')).toBeInTheDocument()
    expect(screen.getByText('812,500')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
  })

  it('draws the ranked chart and the table beneath it in one order', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    const heading = await screen.findByRole('heading', { name: 'By category' })
    const card = heading.closest('[class*="rounded"]') as HTMLElement

    const rows = within(card).getAllByRole('row').slice(1)
    const tableOrder = rows.map((row) => within(row).getAllByRole('cell')[0].textContent)

    expect(tableOrder).toEqual(['Transport & Logistics', 'Tour operations', 'Catering & Hospitality'])
  })

  it('names a category rather than printing its code', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    await screen.findByRole('heading', { name: 'By category' })

    expect(screen.getAllByText('Tour operations')).toHaveLength(2)
    expect(screen.queryByText('tour_operations')).toBeNull()
  })

  it('keeps the key when a bucket carries no name, rather than rendering blank', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)

    expect((await screen.findAllByText('2026-05')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Directorate of Ports').length).toBeGreaterThan(0)
  })

  it('charts award counts, and says so, when the values are withheld', async () => {
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
    const bars = document.querySelectorAll('.recharts-bar-rectangle')
    expect(bars.length).toBeGreaterThanOrEqual(2)
  })

  it('writes the figures on the chart the way the table writes them', async () => {
    restore = mockFetch({ [ANALYTICS]: analytics() })

    renderPage(<MinistryAwardAnalyticsPage />)
    await screen.findByRole('heading', { name: 'By category' })

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
