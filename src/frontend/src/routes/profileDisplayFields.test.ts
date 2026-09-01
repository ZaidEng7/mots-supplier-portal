import { describe, expect, it } from 'vitest'
import { PROFILE_DISPLAY_FIELDS, profileDisplayValue, LEGAL_INFO_FIELDS, legalInfoValue } from './profileDisplayFields'
import type { SupplierProfile } from '../api/supplier'

/** Injects a value TypeScript's static type says cannot occur, for exactly one field, so a test can
 * simulate real-world API/type drift without casting an entire fixture object and losing compiler
 * coverage on every other field (Task #10). Narrower and more honest than `as unknown as T`: the
 * unsafety is visible at the call site (`asRuntimeValue(undefined)`), not laundered through a type
 * name that implies the whole object was checked. */
function asRuntimeValue<T>(value: unknown): T {
  return value as T
}

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
 *
 * Task #10: this file's own fixtures repeated exactly that pattern - `as unknown as SupplierProfile`
 * on a literal that was missing 4 required fields (missingProfileFields, termsAcceptedVersion,
 * termsAcceptedAt, rowVersion), added to the DTO after this fixture was written and silently never
 * caught because the cast told the compiler to stop checking. Typed directly (`: SupplierProfile`)
 * below instead, so a future field added to the real DTO fails HERE at compile time rather than
 * leaving the fixture quietly out of sync with what the API actually returns.
 */
describe('reviewer profile display fields', () => {
  const supplier: SupplierProfile = {
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
    missingProfileFields: [],
    termsAcceptedVersion: null,
    termsAcceptedAt: null,
    rowVersion: 1,
  }

  it('renders every display field as a string, never an object', () => {
    for (const field of PROFILE_DISPLAY_FIELDS) {
      const value = profileDisplayValue(supplier, field)
      expect(typeof value).toBe('string')
    }
  })

  it('does not include any field whose DTO value is an object', () => {
    // The direct assertion of the bug. If someone re-adds `legalInfo` (or any other non-scalar
    // code) to the display list, this fails here rather than in a browser in front of a reviewer.
    // No cast needed: PROFILE_DISPLAY_FIELDS is `satisfies readonly (keyof SupplierProfile)[]`
    // (profileDisplayFields.ts), so `field` is already a real key of SupplierProfile and indexes
    // directly.
    for (const field of PROFILE_DISPLAY_FIELDS) {
      const raw = supplier[field]
      expect(typeof raw).not.toBe('object')
    }
  })

  it('shows a dash for absent values rather than "null" or "undefined"', () => {
    // `description: null` matches the real DTO type (`string | null`) and needs no assertion.
    // `website: undefined` does not - SupplierProfile declares `string | null`, never undefined -
    // and is deliberately out of type: it simulates a field the backend DROPPED from its actual
    // JSON response, something TypeScript's static type cannot rule out at runtime (the exact risk
    // class the reviewer-page bug above was). asRuntimeValue<T> exists so that one adversarial value
    // is visibly, narrowly injected here, rather than casting the whole `sparse` object and losing
    // compiler coverage on every OTHER field the way the old `as unknown as SupplierProfile` did.
    const sparse: SupplierProfile = { ...supplier, description: null, website: asRuntimeValue(undefined) }

    expect(profileDisplayValue(sparse, 'description')).toBe('—')
    expect(profileDisplayValue(sparse, 'website')).toBe('—')
  })

  it('renders a populated scalar as its value', () => {
    expect(profileDisplayValue(supplier, 'supplierGroup')).toBe('SME')
    expect(profileDisplayValue(supplier, 'primaryContactPhone')).toBe('+963900000000')
  })
})

/**
 * Task #33: legalInfo was the object whose direct rendering caused the MSP-77 crash. Restoring it
 * to the review page (via legalInfoValue) must go through the same never-render-the-object
 * discipline as profileDisplayValue above, so this mirrors that test shape one level down: every
 * one of legalInfo's OWN fields is a scalar, read individually, never the object itself.
 */
describe('reviewer legal info fields', () => {
  it('renders every legal info field as a string, never an object', () => {
    const legalInfo = {
      legalNameAr: 'شركة',
      legalNameEn: 'Lifecycle Demo Co',
      registrationNumber: 'CR-1',
      taxId: null,
      supplierType: 'Company',
      establishedOn: null,
    }
    for (const field of LEGAL_INFO_FIELDS) {
      expect(typeof legalInfoValue(legalInfo, field)).toBe('string')
    }
  })

  it('shows a dash for null fields', () => {
    const legalInfo = {
      legalNameAr: 'شركة',
      legalNameEn: 'Lifecycle Demo Co',
      registrationNumber: null,
      taxId: null,
      supplierType: 'Company',
      establishedOn: null,
    }
    expect(legalInfoValue(legalInfo, 'registrationNumber')).toBe('—')
    expect(legalInfoValue(legalInfo, 'taxId')).toBe('—')
  })

  it('renders a populated field as its value', () => {
    const legalInfo = {
      legalNameAr: 'شركة',
      legalNameEn: 'Lifecycle Demo Co',
      registrationNumber: 'CR-1',
      taxId: 'TAX-1',
      supplierType: 'Company',
      establishedOn: null,
    }
    expect(legalInfoValue(legalInfo, 'legalNameEn')).toBe('Lifecycle Demo Co')
    expect(legalInfoValue(legalInfo, 'registrationNumber')).toBe('CR-1')
  })
})
