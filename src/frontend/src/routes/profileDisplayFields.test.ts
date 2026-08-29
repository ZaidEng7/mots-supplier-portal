import { describe, expect, it } from 'vitest'
import { PROFILE_DISPLAY_FIELDS, profileDisplayValue } from './profileDisplayFields'
import type { SupplierProfile } from '../api/supplier'

/**
 * Regression test for the reviewer profile grid (MSP-63 follow-up).
 *
 * MSP-77 replaced this grid's field list with the flagged-field CODE catalog. `legalInfo` is a code
 * in that catalog and an OBJECT on the DTO, so React threw "Objects are not valid as a React child"
 * and the reviewer page returned 500 for every supplier with a populated LegalInfo - which is every
 * registered supplier, since Register creates one.
 *
 * It was invisible for two reasons worth keeping in mind. The vocabularies overlap on the first
 * five fields, so the grid rendered correctly right up until the sixth row. And the access went
 * through `as unknown as Record<string, string | null>`, an assertion that the DTO has a shape it
 * does not - which is what stopped TypeScript reporting it. A type assertion silences the compiler
 * the way a false comment silences a reader.
 */
describe('reviewer profile display fields', () => {
  const supplier = {
    referenceCode: 'SUP-2026-000038',
    displayNameAr: 'شركة',
    displayNameEn: 'Lifecycle Demo Co',
    description: 'A tourism supplier',
    website: 'https://example.com',
    logoStorageKey: null,
    supplierGroup: 'SME',
    onboardingState: 'Approved',
    lifecycleState: 'Active',
    currencyCode: 'SYP',
    // The field that caused the crash: an object, present on every registered supplier.
    legalInfo: {
      legalNameAr: 'شركة',
      legalNameEn: 'Lifecycle Demo Co',
      registrationNumber: 'CR-1',
      taxId: 'TAX-1',
      supplierType: 'Company',
      establishedOn: null,
    },
    primaryContactPhone: '+963900000000',
    representatives: [],
    addresses: [],
    contacts: [],
    branches: [],
    bankAccounts: [],
    categoryCodes: [],
  } as unknown as SupplierProfile

  it('renders every display field as a string, never an object', () => {
    for (const field of PROFILE_DISPLAY_FIELDS) {
      const value = profileDisplayValue(supplier, field)
      expect(typeof value).toBe('string')
    }
  })

  it('does not include any field whose DTO value is an object', () => {
    // The direct assertion of the bug. If someone re-adds `legalInfo` (or any other non-scalar
    // code) to the display list, this fails here rather than in a browser in front of a reviewer.
    for (const field of PROFILE_DISPLAY_FIELDS) {
      const raw = (supplier as unknown as Record<string, unknown>)[field]
      expect(typeof raw).not.toBe('object')
    }
  })

  it('shows a dash for absent values rather than "null" or "undefined"', () => {
    const sparse = { ...supplier, description: null, website: undefined } as unknown as SupplierProfile

    expect(profileDisplayValue(sparse, 'description')).toBe('—')
    expect(profileDisplayValue(sparse, 'website')).toBe('—')
  })

  it('renders a populated scalar as its value', () => {
    expect(profileDisplayValue(supplier, 'supplierGroup')).toBe('SME')
    expect(profileDisplayValue(supplier, 'primaryContactPhone')).toBe('+963900000000')
  })
})
