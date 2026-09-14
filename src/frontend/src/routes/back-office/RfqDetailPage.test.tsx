// FEAT-07.1 through 07.10: the state-gated tender workspace. Two suites - the page itself, and the evaluation panel's
// batch-11 changes.
//
// THE HARNESS. The tab strip asks where it is, so these tests answer: on the tender tab itself. The Suppliers, Bids and
// Settings tabs are other routes with their own tests, and what this file has to be right about is that "Tender" is the
// current one and no other tab claims to be. Link is a real anchor with the resolved href rather than a bare 'a' stub,
// because the screen's exits to the bids, the comparison and the award are links now and a stub that threw their destination
// away would let a wrong route pass unnoticed - which is the whole reason they stopped being raw hrefs. The fixture tender is
// unowned by default (A-7), which is what every tender created before ownership existed looks like, so the tests exercise the
// fallback path unless a case sets it.
//
// ROUTE ORDER MATTERS in the shared fixtures. mockFetch matches by first-declared substring, and the workspace, evaluation and
// assignee paths are all SUFFIXES of the base /api/v1/rfqs/{ref} route - so each must be declared before it or the base RFQ
// fixture answers in its place. The evaluation one is the instructive case: without its own entry it matched the base route
// and the page was handed an Rfq where it expected an Evaluation, then read .criteria off it and crashed - visible only in a
// test that lingered long enough for that query to settle, which made it look like whichever assertion was slow that day.
// Tests that need a real evaluation still declare their own. The assignee list is in the shared set because A-7's two pickers
// ask for it on every buyer view of an RFQ, rather than only in the tests that happen to click one. And because every RFQ
// mutation URL is a suffix of the base GET route, one declared base route serves reads and every write for a given test.
//
// DRAFT. The editable item, requirement and template-bind controls show, and adding an item succeeds. A line item can be
// CORRECTED IN PLACE rather than deleted and retyped (F-7): the aggregate had Add and Remove and nothing between them, so a
// mistyped quantity meant deleting the line - which renumbers every line after it - and typing it again. A requirement can be
// corrected too, and an edit can be ABANDONED, with the abandon asserted first so the row goes back to reading having written
// nothing. A refused correction is REPORTED rather than swallowed, which is the error arm of the same mutation: a save that
// fails silently is how somebody leaves a tender believing a quantity was corrected when it was not.
//
// The tender's own dates can be edited, which is what Draft means (F-6): PUT /rfqs/{code} and updateRfqBasics both existed and
// nothing called either, so an officer who typed the submission window wrongly had to cancel the tender and author it again.
// And the picker will not offer a submission time that lapses while the form is being filled in - walked into, where a window
// set to open a few minutes ahead had lapsed by the time the form was finished and Submit was refused for a date that had
// been in the future when it was typed; the refusal is the domain's and is correct, and the picker offering that time is what
// made it happen. The closing date cannot precede the opening one, which is the other half of the same window.
//
// Draft SAYS that attachments become permanent, because there is no route back (F-8 and D-56): attachments are Draft-only by
// design and an addendum carries no file, so a published tender with the wrong document attached can only be corrected by
// cancelling it. That is a defensible rule and an invisible one - an officer attaching the wrong file has no way to know they
// are making a permanent decision - and the warning is the fix.
//
// THE EARLY CLOSE asks the officer why and sends what they typed (F-9). The screen used to send a fixed translated string, so
// every early close in the system carried the same sentence and the audit trail said nothing about why. The reason is
// collected by the same themed dialog every other reason in this product uses rather than by window.prompt, which rendered
// browser chrome in a product where nothing else does and said nothing at all when it was dismissed. Its control is that
// dismissing the dialog closes NOTHING, visibly: cancelling must not send an empty reason, because the aggregate would refuse
// it and a refusal the officer did not ask for reads as a broken button - and showing the officer that nothing happened is the
// second half window.prompt could not do. A whitespace-only reason cannot be submitted at all: the old prompt accepted it,
// trimmed it to nothing and then silently fired nothing, while the dialog refuses the input - the difference between a button
// that does nothing and a button that says why it is not ready.
//
// THE APPROVER FIELD says which button commits the choice, and is ASSOCIATED with it (§C2.4). Submitting for review also
// commits whatever is in that select, and leaving it blank means "any manager", which the placeholder already said; what
// nothing said is that the OTHER button applies it, so an officer could choose an approver, not press Submit, and reasonably
// believe they had nominated somebody. Associated rather than merely adjacent: a sighted officer sees it beside the control
// and a screen-reader user hears it as part of the control, which is the whole point of saying it there rather than in a
// paragraph somewhere on the page.
//
// THE LAYOUT. §D1 measured eleven cards of identical visual weight, seven of them empty, in DOM order, with nothing on the
// screen larger, closer or louder than anything else. Grouping was the first fix; the tab strip is the second and it does most
// of that work now, because the sections a reader used to scroll past are separate screens and the tab says which one they
// are on. What is left to be right about is that the rail comes FIRST - a screen reader and a 320px viewport both meet "what
// happens next" before the body - and that a heading only appears where it names something the tab does not. "The tender" is
// gone because the current tab says it, and the group headings are gone because those sections are their own screens now, each
// with its own tests; that is asserted OUTSIDE the tab strip, because "Suppliers" is still a word on this page as the tab that
// leads to that screen. Its denominator is the Decisions group, which appears once there is a decision to show: a guard that
// hid a group unconditionally would pass the other test and be wrong.
//
// THE WORKSPACE PANEL is FEAT-13.1 and FR-PWF-001's. On a Draft it shows the Draft stage as current and a blocked
// submit_review action with its reason - three cards rather than one, because "what happens next" is the one that asks
// something of the reader, and a blocked transition still names itself before it explains itself: the label is what the reason
// is a reason ABOUT, where the panel this replaced put the label in a badge and the reason in grey text beside it. The stage
// the tender is actually at is said in the MARKUP rather than only in a colour.
//
// On an Awarded tender it shows a system-driven, unpermitted next action awaiting ERP sync. T2-33: the stage label comes from
// UX-WRITING §7 via StatusChip, and the finished tick used to be a visible aria-hidden glyph - the mark is now filled, ringed
// or hollow, and the state travels to a screen reader as a word instead. "Awarded" appears twice there, as the RFQ's own state
// chip and as this stage, so the tracker is scoped by its accessible name before querying inside it. On a Cancelled tender it
// shows a cancelled banner instead of stages or actions.
//
// THE COMP'S BAND: a tender is a thing with a NAME. The heading used to be "RFQ-2026-000001 - Sample RFQ", the code first at
// h1 size with the tender's own name appended to it; a reference code is how you find a tender again rather than what it is
// called, so the comp puts the name in the heading and files the code with the other identifying facts. The owner is still on
// the screen and still says who is answerable - A-7 put it there and it stays.
//
// The second chip says WHEN submissions close, while they are open, and it is the one a buyer acts on: "Open for submissions"
// does not say whether that means today or next month, and the closing date lived three cards down the page. That test allows
// an hour of slack, because formatRelative truncates and exactly six days minus the milliseconds the test takes to run is five
// whole days - the assertion would otherwise be about the clock rather than about the chip. Its denominator is that the chip
// does NOT count down on a tender whose submissions are not open: on a Draft the same date is a plan and on an Awarded tender
// it is history, and counting down to either would be the screen telling a buyer to hurry about something already finished.
//
// THE AT-A-GLANCE COUNTS count what is on the page, and each is a number a reader used to get by scrolling to a card and
// counting its rows. Questions counts UNANSWERED clarifications, because an answered question is not waiting on the buyer and a
// total would read as though it were. They are read off the description LIST rather than the page, because "Invited" is also
// an invitation STATUS in the table below and a page-wide text query would find the wrong one and pass for the wrong reason.
//
// PUBLISHED shows an existing item and no item-edit controls - state-gated editing - and hides the addendum form before
// publication, since locked-after-Published-except-addenda does not apply pre-publish. Binding an evaluation template toasts.
// Approve is the MANAGER's, so the session has to hold it for the button to be on the page at all: an officer is now shown no
// Approve rather than one that answers 403. The deadline control is hidden before publication, because BRULE-035 permits the
// change while Published or SubmissionOpen only and the screen gates on the same two states the domain does.
//
// SCR-414's ATTACHMENTS list, download one, and offer upload only on a Draft. addRfqAttachment, removeRfqAttachment and the
// download-url route have existed since EPIC-07 and no screen called any of them, so the tender documents could only be
// attached through the API - found by the batch 9 per-screen sweep. The control is that upload and removal are NOT offered
// once the RFQ has left Draft: an attachment a supplier has already been invited to read must not vanish, and the gate is the
// same isDraft every other structural edit on this page uses. Download stays available, because reading it is not editing it.
//
// A-7's OWNER is named on the screen, with "Unassigned" when there is none - who is answerable belongs where the work is,
// rather than only in the audit trail - and the control is the same element with the fallback wording, which is what every
// tender created before A-7 looks like. Submitting for review sends the nominated approver, and null when none is chosen: that
// control comes first, because null is what the server reads as the manager pool, which is the behaviour every caller written
// before A-7 relied on.
//
// THE EVALUATION PANEL's own suite covers the batch-11 changes, each found by walking a tender in a browser and none of which
// any existing test could have caught: two cells rendered GUIDs on the screen where a tender is decided, the assign control
// was a free-text GUID box, and the panel was hidden for the four states after UnderEvaluation.
//
// "MY EVALUATION" is an assigned evaluator's screen and nobody else's. It was shown to whoever could see the panel, so a
// procurement manager - who opens the evaluation, assigns the evaluators and consolidates their scores, but does not score -
// was offered a scoring screen with nothing on it. Asked directly by the person it happened to: "if I cannot evaluate as a
// manager, why is there an evaluate button for the manager?" Its control is the same panel with the same permissions and one
// thing different, this session being on the assignment list - without which the first test would pass against a link nobody
// ever sees.
//
// The evaluator is NAMED rather than printed as a GUID, with the fallback to the id as its control and a real case: an
// assignment whose user row has gone should stay visible rather than leaving the recuse button beside an empty cell. The
// roster's earlier assertion looked for the user id, which is exactly what the screen was rendering - a manager deciding
// whether to recuse an evaluator was reading a UUID, and the recuse button beside it named nobody.
//
// Evaluators are offered as a PICKER of names rather than a box to type a GUID into. That was an Input asking a manager to
// type 01a07461-fa48-7721-abe2-018baaa84d11, and the only staff list in the product needs admin.users.manage, which a
// procurement_manager does not hold. An already-assigned evaluator is left out of it, because assigning the same person twice
// is a request the aggregate refuses and should not be offered. And candidates are NOT asked for without evaluation.assign -
// the guard both ways, because the endpoint requires the permission so a persona without it would get a 403 on every view of
// an RFQ, an error in the log for a control they cannot use.
//
// The results table shows the proposal REFERENCE CODE rather than the internal id. The panel is shown for every non-terminal
// state from SubmissionClosed onward, where the list used to stop at UnderEvaluation - which hid it for the whole second half
// of a tender, so a manager at Shortlisting could not see who had scored what. The control for all of that is that there is NO
// panel while the RFQ is still open for bids, because there is nothing to evaluate before submissions close.
//
// The last test links to the received proposals from SubmissionClosed onward (T-082): the bids are readable before the
// comparison matrix exists and without an opened evaluation, so the link cannot be gated on either. It is a LINK rather than a
// button inside one - it used to be <a href><Button/></a>, which is invalid markup and a bare href that reloaded the whole
// application on a screen whose point is moving between bids.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'
import type { Rfq, RfqState } from '../../api/rfqs'
import type { Evaluation } from '../../api/evaluations'
import type { Workspace } from '../../api/workspace'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useParams: () => ({ referenceCode: 'RFQ-2026-000001' }),
    useRouterState: () => '/back-office/rfqs/RFQ-2026-000001',
    Link: ({ to, params, children, ...rest }: { to: string; params?: Record<string, string>; children: React.ReactNode }) => {
      const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`$${key}`, value), to)
      return <a href={href} {...rest}>{children}</a>
    },
  }
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
  '/api/v1/rfqs/RFQ-2026-000001/invitations/candidates': [],
  '/api/v1/rfqs/RFQ-2026-000001/evaluation': null,
  '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture(),
  '/api/v1/rfqs/RFQ-2026-000001/assignees': {
    owners: [{ userId: 'u-officer-2', fullName: 'Second Officer' }],
    approvers: [{ userId: 'u-manager-1', fullName: 'A Manager' }],
  },
  '/api/v1/reference/categories': [{ code: 'consulting', nameAr: 'استشارات', nameEn: 'Consulting' }],
  '/api/v1/reference/units-of-measure': [{ code: 'each', nameAr: 'وحدة', nameEn: 'Each' }],
  '/api/v1/evaluation-templates': [{ id: 'tpl-1', familyId: 'fam-1', version: 2, nameAr: 'قالب', nameEn: 'Standard', status: 'Active', isReferenced: false, criteria: [] }],
}

describe('RfqDetailPage', () => {
  let restore: () => void

  const mockSubmissionOpen = (recorded?: RecordedRequest[]) => mockFetch(
    {
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'SubmissionOpen' }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen'),
    },
    recorded,
  )
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
    const recorded: RecordedRequest[] = []
    restore = mockSubmissionOpen(recorded)

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))

    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Reason'), '  Only bidder has submitted  ')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Close submission window' }))

    const write = recorded.find((r) => r.url.includes('/close'))
    expect(JSON.parse(String(write!.body)).reason).toBe('Only bidder has submitted')
  })

  it('SubmissionOpen: dismissing the reason dialog closes nothing, visibly', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockSubmissionOpen(recorded)

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(recorded.some((r) => r.url.includes('/close'))).toBe(false)
  })

  it('SubmissionOpen: a whitespace-only reason cannot be submitted at all', async () => {
    restore = mockSubmissionOpen()

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Close submission window' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Reason'), '   ')

    expect(within(dialog).getByRole('button', { name: 'Close submission window' })).toBeDisabled()
  })

  it('Draft: the approver field says which button commits the choice, and is associated with it', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'Draft' }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<RfqDetailPage />)

    const hint = await screen.findByText('Applied when you submit for review. Leave blank to let any manager approve.')
    const select = screen.getByRole('combobox', { name: 'Choose an approver' })
    expect(select.getAttribute('aria-describedby')).toBe(hint.getAttribute('id'))
  })

  it('leads with the rail, and labels only what the tab strip does not name', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)
    await screen.findByRole('heading', { level: 1 })

    const regions = screen.getAllByRole('region').filter((g) => g.tagName === 'SECTION')
    expect(regions.map((g) => document.getElementById(g.getAttribute('aria-labelledby')!)?.textContent))
      .toEqual(['What happens next'])

    const outsideTabs = (text: string) => screen.queryAllByText(text).filter((el) => !el.closest('nav'))

    expect(outsideTabs('The tender')).toEqual([])
    expect(outsideTabs('Suppliers')).toEqual([])
    expect(outsideTabs('Managing this tender')).toEqual([])
    expect(outsideTabs('Decisions')).toEqual([])
  })

  it('shows the Decisions group once there is a decision to show', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('InternalReview', {
        approvals: [{ stepNo: 1, approverUserId: null, decision: 'Approved' as const, comment: null, decidedAt: '2026-08-02T00:00:00Z' }],
      }),
    })

    renderPage(<RfqDetailPage />)

    await screen.findByRole('region', { name: 'Decisions' })
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
    ['Draft' as const, 'Submit for review', 'Tender submitted for review'],
    ['InternalReview' as const, 'Approve', 'Tender approved'],
    ['Approved' as const, 'Publish', 'Tender published'],
  ])('%s: clicking the primary action calls its own transition and surfaces the right toast', async (state, buttonName, toastText) => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture(state) })

    useAuthStore.setState({
      accessToken: 'token',
      status: 'authenticated',
      claims: { userId: 'u-1', email: 'manager@example.test', organizationId: 'org-1', permissions: ['rfq.approve'] },
    } as never)

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('button', { name: buttonName }))

    expect(await screen.findByText(toastText)).toBeInTheDocument()
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
    expect(screen.getByText('Rami Haddad')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('combobox', { name: 'Choose an evaluator' }))
    await userEvent.click(await screen.findByRole('option', { name: /Nadia Karam/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Assign' }))

    expect(await screen.findByText('Evaluator assigned')).toBeInTheDocument()
  })

  it('will not offer a submission time that lapses while the form is being filled in', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Edit details' }))

    const opens = screen.getByLabelText('Submission opens')
    const min = opens.getAttribute('min')
    expect(min, 'the opening date needs a floor, or the picker offers a time already in the past').not.toBeNull()
    expect(new Date(min!).getTime()).toBeGreaterThan(Date.now())

    const closes = screen.getByLabelText('Submission closes')
    expect(closes.getAttribute('min')).toBe(min)
  })


  it('names the tender in the heading and files its code with the other identity facts', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { ownerName: 'Rana Tester' }),
    })

    renderPage(<RfqDetailPage />)

    const heading = await screen.findByRole('heading', { level: 1 })
    expect(heading).toHaveTextContent('Sample RFQ')
    expect(heading).not.toHaveTextContent('RFQ-2026-000001')

    expect(screen.getByText(/RFQ-2026-000001 · Owner: Rana Tester/)).toBeInTheDocument()
  })

  it('says when submissions close, while they are open', async () => {
    const closes = new Date(Date.now() + 6 * 24 * 60 * 60 * 1000 + 60 * 60 * 1000).toISOString()
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen', { submissionClosesAt: closes }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('Closes in 6 days')).toBeInTheDocument()
  })

  it('does not count down on a tender whose submissions are not open', async () => {
    const closes = new Date(Date.now() + 6 * 24 * 60 * 60 * 1000 + 60 * 60 * 1000).toISOString()
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { submissionClosesAt: closes }),
    })

    renderPage(<RfqDetailPage />)

    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByText(/Closes in/)).toBeNull()
  })

  it('counts what is on the page, and counts only the questions still open', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ submittedProposalCount: 4 }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('SubmissionOpen', {
        invitations: [
          { id: 'i-1', supplierId: 's-1', supplierDisplayNameAr: 'أ', supplierDisplayNameEn: 'A', status: 'Invited', invitedAt: '2026-09-01T00:00:00Z', viewedAt: null, respondedAt: null, declineReason: null },
          { id: 'i-2', supplierId: 's-2', supplierDisplayNameAr: 'ب', supplierDisplayNameEn: 'B', status: 'Invited', invitedAt: '2026-09-01T00:00:00Z', viewedAt: null, respondedAt: null, declineReason: null },
        ],
        clarifications: [
          { id: 'c-1', askedBySupplierId: 's-1', askedBySupplierNameAr: 'أ', askedBySupplierNameEn: 'A', question: 'Open one', answer: null, visibility: 'PublishedToAll', askedAt: '2026-09-02T00:00:00Z', answeredAt: null },
          { id: 'c-2', askedBySupplierId: 's-2', askedBySupplierNameAr: 'ب', askedBySupplierNameEn: 'B', question: 'Answered one', answer: 'Yes', visibility: 'PublishedToAll', askedAt: '2026-09-02T00:00:00Z', answeredAt: '2026-09-03T00:00:00Z' },
        ],
      }),
    })

    renderPage(<RfqDetailPage />)

    await screen.findByText('At a glance')

    const glance = document.querySelector('dl')!
    const pairs = Object.fromEntries(
      [...glance.querySelectorAll('dt')].map((dt) => [dt.textContent, dt.nextElementSibling?.textContent]),
    )

    expect(pairs['Invited']).toBe('2')
    expect(pairs['Bids received']).toBe('4')
    expect(pairs['Questions open']).toBe('1')
  })


  it('Draft: the workspace panel shows the Draft stage as current and a blocked submit_review action with its reason', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({
        nextActions: [{ action: 'submit_review', labelAr: 'إرسال للمراجعة الداخلية', labelEn: 'Submit for internal review', permitted: false, blockedReasonAr: 'لا توجد بنود بعد.', blockedReasonEn: 'No items yet.' }],
      }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('What happens next')).toBeInTheDocument()
    expect(screen.getByText('Submit for internal review')).toBeInTheDocument()
    expect(screen.getByText('No items yet.')).toBeInTheDocument()

    const stages = screen.getByLabelText('Lifecycle stages')
    const current = within(stages).getByRole('listitem', { current: 'step' })
    expect(current).toHaveTextContent('Draft')
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
    const stages = screen.getByLabelText('Lifecycle stages')
    expect(within(stages).getByText('Draft')).toBeInTheDocument()
    expect(within(stages).getByText('Done:')).toBeInTheDocument()
    expect(within(stages).getByRole('listitem', { current: 'step' })).toHaveTextContent('Awarded')
  })

  it('Cancelled: the workspace panel shows a cancelled banner instead of stages or actions', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001/workspace': workspaceFixture({ rfqState: 'Cancelled', isCancelled: true, stages: [], nextActions: [] }),
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Cancelled'),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText('This tender has been cancelled.')).toBeInTheDocument()
  })


  it('hides the deadline control before the RFQ is published', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/RFQ-2026-000001/)).toBeInTheDocument()
    expect(screen.queryByLabelText('New deadline')).not.toBeInTheDocument()
  })

  it('lists the Tender attachments, downloads one, and offers upload only on a Draft', async () => {
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
    expect(screen.getByRole('button', { name: 'Download' })).toBeInTheDocument()
  })




  it('names the owner on the screen, and says "Unassigned" when there is none', async () => {
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft', { ownerUserId: 'u-1', ownerName: 'An Officer' }),
    })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/Owner: An Officer/)).toBeInTheDocument()
  })

  it('says "Unassigned" for an RFQ that predates ownership', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<RfqDetailPage />)

    expect(await screen.findByText(/Owner: Unassigned/)).toBeInTheDocument()
  })


  it('submits for review with the nominated approver, and without one when none is chosen', async () => {
    const calls: RecordedRequest[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001/submit-review': rfqFixture('InternalReview'),
    }, calls)

    renderPage(<RfqDetailPage />)

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

  it('offers the scoring screen only to an evaluator assigned to this tender', async () => {
    signInWith(['evaluation.assign', 'evaluation.consolidate'])
    restore = mockFetch(routes('UnderEvaluation', evaluation({
      assignments: [{
        evaluatorUserId: 'someone-else', evaluatorName: 'Nadia Suleiman',
        assignedAt: '2026-09-01T09:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null,
      }],
    })))

    renderPage(<RfqDetailPage />)

    await screen.findByText('Nadia Suleiman')
    expect(screen.queryByRole('link', { name: /my evaluation/i })).not.toBeInTheDocument()
  })

  it('offers it to the evaluator whose assignment it is', async () => {
    signInWith(['evaluation.score'])
    restore = mockFetch(routes('UnderEvaluation', evaluation({
      assignments: [{
        evaluatorUserId: 'u-manager-1', evaluatorName: 'The signed-in evaluator',
        assignedAt: '2026-09-01T09:00:00Z', submittedAt: null, recusedAt: null, recusalReason: null,
      }],
    })))

    renderPage(<RfqDetailPage />)

    expect(await screen.findByRole('link', { name: /my evaluation/i })).toBeInTheDocument()
  })

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
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('UnderEvaluation', evaluation(), [
      { userId: 'u-eval-1', fullName: 'Nadia Suleiman', email: 'nadia@example.test' },
    ]))

    renderPage(<RfqDetailPage />)

    await userEvent.click(await screen.findByRole('combobox', { name: /evaluator|المقيّم/i }))
    expect(await screen.findByRole('option', { name: /Nadia Suleiman/ })).toBeInTheDocument()
  })

  it('leaves an already-assigned evaluator out of the picker', async () => {
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
        proposalId: '9f1c2d3e-0000-4000-8000-000000000001', proposalCode: 'PRP-2026-000004',
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
      signInWith(['evaluation.assign'])
      restore = mockFetch(routes(state as RfqState, evaluation(), [
        { userId: 'u-eval-1', fullName: 'Nadia Suleiman', email: 'nadia@example.test' },
      ]))

      renderPage(<RfqDetailPage />)

      expect(await screen.findByRole('combobox', { name: /evaluator|المقيّم/i })).toBeInTheDocument()
    },
  )

  it('shows no evaluation panel while the RFQ is still open for bids', async () => {
    signInWith(['evaluation.assign'])
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<RfqDetailPage />)

    await screen.findByText(/Sample RFQ/)
    expect(screen.queryByRole('combobox', { name: /evaluator|المقيّم/i })).not.toBeInTheDocument()
  })

  it('links to the received proposals from SubmissionClosed onward', async () => {
    signInWith(['evaluation.assign'])
    restore = mockFetch(routes('SubmissionClosed', evaluation()))

    renderPage(<RfqDetailPage />)

    const link = await screen.findByRole('link', { name: /received proposals|العروض الواردة/i })
    expect(link).toHaveAttribute('href', '/back-office/rfqs/RFQ-2026-000001/proposals')
    expect(link.querySelector('button'), 'a button inside a link is invalid markup').toBeNull()
  })
})
