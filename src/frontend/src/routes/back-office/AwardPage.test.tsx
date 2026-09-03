import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch } from '../../test/renderPage'
import type { Award } from '../../api/awards'
import type { Evaluation } from '../../api/evaluations'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, useParams: () => ({ referenceCode: 'RFQ-2026-000001' }) }
})

const { AwardPage } = await import('./AwardPage')

function evaluationFixture(overrides: Partial<Evaluation> = {}): Evaluation {
  return {
    id: 'eval-1', rfqId: 'rfq-1', rfqReferenceCode: 'RFQ-2026-000001', state: 'Finalized',
    criteria: [], assignments: [],
    results: [
      { proposalId: 'proposal-a', technicallyQualified: true, technicalWeightedScore: 80, financialWeightedScore: 30, weightedTotal: 110, rank: 1 },
    ],
    ...overrides,
  }
}

function awardFixture(overrides: Partial<Award> = {}): Award {
  return {
    id: 'award-1', rfqReferenceCode: 'RFQ-2026-000001', state: 'PendingApproval',
    winningProposalId: 'proposal-a', justificationAr: 'الأفضل', justificationEn: 'Best overall',
    recommendedByUserId: 'user-1', recommendedAt: '2026-08-01T00:00:00Z', recommendationRevision: 1,
    approvals: [{ stepNo: 1, approverUserId: null, decision: null, comment: null, decidedAt: null }],
    awardedAt: null, comparisonSnapshotJson: null,
    erpSyncStatus: 'NotRequested', externalPurchaseOrderRef: null, erpSyncedAt: null, erpRetryCount: 0,
    ...overrides,
  }
}

/** FEAT-14.1..14.6: proves the page shows the right action for each Award state, and surfaces the
 * segregation-of-duties error with its own dedicated message rather than the generic fallback. */
describe('AwardPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('no award yet: shows the recommend form built from qualified evaluation results', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/award': null,
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture(),
    })

    renderPage(<AwardPage />)

    expect(await screen.findByText('No winner recommended yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recommend winner' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Select the winning proposal' })).toBeInTheDocument()
  })

  it('PendingApproval: shows approve/reject, and approving shows a success toast', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/award': awardFixture(),
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture(),
    })

    renderPage(<AwardPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByText('Award approved')).toBeInTheDocument()
  })

  it('a segregation-of-duties refusal shows its own dedicated error message', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/award': awardFixture(),
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture(),
      // mockFetch is method-agnostic and always 200s; simulate the SoD refusal shape the API
      // would actually return by overriding the award route's own body for this test's approve
      // click - since mockFetch always answers 200 for any method, exercise the client-side error
      // mapping directly via a rejected promise instead.
    })

    renderPage(<AwardPage />)
    await screen.findByRole('button', { name: 'Approve' })
    restore()

    const { AwardApiError } = await import('../../api/awards')
    vi.spyOn(await import('../../api/awards'), 'approveAward').mockRejectedValueOnce(
      new AwardApiError(400, { code: 'SEGREGATION_OF_DUTIES_VIOLATION', detail: 'Segregation of duties: the approver must differ from the recommender.' }),
    )
    restore = mockFetch({ '/api/v1/rfqs/RFQ-2026-000001/award': awardFixture(), '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture() })
    renderPage(<AwardPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByText('The approver must differ from the recommender')).toBeInTheDocument()
  })

  it('Awarded with a failed ERP sync: shows the retry button', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/award': awardFixture({ state: 'Awarded', awardedAt: '2026-08-05T00:00:00Z', erpSyncStatus: 'Failed', erpRetryCount: 1 }),
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture(),
    })

    renderPage(<AwardPage />)

    expect(await screen.findByText('ERP sync status: Failed')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry sync' }))

    expect(await screen.findByText('Sync retry queued')).toBeInTheDocument()
  })

  it('Awarded and synced: shows the external PO reference, no retry button', async () => {
    restore = mockFetch({
      '/api/v1/rfqs/RFQ-2026-000001/award': awardFixture({ state: 'Awarded', awardedAt: '2026-08-05T00:00:00Z', erpSyncStatus: 'Synced', externalPurchaseOrderRef: 'PO-000123' }),
      '/api/v1/rfqs/RFQ-2026-000001/evaluation': evaluationFixture(),
    })

    renderPage(<AwardPage />)

    expect(await screen.findByText('Purchase order reference: PO-000123')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry sync' })).not.toBeInTheDocument()
  })
})
