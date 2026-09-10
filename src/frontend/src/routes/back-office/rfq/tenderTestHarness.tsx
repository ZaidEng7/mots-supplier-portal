import type { Rfq, RfqState } from '../../../api/rfqs'

/**
 * What every tender-tab test needs before it can render one: the router answers the strip asks, and a
 * tender to answer them about.
 *
 * <p>The tender's own screens were one file until the tab strip split them into four. Each new file
 * arrived carrying a copy of this fixture, which is how three near-identical harnesses end up drifting
 * apart one edit at a time. One copy, and a test that needs a different tender passes overrides.</p>
 */
export const TENDER_CODE = 'RFQ-2026-000001'

/**
 * A real anchor that resolves `to` + `params` into an href.
 *
 * <p>Exported rather than inlined per test file: the strip is six links, a stub that threw their
 * destinations away would let a wrong route pass unnoticed, and four files were carrying a copy of it.
 * `vi.mock` itself stays in each file, because vitest hoists it to the top of the file it appears in
 * and it cannot be called from here.</p>
 */
export function TestLink({ to, params, children, ...rest }: {
  to: string
  params?: Record<string, string>
  children: React.ReactNode
}) {
  const href = Object.entries(params ?? {}).reduce((path, [key, value]) => path.replace(`$${key}`, value), to)
  return <a href={href} {...rest}>{children}</a>
}

export function rfqFixture(state: RfqState, overrides: Partial<Rfq> = {}): Rfq {
  return {
    referenceCode: TENDER_CODE, organizationId: 'org-1', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
    descriptionAr: null, descriptionEn: null, currencyCode: 'SYP', state,
    publishAt: null, submissionOpensAt: null, submissionClosesAt: null, clarificationDeadlineAt: null,
    evaluationTargetDate: null, evaluationTemplateId: null, evaluationTemplateVersion: null, cancelReason: null,
    items: [], requirements: [], attachments: [], approvals: [], invitations: [], clarifications: [], addenda: [],
    // A-7: unowned by default, which is what every RFQ created before ownership existed looks like.
    ownerUserId: null, ownerName: null, assignedApproverUserId: null, assignedApproverName: null,
    ...overrides,
  }
}

/** The routes every tender tab fetches whatever it is showing. */
export const TENDER_ROUTES = {
  [`/api/v1/rfqs/${TENDER_CODE}/workspace`]: {
    rfqReferenceCode: TENDER_CODE, rfqState: 'Draft', isCancelled: false, submittedProposalCount: 0,
    evaluationState: null, awardState: null,
    stages: [{ key: 'Draft', isCurrent: true, isCompleted: false }], nextActions: [],
  },
  [`/api/v1/rfqs/${TENDER_CODE}/assignees`]: {
    owners: [{ userId: 'u-officer-2', fullName: 'Second Officer' }],
    approvers: [{ userId: 'u-manager-1', fullName: 'A Manager' }],
  },
  [`/api/v1/rfqs/${TENDER_CODE}/invitations/candidates`]: [
    { supplierId: 's-9', displayNameAr: 'مورد مقترح', displayNameEn: 'Suggested Supplier', categoryCodes: [] },
  ],
}
