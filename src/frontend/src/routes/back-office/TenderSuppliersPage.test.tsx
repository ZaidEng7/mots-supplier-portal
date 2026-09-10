import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'
import type { Rfq, RfqState } from '../../api/rfqs'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useParams: () => ({ referenceCode: 'RFQ-2026-000001' }),
    useRouterState: () => '/back-office/rfqs/RFQ-2026-000001/suppliers',
    Link: ({ to, params, children, ...rest }: { to: string; params?: Record<string, string>; children: React.ReactNode }) => {
      const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`\$${key}`, value), to)
      return <a href={href} {...rest}>{children}</a>
    },
  }
})

const { TenderSuppliersPage } = await import('./TenderSuppliersPage')

function rfqFixture(state: RfqState, overrides: Partial<Rfq> = {}): Rfq {
  return {
    referenceCode: 'RFQ-2026-000001', organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
    descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state,
    publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
    items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
    ownerUserId: null, ownerName: null, assignedApproverUserId: null, assignedApproverName: null,
    ...overrides,
  }
}

const REFERENCE_ROUTES = {
  '/api/v1/rfqs/RFQ-2026-000001/workspace': {
    rfqReferenceCode: 'RFQ-2026-000001', rfqState: 'Draft', isCancelled: false, submittedProposalCount: 0,
    evaluationState: null, awardState: null, stages: [], nextActions: [],
  },
  '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [
    { supplierId: 's-9', displayNameAr: 'مورد مقترح', displayNameEn: 'Suggested Supplier', categoryCodes: [] },
  ],
}

/**
 * The comp's Suppliers tab: who was asked to bid, and what they asked back.
 *
 * <p>Every test here moved from  unchanged except for the component it renders.
 * The invitation and clarification behaviour did not change; only the screen it lives on did.</p>
 */
describe('TenderSuppliersPage', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.(); restore = undefined })

  it('Draft: shows suggested candidates and inviting one shows a success toast', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [
        { supplierId: 'sup-1', displayNameAr: 'مورد', displayNameEn: 'Candidate Co', matchCount: 2 },
      ],
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<TenderSuppliersPage />)

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

    renderPage(<TenderSuppliersPage />)

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

    renderPage(<TenderSuppliersPage />)

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

    renderPage(<TenderSuppliersPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Publish to all' }))

    expect(await screen.findByText('Published to all')).toBeInTheDocument()
  })

  it('does NOT show the Publish button for an answer that already went to everyone', async () => {
    // The other half of the test above, and the one the design audit's §C2.3 assumed was missing. It
    // read the guard - `answer && visibility === 'PrivateToAsker'` - noticed that answering now sets
    // both fields at once, and concluded the control describes a state the domain cannot produce.
    //
    // What it describes is a LEGACY state. A-4 (DECISIONS-TAKEN.md:441) made answering publish to every
    // invitee, on the ground that equal information to all bidders is the fundamental fairness principle
    // in tendering, and it kept the visibility enum and this route on purpose: a deployment that
    // answered privately before A-4 still holds those rows, and dropping the route would leave those
    // threads permanently unshareable. The backend has the matching integration test.
    //
    // So the control is correct, and this pins the part that was only ever true by inspection: it never
    // appears on a clarification answered under A-4.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published', {
        clarifications: [
          { id: 'cl-1', askedBySupplierId: 'sup-1', askedBySupplierNameAr: 'مورد', askedBySupplierNameEn: 'Asker Co', question: 'Q?', answer: 'A.', visibility: 'PublishedToAll', askedAt: '2026-08-01T00:00:00Z', answeredAt: '2026-08-02T00:00:00Z' },
        ],
      }),
    })

    renderPage(<TenderSuppliersPage />)

    // The thread renders, so a missing button is a decision rather than an empty screen.
    expect(await screen.findByText(/Asker Co: Q\?/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish to all' })).not.toBeInTheDocument()
  })

  it('tells the officer the answer broadcasts instead of asking whether it should', async () => {
    // A-4. The answer form used to carry a "publish immediately" checkbox defaulting to off, so the
    // fair outcome depended on the officer ticking a box. Equal information to all bidders is not an
    // option, so the box is gone and the form says what will happen.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen', {
        clarifications: [{
          id: 'c-1', askedBySupplierId: 's-1', askedBySupplierNameAr: 'مورد', askedBySupplierNameEn: 'Supplier One',
          question: 'Which incoterm?', answer: null, visibility: 'PrivateToAsker',
          askedAt: '2026-09-01T10:00:00Z', answeredAt: null,
        }],
      }),
    })

    renderPage(<TenderSuppliersPage />)

    // The question renders alongside the asker's name in one paragraph, hence the partial match.
    expect(await screen.findByText(/Which incoterm\?/)).toBeInTheDocument()
    expect(screen.getByText(/goes to every invited supplier/)).toBeInTheDocument()
    expect(screen.queryByText('Publish immediately')).not.toBeInTheDocument()
  })

  it('sends no publish flag when the officer answers', async () => {
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen', {
        clarifications: [{
          id: 'c-1', askedBySupplierId: 's-1', askedBySupplierNameAr: 'مورد', askedBySupplierNameEn: 'Supplier One',
          question: 'Which incoterm?', answer: null, visibility: 'PrivateToAsker',
          askedAt: '2026-09-01T10:00:00Z', answeredAt: null,
        }],
      }),
      '/api/v1/rfqs/RFQ-2026-000001/clarifications/c-1/answer': rfqFixture('SubmissionOpen'),
    }, calls)

    renderPage(<TenderSuppliersPage />)

    await userEvent.type(await screen.findByLabelText('Answer'), 'FOB.')  // the Field label, not the button
    await userEvent.click(screen.getByRole('button', { name: 'Answer' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.includes('/answer') && c.method === 'POST')).toBe(true))
    const sent = JSON.parse(calls.find((c) => c.url.includes('/answer'))!.body)
    expect(sent).toEqual({ answer: 'FOB.' })
  })
})
