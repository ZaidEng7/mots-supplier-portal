import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { mockFetch, renderPage } from '../../test/renderPage'
import { CategoryCoveragePage } from './CategoryCoveragePage'

/**
 * SCR-604. The screen exists to show the categories the market is NOT serving, so the tests are mostly
 * about the empty rows: a coverage list that quietly omitted them would look complete and answer a
 * different question.
 */

const COVERAGE = '/api/v1/ministry/categories'

const COVERED = {
  categoryCode: 'catering', nameAr: 'تغذية', nameEn: 'Catering',
  approvedSuppliers: 4, activeSuppliers: 3, activeOfferings: 12, tenders: 5, awardedTenders: 4,
}

const UNCOVERED = {
  categoryCode: 'medical', nameAr: 'مستلزمات طبية', nameEn: 'Medical supplies',
  approvedSuppliers: 0, activeSuppliers: 0, activeOfferings: 0, tenders: 0, awardedTenders: 0,
}

const SUSPENDED_ONLY = {
  categoryCode: 'transport', nameAr: 'نقل', nameEn: 'Transport',
  approvedSuppliers: 2, activeSuppliers: 0, activeOfferings: 3, tenders: 2, awardedTenders: 0,
}

function body(overrides: Record<string, unknown> = {}) {
  return {
    categories: [COVERED, UNCOVERED, SUSPENDED_ONLY],
    categoriesWithNoActiveSupplier: 2,
    categoriesAreFlat: true,
    ...overrides,
  }
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('CategoryCoveragePage', () => {
  it('leads with the number of categories nobody can currently serve', async () => {
    restore = mockFetch({ [COVERAGE]: body() })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('2 of 3 categories have no active supplier')).toBeInTheDocument()
  })

  it('lists a category with no suppliers, flagged, instead of leaving it out', async () => {
    restore = mockFetch({ [COVERAGE]: body() })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('Medical supplies')).toBeInTheDocument()
    expect(screen.getAllByText('No supplier').length).toBeGreaterThan(0)
  })

  it('flags a category whose suppliers are all suspended, which one number would hide', async () => {
    // approvedSuppliers 2, activeSuppliers 0: the pool exists on paper and cannot bid today. A screen
    // carrying only the first number would show this category as covered.
    restore = mockFetch({ [COVERAGE]: body({ categories: [SUSPENDED_ONLY], categoriesWithNoActiveSupplier: 1 }) })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('Transport')).toBeInTheDocument()
    // The two numbers on the row, read from the cells rather than by text: "2" appears twice on this
    // fixture (approved suppliers and tenders), which is the whole reason the columns are separate.
    const cells = screen.getAllByRole('cell').map((cell) => cell.textContent)
    expect(cells[1]).toBe('2')
    expect(cells[2]).toBe('0')
    expect(screen.getByText('No supplier')).toBeInTheDocument()
  })

  it('flags a category that was tendered and never awarded', async () => {
    restore = mockFetch({ [COVERAGE]: body({ categories: [SUSPENDED_ONLY], categoriesWithNoActiveSupplier: 1 }) })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('No award')).toBeInTheDocument()
  })

  it('says the list is flat rather than drawing a hierarchy nobody has decided', async () => {
    // SCR-604's inventory row says "category tree"; MSP-54's list is flat by design. The screen reports
    // that instead of inventing the tree.
    restore = mockFetch({ [COVERAGE]: body() })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('The category list is currently flat and carries no hierarchy.')).toBeInTheDocument()
  })

  it('offers a retry when the read fails, rather than an empty table', async () => {
    restore = mockFetch({ [COVERAGE]: { __status: 500 } })

    renderPage(<CategoryCoveragePage />)

    expect(await screen.findByText('Could not load category coverage')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })
})
