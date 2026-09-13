// FR-DOC-009's required-versus-optional grouping.
//
// The requirement's other halves - state chips, expiry countdowns, localisation - were already built. Grouping was the
// missing one, and it was missing in a way that reads as complete: every document was on the page, isRequired was on the
// DTO, and nothing was broken. The supplier simply could not tell which documents blocked their submission without opening
// each one, which is the entire purpose of the grouping.
//
// The harness has no router by design, and OnboardingStepNav reads useRouterState for the current path, so that is stubbed
// rather than the whole route tree being wired up for a test about document grouping.
//
// Each document lands under the heading matching its required flag, asserted SCOPED TO EACH SECTION rather than against
// the page: a test that only checked both names appear somewhere would pass on the flat list this replaced, because the
// defect was never a missing document but a missing distinction.
//
// Required documents come before optional ones, asserted through document POSITION rather than array order - the API
// returned them the other way round in that test, so it fails if the grouping ever just renders what it was given. And
// required-first is correct in RTL for the same reason it is in LTR.
//
// With no optional documents the section SAYS so rather than hiding. With no required types the heading is omitted
// entirely, which is not an empty state: a required group with nothing in it means the document-type catalogue is
// misconfigured, and a reassuring "none required" would present a configuration fault as a finished checklist.
//
// The last test is the rejection reason's own render path, Task #21's - the --color-danger-fg fixed for dark-mode AA
// contrast in that task - which every other test in this file leaves unexercised, since they only seed a null latest
// document.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import { renderPage, mockFetch } from '../test/renderPage'
import type { DocumentTypeStatus } from '../api/documents'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    Link: 'a',
    useRouterState: () => '/onboarding',
  }
})

const { OnboardingPage } = await import('./OnboardingPage')

function documentType(code: string, isRequired: boolean): DocumentTypeStatus {
  return {
    documentTypeId: `id-${code}`,
    code,
    nameAr: code,
    nameEn: code,
    isRequired,
    expiryTracked: false,
    latestDocument: null,
  }
}

const supplier = {
  supplierCode: 'SUP-2026-000001',
  displayNameAr: 'شركة',
  displayNameEn: 'Grouping Demo Co',
  description: null, website: null, logoStorageKey: null, supplierGroup: null,
  onboardingState: 'ProfileInProgress',
  lifecycleState: 'Active',
  currencyCode: null, legalInfo: null, primaryContactPhone: null,
  representatives: [], addresses: [], contacts: [], branches: [], bankAccounts: [], categoryCodes: [],
}

function mount(documents: ReturnType<typeof documentType>[]) {
  return mockFetch({
    '/api/v1/suppliers/SUP-2026-000001/documents': documents,
    '/api/v1/suppliers/me/annotations/active': null,
    '/api/v1/suppliers/me': supplier,
    '/api/v1/currencies': [],
  })
}

describe('OnboardingPage document grouping', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('puts each document under the heading matching its required flag', async () => {
    restore = mount([
      documentType('commercial_registration', true),
      documentType('chamber_membership', false),
      documentType('tax_certificate', true),
    ])

    renderPage(<OnboardingPage />)

    const required = await screen.findByRole('heading', { name: 'Required documents' })
    const optional = await screen.findByRole('heading', { name: 'Optional documents' })

    const requiredSection = required.closest('section')!
    const optionalSection = optional.closest('section')!

    expect(within(requiredSection).getByText('commercial_registration')).toBeInTheDocument()
    expect(within(requiredSection).getByText('tax_certificate')).toBeInTheDocument()
    expect(within(requiredSection).queryByText('chamber_membership')).toBeNull()

    expect(within(optionalSection).getByText('chamber_membership')).toBeInTheDocument()
    expect(within(optionalSection).queryByText('tax_certificate')).toBeNull()
  })

  it('shows required documents before optional ones', async () => {
    restore = mount([
      documentType('chamber_membership', false),
      documentType('commercial_registration', true),
    ])

    renderPage(<OnboardingPage />)

    const required = await screen.findByRole('heading', { name: 'Required documents' })
    const optional = await screen.findByRole('heading', { name: 'Optional documents' })

    expect(required.compareDocumentPosition(optional))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING)
  })

  it('says so when there are no optional documents rather than hiding the section', async () => {
    restore = mount([documentType('commercial_registration', true)])

    renderPage(<OnboardingPage />)

    expect(await screen.findByText('No optional documents.')).toBeInTheDocument()
  })

  it('omits the required heading entirely when the catalogue has no required types', async () => {
    restore = mount([documentType('chamber_membership', false)])

    renderPage(<OnboardingPage />)

    await screen.findByRole('heading', { name: 'Optional documents' })
    await waitFor(() =>
      expect(screen.queryByRole('heading', { name: 'Required documents' })).toBeNull())
  })

  it('shows the rejection reason on a document that was rejected', async () => {
    const rejected = {
      ...documentType('commercial_registration', true),
      latestDocument: {
        documentId: 'doc-1', version: 1, state: 'Rejected', originalFileName: 'file.pdf', contentType: 'application/pdf',
        sizeBytes: 1024, issueDate: null, expiryDate: null, rejectReason: 'Illegible scan',
        uploadedAt: new Date().toISOString(), reviewedAt: new Date().toISOString(),
      },
    }
    restore = mount([rejected])

    renderPage(<OnboardingPage />)

    expect(await screen.findByText('Illegible scan')).toBeInTheDocument()
  })
})
