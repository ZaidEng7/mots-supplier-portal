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
    const evaluation: Evaluation = {
      id: 'eval-1', rfqId: 'rfq-1', rfqReferenceCode: 'RFQ-2026-000001', state: 'Assigned',
      criteria: [
        { id: 'crit-tech', nameAr: 'جودة', nameEn: 'Quality', dimension: 'Technical', weight: 60, maxScore: 100, threshold: 60, scoringType: 'Numeric', isFinancial: false },
        { id: 'crit-fin', nameAr: 'سعر', nameEn: 'Price', dimension: 'Commercial', weight: 40, maxScore: 100, threshold: null, scoringType: 'Numeric', isFinancial: true },
      ],
      assignments: [{ evaluatorUserId: 'eval-user-1', assignedAt: '2026-08-01T00:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null }],
      results: [],
    }
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluation,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('UnderEvaluation'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Quality')).toBeInTheDocument()
    expect(screen.getByText('Price')).toBeInTheDocument()
    expect(screen.getAllByText('Technical').length).toBeGreaterThan(0)
    expect(screen.getByText('Financial')).toBeInTheDocument()
    expect(screen.getByText('eval-user-1')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Evaluator user id'), 'eval-user-2')
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
