import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderPage, mockFetch } from '../../test/renderPage'

const { OfferingSearchPage } = await import('./OfferingSearchPage')

/** FEAT-06.3/FR-OFF-004: procurement staff's buyer-facing offering search - zero coverage before
 * this, since the endpoint (and this page) are new for EPIC-06's discoverability requirement. The
 * lifecycle-gating and row-scoping itself is proven server-side in OfferingBuyerSearchTests.cs;
 * this covers the page actually rendering what the endpoint returns, attributes included. */
describe('OfferingSearchPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('renders offerings returned by the search endpoint, including their attributes and supplier name', async () => {
    restore = mockFetch({
      '/api/v1/reference/categories': [{ id: '1', code: 'tour_operations', nameAr: 'سياحة', nameEn: 'Tourism' }],
      '/api/v1/offerings/search': [{
        id: 'off-1',
        supplierReferenceCode: 'SUP-2026-000001',
        supplierDisplayNameAr: 'شركة اختبار',
        supplierDisplayNameEn: 'Active Co',
        nameAr: 'جولة', nameEn: 'City Tour', description: null,
        categoryCode: 'tour_operations', unitOfMeasureCode: 'trip',
        priceAmount: 45.5, currencyCode: 'USD',
        attributes: { capacity: '50 guests' },
      }],
    })

    renderPage(<OfferingSearchPage />)

    expect(await screen.findByText('City Tour')).toBeInTheDocument()
    expect(screen.getByText('Active Co')).toBeInTheDocument()
    expect(screen.getByText('Tourism')).toBeInTheDocument()
    expect(screen.getByText('capacity: 50 guests')).toBeInTheDocument()
  })

  it('shows the empty state when no offerings match', async () => {
    restore = mockFetch({
      '/api/v1/reference/categories': [],
      '/api/v1/offerings/search': [],
    })

    renderPage(<OfferingSearchPage />)

    expect(await screen.findByText('No results')).toBeInTheDocument()
  })
})
