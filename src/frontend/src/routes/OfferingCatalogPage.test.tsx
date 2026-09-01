import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../test/renderPage'
import { toPayload, attributesToRows } from './OfferingCatalogPage'

const { OfferingCatalogPage } = await import('./OfferingCatalogPage')

/** FEAT-06.1/FEAT-06.2: the CRUD UI had zero test coverage before this - the backend CRUD is
 * tested in OfferingTests.cs, but nothing exercised the form, the create/edit/deactivate mutation
 * flows, or the FEAT-06.2 flexible-attribute editor's round-trip logic. */
describe('toPayload', () => {
  const base = {
    nameAr: 'خدمة', nameEn: 'Service', description: '', categoryCode: 'tour_operations',
    unitOfMeasureCode: 'trip', priceAmount: '', currencyCode: '',
  }

  it('drops attribute rows with an empty key rather than rejecting the save', () => {
    const payload = toPayload({ ...base, attributes: [{ key: 'capacity', value: '50' }, { key: '', value: 'ignored' }] })
    expect(payload.attributes).toEqual({ capacity: '50' })
  })

  it('sends null, not an empty object, when there are no attribute rows', () => {
    const payload = toPayload({ ...base, attributes: [] })
    expect(payload.attributes).toBeNull()
  })

  it('trims whitespace from attribute keys', () => {
    const payload = toPayload({ ...base, attributes: [{ key: '  capacity  ', value: '50' }] })
    expect(payload.attributes).toEqual({ capacity: '50' })
  })
})

describe('attributesToRows', () => {
  it('converts a null/undefined attributes map to an empty row list', () => {
    expect(attributesToRows(null)).toEqual([])
    expect(attributesToRows(undefined)).toEqual([])
  })

  it('converts an attributes map to key/value rows', () => {
    expect(attributesToRows({ capacity: '50 guests' })).toEqual([{ key: 'capacity', value: '50 guests' }])
  })
})

describe('OfferingCatalogPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('creates an offering with a flexible attribute round-tripped through the form', async () => {
    restore = mockFetch({
      '/api/v1/reference/categories': [{ id: '1', code: 'tour_operations', nameAr: 'سياحة', nameEn: 'Tourism' }],
      '/api/v1/reference/units-of-measure': [{ id: '1', code: 'trip', nameAr: 'رحلة', nameEn: 'Trip' }],
      '/api/v1/reference/currencies': [{ id: '1', code: 'USD', nameAr: 'دولار', nameEn: 'USD' }],
      '/api/v1/suppliers/me/offerings': [{ id: 'off-1', nameAr: 'جولة', nameEn: 'City Tour', description: null, categoryCode: 'tour_operations', unitOfMeasureCode: 'trip', priceAmount: null, currencyCode: null, isActive: true, attributes: { capacity: '50 guests' } }],
    })

    renderPage(<OfferingCatalogPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Add offering' }))
    const dialog = await screen.findByRole('dialog')

    await userEvent.type(within(dialog).getByLabelText('Name (Arabic)', { exact: false }), 'جولة')
    await userEvent.type(within(dialog).getByLabelText('Name (English)', { exact: false }), 'City Tour')
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Category' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Tourism' }))
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Unit of measure' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Trip' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add attribute' }))
    await userEvent.type(within(dialog).getByLabelText('Attribute'), 'capacity')
    await userEvent.type(within(dialog).getByLabelText('Value'), '50 guests')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Offering created')).toBeInTheDocument()
  })

  it('shows a success toast once an offering is deactivated', async () => {
    restore = mockFetch({
      '/api/v1/reference/categories': [{ id: '1', code: 'tour_operations', nameAr: 'سياحة', nameEn: 'Tourism' }],
      '/api/v1/reference/units-of-measure': [{ id: '1', code: 'trip', nameAr: 'رحلة', nameEn: 'Trip' }],
      '/api/v1/reference/currencies': [],
      '/api/v1/suppliers/me/offerings/off-1/deactivate': { id: 'off-1', nameAr: 'جولة', nameEn: 'City Tour', description: null, categoryCode: 'tour_operations', unitOfMeasureCode: 'trip', priceAmount: null, currencyCode: null, isActive: false, attributes: null },
      '/api/v1/suppliers/me/offerings': [{ id: 'off-1', nameAr: 'جولة', nameEn: 'City Tour', description: null, categoryCode: 'tour_operations', unitOfMeasureCode: 'trip', priceAmount: null, currencyCode: null, isActive: true, attributes: null }],
    })

    renderPage(<OfferingCatalogPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByText('Offering deactivated')).toBeInTheDocument()
  })
})
