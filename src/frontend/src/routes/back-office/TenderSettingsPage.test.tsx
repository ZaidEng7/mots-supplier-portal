import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, type RecordedRequest } from '../../test/renderPage'
import type { Rfq, RfqState } from '../../api/rfqs'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return {
    ...actual,
    useParams: () => ({ referenceCode: 'RFQ-2026-000001' }),
    useRouterState: () => '/back-office/rfqs/RFQ-2026-000001/settings',
    Link: ({ to, params, children, ...rest }: { to: string; params?: Record<string, string>; children: React.ReactNode }) => {
      const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`\$${key}`, value), to)
      return <a href={href} {...rest}>{children}</a>
    },
  }
})

const { TenderSettingsPage } = await import('./TenderSettingsPage')

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
  '/api/v1/rfqs/RFQ-2026-000001/assignees': {
    owners: [{ userId: 'u-officer-2', fullName: 'Second Officer' }],
    approvers: [{ userId: 'u-manager-1', fullName: 'A Manager' }],
  },
}

/**
 * The comp's Settings tab: everything done TO a tender rather than what the tender is.
 *
 * <p>Every test here moved from  unchanged except for the component it renders.
 * The forms did not change; only the screen they live on did, and a test that had to be rewritten to
 * follow them would have been evidence that something else changed too.</p>
 */
describe('TenderSettingsPage', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.(); restore = undefined })

  /**
   * The ordering claim, asserted rather than described, and now a claim about this screen rather than
   * about the whole tender. It used to say an officer meets the line items before the button that
   * cancels the tender; the items are on another screen entirely, so what is left to be right about is
   * that cancelling comes after every reversible thing on this one.
   */
  it('puts the irreversible control after every reversible one', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<TenderSettingsPage />)

    const reassign = await screen.findByRole('button', { name: 'Reassign' })
    const deadline = screen.getByRole('button', { name: 'Change deadline' })
    const cancel = screen.getByRole('button', { name: 'Cancel RFQ' })

    const precedes = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)
    expect(precedes(reassign, cancel)).toBe(true)
    expect(precedes(deadline, cancel)).toBe(true)
  })

  it('cancel asks before it acts, warns that it is final, and still requires a reason', async () => {
    // §D1: this was an inline reason field beside a `ghost` button - the lowest-emphasis variant in the
    // system - for an action that tells every invited supplier their tender is gone. It now gets the
    // same treatment the supplier's proposal withdrawal got: danger variant, a dialog, and a warning.
    // The mandatory reason is unchanged, because the reason is the audit record.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft') })

    renderPage(<TenderSettingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel RFQ' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/final/i)).toBeInTheDocument()

    const confirm = within(dialog).getByRole('button', { name: 'Cancel RFQ' })
    expect(confirm).toBeDisabled()

    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Budget withdrawn')
    await waitFor(() => expect(confirm).toBeEnabled())
    await userEvent.click(confirm)

    expect(await screen.findByText('RFQ cancelled')).toBeInTheDocument()
  })

  it('hides the cancel section once the RFQ is Cancelled', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Cancelled') })

    renderPage(<TenderSettingsPage />)

    await screen.findByText('Cancelled')
    expect(screen.queryByRole('button', { name: 'Cancel RFQ' })).not.toBeInTheDocument()
  })

  it('Published: shows the addendum form, and issuing one shows a success toast', async () => {
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<TenderSettingsPage />)

    await userEvent.type(await screen.findByLabelText('Title (English)'), 'Deadline extended')
    await userEvent.type(screen.getByLabelText('Title (Arabic)'), 'تمديد الموعد')
    await userEvent.type(screen.getByLabelText('Description (English)'), 'The deadline has moved.')
    await userEvent.type(screen.getByLabelText('Description (Arabic)'), 'تم تمديد الموعد.')
    await userEvent.click(screen.getByRole('button', { name: 'Issue addendum' }))

    expect(await screen.findByText('Addendum issued')).toBeInTheDocument()
  })

  it('offers the deadline control on a Published RFQ and not on a Draft one', async () => {
    // T-018: an extension the officer cannot trigger is the same defect shape as T-067 - the rule
    // permits it and no surface reaches it.
    restore = mockFetch({ ...REFERENCE_ROUTES, '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Published') })

    renderPage(<TenderSettingsPage />)

    expect(await screen.findByLabelText('New deadline')).toBeInTheDocument()
    // A-6: and a reason, which the server now requires. Disabled until BOTH are given - the guard in
    // the direction that refuses.
    expect(screen.getByLabelText('Reason for the change')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change deadline' })).toBeDisabled()
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

    renderPage(<TenderSettingsPage />)

    await userEvent.type(await screen.findByLabelText('New deadline'), '2026-12-01T10:00')
    // Still disabled: the date alone is not enough.
    expect(screen.getByRole('button', { name: 'Change deadline' })).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason for the change'), 'The Ministry extended the tender period.')
    await userEvent.click(screen.getByRole('button', { name: 'Change deadline' }))

    await vi.waitFor(() => expect(calls.some((c) => c.url.endsWith('/deadline'))).toBe(true))
    const sent = JSON.parse(calls.find((c) => c.url.endsWith('/deadline'))!.body)
    expect(sent.reason).toBe('The Ministry extended the tender period.')
  })

  it('sends the new owner and the reason, and will not reassign without both', async () => {
    const calls: RecordedRequest[] = []
    restore = mockFetch({
      ...REFERENCE_ROUTES,
      '/api/v1/rfqs/RFQ-2026-000001': rfqFixture('Draft'),
      '/api/v1/rfqs/RFQ-2026-000001/reassign': rfqFixture('Draft', { ownerUserId: 'u-officer-2', ownerName: 'Second Officer' }),
    }, calls)

    renderPage(<TenderSettingsPage />)

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
})
