import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'
import type { Rfq, RfqState } from '../../api/rfqs'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { RfqDetailPage } = await import('./RfqDetailPage')

function rfqFixture(state: RfqState, overrides: Partial<Rfq> = {}): Rfq {
  return {
    referenceCode: 'RFQ-2026-000001', organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
    descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state,
    publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
    items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
    ...overrides,
  }
}

const REFERENCE_ROUTES = {
  // Declared before any '/api/v1/rfqs/{ref}' base route in every merged mockFetch call below -
  // mockFetch (renderPage.tsx) matches by first-declared substring, and this candidates path is a
  // suffix of the base RFQ route, so it must win the match or the base RFQ fixture object would be
  // returned here instead (breaking candidates.filter()).
  '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [],
  '/api/v1/reference/categories': [{ code: 'consulting', nameAr: 'استشارات', nameEn: 'Consulting' }],
  '/api/v1/reference/units-of-measure': [{ code: 'each', nameAr: 'وحدة', nameEn: 'Each' }],
  '/api/v1/evaluation-templates': [{ id: 'tpl-1', familyId: 'fam-1', version: 2, nameAr: 'قالب', nameEn: 'Standard', status: 'Active', isReferenced: false, criteria: [] }],
}

/** FEAT-07.1..07.10: this is the state-gated workspace. mockFetch (renderPage.tsx) answers by URL
 * substring, and every RFQ mutation URL is a suffix of the base `/api/v1/rfqs/{ref}` GET route, so
 * one declared base route serves reads and every write for a given test. */
describe('RfqDetailPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('Draft: shows editable item/requirement/template-bind controls, and adding an item succeeds', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByRole('button', { name: 'Submit for review' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add item' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Bind template' })).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Title (English)'), 'Cleaning supplies')
    await userEvent.type(screen.getByLabelText('Title (Arabic)'), 'مستلزمات تنظيف')
    await userEvent.click(screen.getByRole('button', { name: 'Add item' }))

    expect(await screen.findByText('Item added')).toBeInTheDocument()
  })

  it('Published: an existing item is shown but item-edit controls are gone (state-gated editing)', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published', {
        items: [{ id: 'item-1', lineNo: 1, titleAr: 'أ', titleEn: 'Widget', specificationAr: null, specificationEn: null, categoryCode: 'consulting', quantity: 5, unitOfMeasureCode: 'each', isUnitPrice: true, isOptional: false }],
      }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Widget')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add item' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bind template' })).not.toBeInTheDocument()
  })

  it('binding an evaluation template shows a success toast', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: 'Evaluation template' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Standard (v2)' }))
    await userEvent.click(screen.getByRole('button', { name: 'Bind template' }))

    expect(await screen.findByText('Evaluation template bound')).toBeInTheDocument()
  })

  it.each([
    ['Draft' as const, 'Submit for review', 'RFQ submitted for review'],
    ['InternalReview' as const, 'Approve', 'RFQ approved'],
    ['Approved' as const, 'Publish', 'RFQ published'],
  ])('%s: clicking the primary action calls its own transition and surfaces the right toast', async (state, buttonName, toastText) => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture(state) })

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: buttonName }))

    expect(await screen.findByText(toastText)).toBeInTheDocument()
  })

  it('cancel requires a reason before it can be submitted, then shows a success toast', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    const cancelButton = await screen.findByRole('button', { name: 'Cancel RFQ' })
    expect(cancelButton).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason'), 'Budget withdrawn')
    await waitFor(() => expect(cancelButton).toBeEnabled())
    await userEvent.click(cancelButton)

    expect(await screen.findByText('RFQ cancelled')).toBeInTheDocument()
  })

  it('hides the cancel section once the RFQ is Cancelled', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Cancelled') })

    renderPage(<RfqDetailPage />)

    await screen.findByText('Cancelled')
    expect(screen.queryByRole('button', { name: 'Cancel RFQ' })).not.toBeInTheDocument()
  })

  it('Draft: shows suggested candidates and inviting one shows a success toast', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [
        { supplierId: 'sup-1', displayNameAr: 'مورد', displayNameEn: 'Candidate Co', matchCount: 2 },
      ],
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/Candidate Co/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Invite' }))

    expect(await screen.findByText('Supplier invited')).toBeInTheDocument()
  })

  it('lists existing invitations with supplier name and status', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', {
        invitations: [
          { id: 'inv-1', supplierId: 'sup-1', supplierDisplayNameAr: 'مورد', supplierDisplayNameEn: 'Invited Co', status: 'Viewed', invitedAt: '2026-08-01T00:00:00Z', viewedAt: '2026-08-02T00:00:00Z', respondedAt: null, declineReason: null },
        ],
      }),
    })

    renderPage(<RfqDetailPage />)

    const row = (await screen.findByText('Invited Co')).closest('tr') as HTMLElement
    expect(within(row).getByText('Viewed')).toBeInTheDocument()
  })

  it('shows an unanswered clarification with an answer form, and answering shows a success toast', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published', {
        clarifications: [
          { id: 'cl-1', askedBySupplierId: 'sup-1', askedBySupplierNameAr: 'مورد', askedBySupplierNameEn: 'Asker Co', question: 'What is the incoterm?', answer: null, visibility: 'PrivateToAsker', askedAt: '2026-08-01T00:00:00Z', answeredAt: null },
        ],
      }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/What is the incoterm\?/)).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Answer'), 'FOB.')
    await userEvent.click(screen.getByRole('button', { name: 'Answer' }))

    expect(await screen.findByText('Answer saved')).toBeInTheDocument()
  })

  it('shows a Publish button for a privately-answered clarification, and clicking it shows a success toast', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published', {
        clarifications: [
          { id: 'cl-1', askedBySupplierId: 'sup-1', askedBySupplierNameAr: 'مورد', askedBySupplierNameEn: 'Asker Co', question: 'Q?', answer: 'A.', visibility: 'PrivateToAsker', askedAt: '2026-08-01T00:00:00Z', answeredAt: '2026-08-02T00:00:00Z' },
        ],
      }),
    })

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Publish to all' }))

    expect(await screen.findByText('Published to all')).toBeInTheDocument()
  })

  it('Published: shows the addendum form, and issuing one shows a success toast', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<RfqDetailPage />)

    await userEvent.type(await screen.findByLabelText('Title (English)'), 'Deadline extended')
    await userEvent.type(screen.getByLabelText('Title (Arabic)'), 'تمديد الموعد')
    await userEvent.type(screen.getByLabelText('Description (English)'), 'The deadline has moved.')
    await userEvent.type(screen.getByLabelText('Description (Arabic)'), 'تم تمديد الموعد.')
    await userEvent.click(screen.getByRole('button', { name: 'Issue addendum' }))

    expect(await screen.findByText('Addendum issued')).toBeInTheDocument()
  })

  it('Draft: hides the addendum form (locked-after-Published-except-addenda does not apply pre-publish)', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    await screen.findByText('Draft')
    expect(screen.queryByRole('button', { name: 'Issue addendum' })).not.toBeInTheDocument()
  })
})
