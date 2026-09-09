import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'
import type { Rfq, RfqState } from '../../api/rfqs'
import type { Evaluation } from '../../api/evaluations'
import type { Workspace } from '../../api/workspace'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { RfqDetailPage } = await import('./RfqDetailPage')
const { useAuthStore } = await import('../../lib/authStore')

function rfqFixture(state: RfqState, overrides: Partial<Rfq> = {}): Rfq {
  return {
    referenceCode: 'RFQ-2026-000001', organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
    descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state,
    publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
    items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
    // A-7: unowned by default, which is what every RFQ created before ownership existed looks like -
    // so the tests below exercise the fallback path unless a case sets it.
    ownerUserId: null, ownerName: null, assignedApproverUserId: null, assignedApproverName: null,
    ...overrides,
  }
}

function workspaceFixture(overrides: Partial<Workspace> = {}): Workspace {
  return {
    rfqReferenceCode: 'RFQ-2026-000001', rfqState: 'Draft', isCancelled: false, submittedProposalCount: 0,
    evaluationState: null, awardState: null,
    stages: [{ key: 'Draft', isCurrent: true, isCompleted: false }],
    nextActions: [],
    ...overrides,
  }
}

const REFERENCE_ROUTES = {
  // Declared before any '/api/v1/rfqs/{ref}' base route in every merged mockFetch call below -
  // mockFetch (renderPage.tsx) matches by first-declared substring, and these paths are suffixes
  // of the base RFQ route, so each must win the match or the base RFQ fixture object would be
  // returned here instead (breaking candidates.filter() / workspace's own shape).
  '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [],
  '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture(),
  // A-7: the two assignment pickers ask for this on every buyer view of an RFQ, so it belongs in the
  // shared routes rather than in the tests that happen to click one.
  '/api/v1/rfqs/RFQ-2026-000001/assignees': {
    owners: [{ userId: 'u-officer-2', fullName: 'Second Officer' }],
    approvers: [{ userId: 'u-manager-1', fullName: 'A Manager' }],
  },
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

  it('Draft: a line item can be corrected in place rather than deleted and retyped', async () => {
    // F-7: the aggregate had Add and Remove and nothing between them, so a mistyped quantity meant
    // deleting the line - which renumbers every line after it - and typing it again.
    const item = {
      id: 'item-1', lineNo: 1, titleAr: 'وجبة', titleEn: 'Hot lunch', specificationAr: null, specificationEn: null,
      categoryCode: 'consulting', quantity: 1000, unitOfMeasureCode: 'each', isUnitPrice: true, isOptional: false,
    }
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      { ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { items: [item] }) },
      recorded,
    )

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    const quantity = screen.getByLabelText('Quantity — 1')
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '180000')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Item updated')).toBeInTheDocument()
    const write = recorded.find((r) => r.method === 'PUT' && r.url.includes('/items/item-1'))
    expect(write, 'the correction goes to the item, not to a delete-then-add pair').toBeTruthy()
    expect(JSON.parse(String(write!.body)).quantity).toBe(180000)
  })

  it('Draft: the tender\'s own dates can be edited, which is what Draft means', async () => {
    // F-6: PUT /rfqs/{code} and updateRfqBasics both existed and nothing called either. An officer who
    // typed the submission window wrongly had to cancel the tender and author it again.
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      { ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') },
      recorded,
    )

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit details' }))
    const opens = screen.getByLabelText('Submission opens')
    await userEvent.type(opens, '2026-10-01T09:00')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Tender details saved')).toBeInTheDocument()
    const write = recorded.find((r) => r.method === 'PUT' && r.url.endsWith('/api/v1/rfqs/RFQ-2026-000001'))
    expect(write, 'the tender basics are written through the endpoint that already existed').toBeTruthy()
    expect(JSON.parse(String(write!.body)).submissionOpensAt).toContain('2026-10-01')
  })

  it('Draft: says that attachments become permanent, because there is no route back', async () => {
    // F-8/D-56: attachments are Draft-only by design and an addendum carries no file, so a published
    // tender with the wrong document attached can only be corrected by cancelling it. That is a
    // defensible rule and an invisible one - an officer attaching the wrong file has no way to know
    // they are making a permanent decision. The warning is the fix.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/attachments cannot be added, replaced or removed/i)).toBeInTheDocument()
  })

  it('Draft: a requirement can be corrected, and an edit can be abandoned', async () => {
    const requirement = { id: 'req-1', textAr: 'شرط', textEn: 'Cold chain plan', isMandatory: true, documentTypeCode: null, expectedEnvelope: null }
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      { ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { requirements: [requirement] }) },
      recorded,
    )

    renderPage(<RfqDetailPage />)

    // Abandoning first: the row must go back to reading, having written nothing.
    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByLabelText('Text (English) — 1')).not.toBeInTheDocument()
    expect(recorded.filter((r) => r.method === 'PUT')).toHaveLength(0)

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    const text = screen.getByLabelText('Text (English) — 1')
    await userEvent.clear(text)
    await userEvent.type(text, 'Cold chain plan for twelve sites')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Requirement updated')).toBeInTheDocument()
    const write = recorded.find((r) => r.method === 'PUT' && r.url.includes('/requirements/req-1'))
    expect(write).toBeTruthy()
    expect(JSON.parse(String(write!.body)).textEn).toBe('Cold chain plan for twelve sites')
  })

  it('Draft: a refused correction is reported rather than swallowed', async () => {
    // The error arm of the same mutation. A save that fails silently is how somebody leaves a tender
    // believing a quantity was corrected when it was not.
    const item = {
      id: 'item-1', lineNo: 1, titleAr: 'وجبة', titleEn: 'Hot lunch', specificationAr: null, specificationEn: null,
      categoryCode: 'consulting', quantity: 1000, unitOfMeasureCode: 'each', isUnitPrice: true, isOptional: false,
    }
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/items/item-1': { __status: 409, detail: 'Cannot edit RFQ content from state Published' },
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { items: [item] }),
    })

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/Cannot edit RFQ content|could not be saved/i)).toBeInTheDocument()
  })

  it('SubmissionOpen: closing early asks the officer why, and sends what they typed', async () => {
    // F-9: the screen used to send a fixed translated string, so every early close in the system
    // carried the same sentence and the audit trail said nothing about why.
    //
    // The reason is now collected by the same themed dialog every other reason on this product uses,
    // rather than by window.prompt - which rendered a browser chrome dialog in a product where nothing
    // else does, and which said nothing at all when it was dismissed.
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      {
        ...REFERENCE_ROUTES,
        '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'SubmissionOpen' }),
        '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen'),
      },
      recorded,
    )

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))

    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Reason'), '  Only bidder has submitted  ')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Close submission window' }))

    const write = recorded.find((r) => r.url.includes('/close'))
    expect(JSON.parse(String(write!.body)).reason).toBe('Only bidder has submitted')
  })

  it('SubmissionOpen: dismissing the reason dialog closes nothing, visibly', async () => {
    // The control. Cancelling must not send an empty reason - the aggregate would refuse it, and a
    // refusal the officer did not ask for reads as a broken button. What window.prompt could not do is
    // the second half: show the officer that nothing happened. A dialog that closes is that feedback.
    const recorded: RecordedRequest[] = []
    restore = mockFetch(
      {
        ...REFERENCE_ROUTES,
        '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'SubmissionOpen' }),
        '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen'),
      },
      recorded,
    )

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(recorded.some((r) => r.url.includes('/close'))).toBe(false)
  })

  it('SubmissionOpen: a whitespace-only reason cannot be submitted at all', async () => {
    // The old prompt accepted it, trimmed it to nothing and then silently fired nothing. The dialog
    // refuses the input instead, which is the difference between a button that does nothing and a
    // button that says why it is not ready.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'SubmissionOpen' }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen'),
    })

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Reason'), '   ')

    expect(within(dialog).getByRole('button', { name: 'Close submission window' })).toBeDisabled()
  })

  it('Draft: the approver field says which button commits the choice, and is associated with it', async () => {
    // §C2.4. Submitting for review also commits whatever is in this select, and leaving it blank means
    // "any manager" - which the placeholder already said. What nothing said is that the OTHER button is
    // what applies it, so an officer could choose an approver, not press Submit, and reasonably believe
    // they had nominated somebody.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'Draft' }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<RfqDetailPage />)

    const hint = await screen.findByText('Applied when you submit for review. Leave blank to let any manager approve.')
    // Associated, not merely adjacent: a sighted officer sees it beside the control and a screen-reader
    // user hears it as part of the control, which is the whole point of saying it here rather than in
    // a paragraph somewhere on the page.
    const select = screen.getByRole('combobox', { name: 'Choose an approver' })
    expect(select.getAttribute('aria-describedby')).toBe(hint.getAttribute('id'))
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

    await screen.findAllByText('Draft')
    expect(screen.queryByRole('button', { name: 'Issue addendum' })).not.toBeInTheDocument()
  })

  it('SubmissionClosed with no evaluation yet: shows Open evaluation, and opening it shows a success toast', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': null,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionClosed'),
    })

    renderPage(<RfqDetailPage />)

    const openButton = await screen.findByRole('button', { name: 'Open evaluation' })
    await userEvent.click(openButton)

    expect(await screen.findByText('Evaluation opened')).toBeInTheDocument()
  })

  it('UnderEvaluation: shows criteria with technical/financial envelope badges and the evaluator roster', async () => {
    // The candidates read is gated on evaluation.assign - the endpoint is, so the query is, so an officer
    // opening this page does not fetch a list they cannot act on. A test that wants the picker populated has to
    // say who is signed in.
    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'mgr-1', email: 'manager@example.test', permissions: ['evaluation.assign'] },
    })

    const evaluation: Evaluation = {
      id: 'eval-1', rfqId: 'rfq-1', rfqReferenceCode: 'RFQ-2026-000001', state: 'Assigned',
      criteria: [
        { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false },
        { id: 'crit-fin', nameAr: 'سعر', nameEn: 'Price', dimension: 'Commercial', weight: 40, maxScore: 100, threshold: null, scoringType: 'Numeric', isFinancial: true },
      ],
      // evaluatorName: the table rendered the GUID before batch 11, so the fixture now carries what the
      // screen actually shows.
      assignments: [{ evaluatorUserId: 'eval-user-1', evaluatorName: 'Rami Haddad', assignedAt: '2026-08-01T00:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null }],
      results: [],
    }
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/evaluation/candidates': [
        { userId: 'eval-user-2', fullName: 'Nadia Karam', email: 'nadia@example.test' },
      ],
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluation,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('UnderEvaluation'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Quality')).toBeInTheDocument()
    expect(screen.getByText('Price')).toBeInTheDocument()
    expect(screen.getAllByText('Technical').length).toBeGreaterThan(0)
    expect(screen.getByText('Financial')).toBeInTheDocument()
    // The NAME, not the GUID. This assertion used to look for the user id, which is exactly what the roster
    // was rendering: a manager deciding whether to recuse an evaluator was reading a UUID, and the recuse
    // button beside it named nobody.
    expect(screen.getByText('Rami Haddad')).toBeInTheDocument()

    // A PICKER now, not a box for a raw GUID. This test used to type "eval-user-2" into a text field, which
    // is exactly what a manager had to do - and they had no way to learn that id, because the only staff list
    // in the product needs admin.users.manage. Selecting a candidate is what the screen offers.
    await userEvent.click(screen.getByRole('combobox', { name: 'Choose an evaluator' }))
    await userEvent.click(await screen.findByRole('option', { name: /Nadia Karam/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Assign' }))

    expect(await screen.findByText('Evaluator assigned')).toBeInTheDocument()
  })

  // ---- FEAT-13.1/FR-PWF-001: the guided workspace panel ----

  it('Draft: the workspace panel shows the Draft stage as current and a blocked submit_review action with its reason', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({
        nextActions: [{ action: 'submit_review', labelAr: 'إرسال للمراجعة الداخلية', labelEn: 'Submit for internal review', permitted: false, blockedReasonAr: 'لا توجد بنود بعد.', blockedReasonEn: 'No items yet.' }],
      }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('RFQ Workflow')).toBeInTheDocument()
    expect(screen.getByText('Submit for internal review')).toBeInTheDocument()
    expect(screen.getByText('No items yet.')).toBeInTheDocument()
  })

  it('Awarded: the workspace panel shows a system-driven, unpermitted next action awaiting ERP sync', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({
        rfqState: 'Awarded', evaluationState: 'Finalized', awardState: 'Awarded',
        stages: [
          { key: 'Draft', isCurrent: false, isCompleted: true },
          { key: 'Awarded', isCurrent: true, isCompleted: false },
          { key: 'Completed', isCurrent: false, isCompleted: false },
        ],
        nextActions: [{ action: 'awaiting_erp_sync', labelAr: 'بانتظار مزامنة أمر الشراء مع نظام تخطيط الموارد', labelEn: 'Awaiting ERP Purchase Order sync', permitted: false, blockedReasonAr: 'هذه الخطوة تلقائية أو بانتظار طرف آخر.', blockedReasonEn: 'This step is automatic or awaiting another party.' }],
      }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Awarded'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Awaiting ERP Purchase Order sync')).toBeInTheDocument()
    expect(screen.getByText('This step is automatic or awaiting another party.')).toBeInTheDocument()
    // T2-33: the stage label now comes from UX-WRITING §7 via StatusChip, and the completed tick is
    // a separate aria-hidden glyph rather than string-concatenated into the label - so the two are
    // asserted separately. "Awarded" appears twice (the RFQ's own state chip and this stage), hence
    // the stage tracker is scoped by its own accessible name before querying inside it.
    expect(screen.getByText('✓')).toBeInTheDocument()
    const stages = screen.getByLabelText('Lifecycle stages')
    expect(within(stages).getByText('Draft')).toBeInTheDocument()
  })

  it('Cancelled: the workspace panel shows a cancelled banner instead of stages or actions', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'Cancelled', isCancelled: true, stages: [], nextActions: [] }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Cancelled'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('This RFQ has been cancelled.')).toBeInTheDocument()
  })

  it('offers the deadline control on a Published RFQ and not on a Draft one', async () => {
    // T-018: an extension the officer cannot trigger is the same defect shape as T-067 - the rule
    // permits it and no surface reaches it.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByLabelText('New deadline')).toBeInTheDocument()
    // A-6: and a reason, which the server now requires. Disabled until BOTH are given - the guard in
    // the direction that refuses.
    expect(screen.getByLabelText('Reason for the change')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change deadline' })).toBeDisabled()
  })

  it('hides the deadline control before the RFQ is published', async () => {
    // The control for the test above: BRULE-035 permits the change while Published/SubmissionOpen
    // only, and the screen gates on the same two states the domain does.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/RFQ-2026-000001/)).toBeInTheDocument()
    expect(screen.queryByLabelText('New deadline')).not.toBeInTheDocument()
  })

  it('lists the RFQ attachments, downloads one, and offers upload only on a Draft', async () => {
    // SCR-414. addRfqAttachment / removeRfqAttachment / the download-url route have existed since
    // EPIC-07 and no screen called any of them: the tender documents could only be attached through
    // the API. Found by the batch 9 per-screen sweep.
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', {
        attachments: [{
          id: 'att-1', originalFileName: 'tender-terms.pdf', contentType: 'application/pdf',
          caption: 'Terms of reference', uploadedAt: '2026-09-01T10:00:00Z',
        }],
      }),
      '/api/v1/rfqs/RFQ-2026-000001/attachments/att-1/download-url': { url: 'https://storage.example/signed' },
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('tender-terms.pdf')).toBeInTheDocument()
    expect(screen.getByLabelText('Add an attachment')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Download' }))
    await vi.waitFor(() => expect(open).toHaveBeenCalledWith('https://storage.example/signed', '_blank', 'noopener,noreferrer'))
    open.mockRestore()
  })

  it('does not offer attachment upload or removal once the RFQ has left Draft', async () => {
    // The control. An attachment a supplier has already been invited to read must not vanish, and the
    // gate is the same isDraft every other structural edit on this page uses.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published', {
        attachments: [{
          id: 'att-1', originalFileName: 'tender-terms.pdf', contentType: 'application/pdf',
          caption: null, uploadedAt: '2026-09-01T10:00:00Z',
        }],
      }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('tender-terms.pdf')).toBeInTheDocument()
    expect(screen.queryByLabelText('Add an attachment')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    // Download stays available: reading it is not editing it.
    expect(screen.getByRole('button', { name: 'Download' })).toBeInTheDocument()
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

    renderPage(<RfqDetailPage />)

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

    renderPage(<RfqDetailPage />)

    await userEvent.type(await screen.findByLabelText('Answer'), 'FOB.')  // the Field label, not the button
    await userEvent.click(screen.getByRole('button', { name: 'Answer' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.includes('/answer') && c.method === 'POST')).toBe(true))
    const sent = JSON.parse(calls.find((c) => c.url.includes('/answer'))!.body)
    expect(sent).toEqual({ answer: 'FOB.' })
  })

  it('sends the deadline reason and will not submit without one', async () => {
    // A-6. BRULE-035 leaves an extension uncapped, so the reason is what makes it defensible; D-12
    // called the audit row the control, and a row that records only that someone moved a date is not
    // one.
    const calls: { url: string; method: string; body: string }[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published'),
      '/api/v1/rfqs/RFQ-2026-000001/deadline': rfqFixture('Published'),
    }, calls)

    renderPage(<RfqDetailPage />)

    await userEvent.type(await screen.findByLabelText('New deadline'), '2026-12-01T10:00')
    // Still disabled: the date alone is not enough.
    expect(screen.getByRole('button', { name: 'Change deadline' })).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason for the change'), 'The Ministry extended the tender period.')
    await userEvent.click(screen.getByRole('button', { name: 'Change deadline' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/deadline'))).toBe(true))
    const sent = JSON.parse(calls.find((c) => c.url.endsWith('/deadline'))!.body)
    expect(sent.reason).toBe('The Ministry extended the tender period.')
  })

  it('names the owner on the screen, and says "Unassigned" when there is none', async () => {
    // A-7. Who is answerable belongs where the work is, not only in the audit trail.
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { ownerUserId: 'u-1', ownerName: 'An Officer' }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/Owner: An Officer/)).toBeInTheDocument()
  })

  it('says "Unassigned" for an RFQ that predates ownership', async () => {
    // The control for the test above: the same element, the fallback wording. Every RFQ created before
    // A-7 looks exactly like this fixture's default.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/Owner: Unassigned/)).toBeInTheDocument()
  })

  it('sends the new owner and the reason, and will not reassign without both', async () => {
    const calls: RecordedRequest[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001/reassign': rfqFixture('Draft', { ownerUserId: 'u-officer-2', ownerName: 'Second Officer' }),
    }, calls)

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: 'New owner' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Second Officer' }))

    // Still disabled: an owner without a stated reason is the audit row this operation exists for,
    // written empty.
    expect(screen.getByRole('button', { name: 'Reassign' })).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason for the handover'), 'The first officer is on leave.')
    await userEvent.click(screen.getByRole('button', { name: 'Reassign' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/reassign'))).toBe(true))
    const sent = JSON.parse(calls.find((c) => c.url.endsWith('/reassign'))!.body)
    expect(sent).toMatchObject({ newOwnerUserId: 'u-officer-2', reason: 'The first officer is on leave.' })
  })

  it('submits for review with the nominated approver, and without one when none is chosen', async () => {
    const calls: RecordedRequest[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001/submit-review': rfqFixture('InternalReview'),
    }, calls)

    renderPage(<RfqDetailPage />)

    // The control first: submitting with nothing chosen sends null, which the server reads as the
    // manager pool - the behaviour every caller written before A-7 relied on.
    await userEvent.click(await screen.findByRole('button', { name: 'Submit for review' }))
    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/submit-review'))).toBe(true))
    expect(JSON.parse(calls.find((c) => c.url.endsWith('/submit-review'))!.body).assignedApproverUserId).toBeNull()

    await userEvent.click(screen.getByRole('combobox', { name: 'Choose an approver' }))
    await userEvent.click(await screen.findByRole('option', { name: 'A Manager' }))
    await userEvent.click(screen.getByRole('button', { name: 'Submit for review' }))

    await vi.waitFor(() => {
      const bodies = calls.filter((c) => c.url.endsWith('/submit-review')).map((c) => JSON.parse(c.body))
      expect(bodies.some((b) => b.assignedApproverUserId === 'u-manager-1')).toBe(true)
    })
  })
})

/**
 * The evaluation panel's batch-11 changes, each of which was found by walking a tender in a browser
 * and none of which any existing test could have caught: two cells rendered GUIDs on the screen where
 * a tender is decided, the assign control was a free-text GUID box, and the panel was hidden for the
 * four states after UnderEvaluation.
 */
describe('RfqDetailPage evaluation panel (T-082)', () => {
  let restore: () => void

  afterEach(() => {
    restore?.()
    useAuthStore.setState({ accessToken: null, claims: null, status: 'idle' })
  })

  function signInWith(permissions: string[]) {
    useAuthStore.setState({
      accessToken: 'test',
      status: 'authenticated',
      claims: { userId: 'u-manager-1', email: 'manager@example.test', organizationId: 'org-1', permissions },
    })
  }

  function evaluation(overrides: Partial<Evaluation> = {}): Evaluation {
    return {
      id: 'ev-1', rfqId: 'rfq-1', rfqReferenceCode: 'RFQ-2026-000001', state: 'InProgress',
      criteria: [], assignments: [], results: [],
      ...overrides,
    }
  }

  const CANDIDATES = '/api/v1/rfqs/RFQ-2026-000001/evaluation/candidates'
  const EVALUATION = '/api/v1/rfqs/RFQ-2026-000001/evaluation'

  function routes(state: RfqState, ev: Evaluation, candidates: unknown[] = []) {
    return {
      ...REFERENCE_ROUTES,
      [CANDIDATES]: candidates,
      [EVALUATION]: ev,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture(state),
    }
  }

  it('names the evaluator rather than printing their GUID', async () => {
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('UnderEvaluation', evaluation({
      assignments: [{
        evaluatorUserId: '01a07461-fa48-7721-abe2-018baaa84d11', evaluatorName: 'Nadia Suleiman',
        assignedAt: '2026-09-01T09:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null,
      }],
    })))

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Nadia Suleiman')).toBeInTheDocument()
    expect(screen.queryByText('01a07461-fa48-7721-abe2-018baaa84d11')).not.toBeInTheDocument()
  })

  it('falls back to the id when the evaluator has no name', async () => {
    // The control, and a real case: an assignment whose user row has gone should stay visible rather
    // than leaving the recuse button beside an empty cell.
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('UnderEvaluation', evaluation({
      assignments: [{
        evaluatorUserId: 'u-gone', evaluatorName: null,
        assignedAt: '2026-09-01T09:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null,
      }],
    })))

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('u-gone')).toBeInTheDocument()
  })

  it('offers evaluators as a picker of names, not a box to type a GUID into', async () => {
    // This was an Input asking a manager to type 01a07461-fa48-7721-abe2-018baaa84d11, and the only
    // staff list in the product needs admin.users.manage, which a procurement_manager does not hold.
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('UnderEvaluation', evaluation(), [
      { userId: 'u-eval-1', fullName: 'Nadia Suleiman', email: 'nadia@example.test' },
    ]))

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: /evaluator|المقيّم/i }))
    expect(await screen.findByRole('option', { name: /Nadia Suleiman/ })).toBeInTheDocument()
  })

  it('leaves an already-assigned evaluator out of the picker', async () => {
    // Assigning the same person twice is a request the aggregate refuses, so it should not be offered.
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('UnderEvaluation', evaluation({
      assignments: [{
        evaluatorUserId: 'u-eval-1', evaluatorName: 'Nadia Suleiman',
        assignedAt: '2026-09-01T09:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null,
      }],
    }), [
      { userId: 'u-eval-1', fullName: 'Nadia Suleiman', email: 'nadia@example.test' },
      { userId: 'u-eval-2', fullName: 'Omar Khalil', email: 'omar@example.test' },
    ]))

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: /evaluator|المقيّم/i }))
    expect(await screen.findByRole('option', { name: /Omar Khalil/ })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Nadia Suleiman/ })).not.toBeInTheDocument()
  })

  it('does not ask for candidates without evaluation.assign', async () => {
    // Guard both ways. The candidates endpoint requires the permission, so a persona without it would
    // get a 403 on every view of an RFQ - an error in the log for a control they cannot use.
    signInWith([])
    const recorded: RecordedRequest[] = []
    restore = mockFetch(routes('UnderEvaluation', evaluation()), recorded)

    renderPage(<RfqDetailPage />)

    await screen.findByText(/Sample RFQ/)
    await waitFor(() => expect(recorded.length).toBeGreaterThan(2))
    expect(recorded.some((r) => r.url.includes('/evaluation/candidates'))).toBe(false)
  })

  it('shows the proposal reference code in the results table, not the internal id', async () => {
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('Recommendation', evaluation({
      state: 'Consolidated',
      results: [{
        proposalId: '9f1c2d3e-0000-4000-8000-000000000001', proposalReferenceCode: 'PRP-2026-000004',
        technicallyQualified: true, technicalWeightedScore: 82, financialWeightedScore: 15,
        weightedTotal: 97, rank: 1,
      }],
    })))

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('PRP-2026-000004')).toBeInTheDocument()
    expect(screen.queryByText('9f1c2d3e-0000-4000-8000-000000000001')).not.toBeInTheDocument()
  })

  it.each(['SubmissionClosed', 'UnderEvaluation', 'Clarification', 'Shortlisting', 'Recommendation', 'AwardApproval', 'Awarded'])(
    'shows the evaluation panel in %s', async (state) => {
      // The list used to stop at UnderEvaluation, which hid the panel for the whole second half of a
      // tender: a manager at Shortlisting could not see who had scored what.
      signInWith(['evaluation.assign'])
      restore = mockFetch(routes(state as RfqState, evaluation(), [
        { userId: 'u-eval-1', fullName: 'Nadia Suleiman', email: 'nadia@example.test' },
      ]))

      renderPage(<RfqDetailPage />)

      expect(await screen.findByRole('combobox', { name: /evaluator|المقيّم/i })).toBeInTheDocument()
    },
  )

  it('shows no evaluation panel while the RFQ is still open for bids', async () => {
    // The control for the seven above: there is nothing to evaluate before submissions close.
    signInWith(['evaluation.assign'])
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<RfqDetailPage />)

    await screen.findByText(/Sample RFQ/)
    expect(screen.queryByRole('combobox', { name: /evaluator|المقيّم/i })).not.toBeInTheDocument()
  })

  it('links to the received proposals from SubmissionClosed onward', async () => {
    // T-082: the bids are readable before the comparison matrix exists and without an opened
    // evaluation, so the link cannot be gated on either.
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('SubmissionClosed', evaluation()))

    renderPage(<RfqDetailPage />)

    const link = (await screen.findByRole('button', { name: /received proposals|العروض الواردة/i })).closest('a')
    expect(link).toHaveAttribute('href', '/back-office/rfqs/RFQ-2026-000001/proposals')
  })
})
