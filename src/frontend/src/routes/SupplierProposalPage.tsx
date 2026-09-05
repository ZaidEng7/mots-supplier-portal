import { formatCurrency, formatNumber } from '../lib/datetime'
import { useState } from 'react'
import { Dialog } from '../components/ui/Dialog'
import { useTranslation } from 'react-i18next'
import { getPublicSettings } from '../api/systemSettings'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Button, Card, Input, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../components/ui'
import { invalidateQuietly } from '../lib/queryClient'
import { getInvitedRfq } from '../api/supplierRfqs'
import {
  startProposal, getProposal, patchProposal,
  addProposalDocument, removeProposalDocument, submitProposal, withdrawProposal, declineAwardOffer, ProposalApiError,
} from '../api/proposals'

/** FEAT-09.1..09.6/FR-PRP-001..008: the supplier's own proposal workspace against one invited RFQ.
 * OQ-009 two-envelope: the pricing table below IS the financial envelope - this page is the
 * owning supplier's own view, the one case where both envelopes are shown together (their own
 * bid). State-gated actions here are a UI convenience only (hide, never gate, per this
 * codebase's established rule) - every write re-enforces its own Draft-only guard
 * server-side. */
export function SupplierProposalPage() {
  const { referenceCode } = useParams({ strict: false }) as { referenceCode: string }
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [pricingDrafts, setPricingDrafts] = useState<Record<string, { quantity: string; unitPrice: string }>>({})
  const [answerDrafts, setAnswerDrafts] = useState<Record<string, { ar: string; en: string }>>({})
  // T-060: the default currency is a system setting (BR-18/FR-ADM-006), not a literal here. SYP is
  // still the fallback - it is the setting's own default, so an unreachable settings read behaves
  // exactly as this line did before.
  const publicSettings = useQuery({ queryKey: ['public-settings'], queryFn: getPublicSettings })
  const defaultCurrency = publicSettings.data?.['proposals.defaultCurrencyCode'] ?? 'SYP'
  const [currencyOverride, setCurrencyOverride] = useState<string | null>(null)
  // Null means "the supplier has not chosen", so the setting's value can still arrive after the first
  // render without discarding a currency they typed.
  const currencyCode = currencyOverride ?? defaultCurrency
  const setCurrencyCode = setCurrencyOverride
  const [paymentTerms, setPaymentTerms] = useState('')
  const [incotermCode, setIncotermCode] = useState('')
  const [validityEnd, setValidityEnd] = useState('')
  const [withdrawReason, setWithdrawReason] = useState('')
  const [declineReason, setDeclineReason] = useState('')

  const rfqQuery = useQuery({ queryKey: ['supplier-rfq', referenceCode], queryFn: () => getInvitedRfq(referenceCode) })
  const proposalQuery = useQuery({
    queryKey: ['proposal', referenceCode],
    queryFn: () => getProposal(referenceCode),
    retry: false,
  })

  // §12-A/C2: every mutation below addresses the proposal by its OWN public code, not by the RFQ's.
  // `referenceCode` from the route is the RFQ; the proposal's code comes back on the fetch above.
  // Both are strings, so passing the wrong one type-checks - hence the named local rather than
  // threading `referenceCode` into functions that no longer mean it. R-9 renamed the response
  // field to `proposalCode`, which says the same thing the local was invented to say.
  const proposalCode = proposalQuery.data?.proposalCode ?? ''

  // SCR-151: "*Concurrency conflict:* `Dialog` 'This proposal changed in another tab/user' →
  // reload/merge." §8.1 delivers it as a 412 ETAG_MISMATCH. Reload is offered; MERGE is not, because
  // there is no merge UI in this codebase and inventing one here would be a screen nobody specified
  // - reported rather than approximated.
  const [conflictOpen, setConflictOpen] = useState(false)

  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['proposal', referenceCode] })
  /** A 412 is not a message - it is a state the supplier has to resolve. Everything else is a toast. */
  const onMutationError = (err: unknown, fallback: string) => {
    if (err instanceof ProposalApiError && err.isConcurrencyConflict) {
      setConflictOpen(true)
      return
    }
    notify({ kind: 'danger', title: errorMessage(err, fallback) })
  }

  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof ProposalApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof ProposalApiError ? err.message : fallback

  const startMutation = useMutation({
    mutationFn: () => startProposal(referenceCode),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.startFailed')) }),
  })

  // §12.5: one PATCH per edit, and the items array is sent WHOLE because RFC 7396 replaces an array
  // rather than merging into it. Pricing one line therefore means sending every priced line - which
  // is also what makes removing one possible now that DELETE /items/{id} is gone.
  const pricedItems = (proposalQuery.data?.items ?? []).map((i) => ({
    rfqItemId: i.rfqItemId, quantity: i.quantity, unitPrice: i.unitPrice,
    discount: i.discount, leadTimeDays: i.leadTimeDays, notesAr: i.notesAr, notesEn: i.notesEn,
  }))

  const priceMutation = useMutation({
    mutationFn: ({ rfqItemId, quantity, unitPrice }: { rfqItemId: string; quantity: number; unitPrice: number }) =>
      patchProposal(proposalCode, {
        items: [...pricedItems.filter((i) => i.rfqItemId !== rfqItemId), { rfqItemId, quantity, unitPrice }],
      }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.itemPriced') }) },
    onError: (err) => onMutationError(err, t('proposal.errors.saveFailed')),
  })

  const answerMutation = useMutation({
    mutationFn: ({ requirementId, ar, en }: { requirementId: string; ar: string; en: string }) =>
      patchProposal(proposalCode, { technicalResponse: { answers: [{ requirementId, answerAr: ar, answerEn: en }] } }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.requirementAnswered') }) },
    onError: (err) => onMutationError(err, t('proposal.errors.saveFailed')),
  })

  const termsMutation = useMutation({
    // Only the fields this form owns are mentioned. Sending deliveryTerms/warranty as null here -
    // which the per-field PUT did - would DELETE values the supplier set elsewhere, because under
    // merge patch an explicit null is a delete rather than "no opinion".
    mutationFn: () => patchProposal(proposalCode, {
      commercialTerms: {
        currencyCode, paymentTerms: paymentTerms || null, incotermCode: incotermCode || null,
        validityEnd: validityEnd || null,
      },
    }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.termsSaved') }) },
    onError: (err) => onMutationError(err, t('proposal.errors.saveFailed')),
  })

  const documentMutation = useMutation({
    mutationFn: (file: File) => addProposalDocument(proposalCode, file),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.documentAdded') }) },
    onError: (err) => onMutationError(err, t('proposal.errors.saveFailed')),
  })

  const removeDocumentMutation = useMutation({
    mutationFn: (documentId: string) => removeProposalDocument(proposalCode, documentId),
    onSuccess: () => invalidate(),
    onError: (err) => onMutationError(err, t('proposal.errors.saveFailed')),
  })

  const submitMutation = useMutation({
    mutationFn: () => submitProposal(proposalCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.submitted') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.submitFailed')) }),
  })

  const withdrawMutation = useMutation({
    mutationFn: () => withdrawProposal(proposalCode, withdrawReason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.withdrawn') }); setWithdrawReason('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.withdrawFailed')) }),
  })

  const declineMutation = useMutation({
    mutationFn: () => declineAwardOffer(proposalCode, declineReason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.declined') }); setDeclineReason('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.declineFailed')) }),
  })

  if (rfqQuery.isLoading || proposalQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }
  if (rfqQuery.isError || !rfqQuery.data) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.notFound')}</p>
  }

  const rfq = rfqQuery.data
  const proposal = proposalQuery.data

  if (!proposal) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('proposal.title')} — {rfq.rfqCode}
        </h1>
        <Button isLoading={startMutation.isPending} onClick={() => startMutation.mutate()} className="self-start">
          {t('proposal.start')}
        </Button>
      </div>
    )
  }

  const isDraft = proposal.state === 'Draft'
  const canWithdraw = proposal.state === 'Draft' || proposal.state === 'Submitted'
  const pricingFor = (rfqItemId: string) => pricingDrafts[rfqItemId] ?? { quantity: '', unitPrice: '' }
  const answerFor = (requirementId: string) => {
    const existing = proposal.requirementAnswers.find((a) => a.requirementId === requirementId)
    return answerDrafts[requirementId] ?? { ar: existing?.answerAr ?? '', en: existing?.answerEn ?? '' }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('proposal.title')} — {rfq.rfqCode}
          </h1>
          <StatusChip machine="proposal" value={proposal.state} />
        </div>
        {isDraft ? (
          <Button isLoading={submitMutation.isPending} onClick={() => submitMutation.mutate()}>{t('proposal.submit')}</Button>
        ) : null}
      </div>

      <Card title={t('proposal.pricing')}>
        <Table caption={t('proposal.pricing')}>
          <TableHead>
            <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
            <TableHeaderCell>{t('rfq.fields.quantity')}</TableHeaderCell>
            <TableHeaderCell>{t('proposal.unitPrice')}</TableHeaderCell>
            <TableHeaderCell>{t('proposal.lineTotal')}</TableHeaderCell>
            {isDraft ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
          </TableHead>
          <TableBody>
            {rfq.items.map((item) => {
              const priced = proposal.items.find((i) => i.rfqItemId === item.id)
              const draft = pricingFor(item.id)
              return (
                <TableRow key={item.id}>
                  <TableCell>{isArabic ? item.titleAr : item.titleEn}{item.isOptional ? null : <span aria-hidden="true"> *</span>}</TableCell>
                  <TableCell>{formatNumber(item.quantity, locale, 0)}</TableCell>
                  <TableCell>{priced ? formatCurrency(priced.unitPrice, proposal.currency, locale) : '—'}</TableCell>
                  <TableCell>{priced ? formatCurrency(priced.lineTotal, proposal.currency, locale) : '—'}</TableCell>
                  {isDraft ? (
                    <TableCell>
                      <div className="flex items-center gap-1">
                        <Input type="number" aria-label={`${t('rfq.fields.quantity')} - ${item.titleEn}`} placeholder={t('rfq.fields.quantity')}
                          value={draft.quantity || (priced?.quantity ?? '')} className="w-20"
                          onChange={(e) => setPricingDrafts((prev) => ({ ...prev, [item.id]: { ...draft, quantity: e.target.value } }))} />
                        <Input type="number" aria-label={`${t('proposal.unitPrice')} - ${item.titleEn}`} placeholder={t('proposal.unitPrice')}
                          value={draft.unitPrice || (priced?.unitPrice ?? '')} className="w-24"
                          onChange={(e) => setPricingDrafts((prev) => ({ ...prev, [item.id]: { ...draft, unitPrice: e.target.value } }))} />
                        {/* A blank price is not a price. This used to coerce an empty input to 0
                            and send it, which recorded a free bid before §7.2's rule and produces an
                            unexplained 422 after it - the button is simply disabled instead. */}
                        <Button size="sm" isLoading={priceMutation.isPending}
                          disabled={!(draft.unitPrice || priced?.unitPrice)}
                          onClick={() => priceMutation.mutate({
                            rfqItemId: item.id,
                            quantity: Number(draft.quantity || priced?.quantity || item.quantity),
                            unitPrice: Number(draft.unitPrice || priced?.unitPrice),
                          })}>
                          {t('proposal.savePrice')}
                        </Button>
                      </div>
                    </TableCell>
                  ) : null}
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </Card>

      <Card title={t('proposal.requirements')}>
        {rfq.requirements.length > 0 ? (
          <ul className="flex flex-col gap-3">
            {rfq.requirements.map((req) => {
              const draft = answerFor(req.id)
              return (
                <li key={req.id}>
                  <p className="font-[var(--fw-medium)]">{isArabic ? req.textAr : req.textEn}{req.isMandatory ? <span aria-hidden="true"> *</span> : null}</p>
                  {isDraft ? (
                    <div className="mt-1 flex flex-wrap items-end gap-2">
                      <Input aria-label={`${t('rfq.fields.textEn')} - ${req.textEn}`} placeholder={t('rfq.fields.textEn')}
                        value={draft.en} onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [req.id]: { ...draft, en: e.target.value } }))} />
                      <Input aria-label={`${t('rfq.fields.textAr')} - ${req.textEn}`} placeholder={t('rfq.fields.textAr')}
                        value={draft.ar} onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [req.id]: { ...draft, ar: e.target.value } }))} />
                      <Button size="sm" isLoading={answerMutation.isPending} disabled={!draft.ar || !draft.en}
                        onClick={() => answerMutation.mutate({ requirementId: req.id, ar: draft.ar, en: draft.en })}>
                        {t('proposal.saveAnswer')}
                      </Button>
                    </div>
                  ) : (
                    <p style={{ color: 'var(--color-text-secondary)' }}>
                      {proposal.requirementAnswers.find((a) => a.requirementId === req.id)?.[isArabic ? 'answerAr' : 'answerEn'] ?? '—'}
                    </p>
                  )}
                </li>
              )
            })}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('proposal.noRequirements')}</p>
        )}
      </Card>

      <Card title={t('proposal.terms')}>
        {isDraft ? (
          <div className="flex flex-wrap items-end gap-2">
            <Input aria-label={t('proposal.currency')} placeholder={t('proposal.currency')} value={currencyCode} onChange={(e) => setCurrencyCode(e.target.value)} className="w-20" />
            <Input aria-label={t('proposal.paymentTerms')} placeholder={t('proposal.paymentTerms')} value={paymentTerms} onChange={(e) => setPaymentTerms(e.target.value)} />
            <Input aria-label={t('proposal.incoterm')} placeholder={t('proposal.incoterm')} value={incotermCode} onChange={(e) => setIncotermCode(e.target.value)} className="w-24" />
            <Input type="date" aria-label={t('proposal.validityEnd')} value={validityEnd} onChange={(e) => setValidityEnd(e.target.value)} />
            <Button size="sm" isLoading={termsMutation.isPending} onClick={() => termsMutation.mutate()}>{t('proposal.saveTerms')}</Button>
          </div>
        ) : (
          <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-[length:var(--text-body-sm)]">
            <dt style={{ color: 'var(--color-text-secondary)' }}>{t('proposal.currency')}</dt><dd>{proposal.currency ?? '—'}</dd>
            <dt style={{ color: 'var(--color-text-secondary)' }}>{t('proposal.validityEnd')}</dt><dd>{proposal.validityEnd ?? '—'}</dd>
          </dl>
        )}
      </Card>

      <Card title={t('proposal.documents')}>
        {proposal.documents.length > 0 ? (
          <ul className="flex flex-col gap-1">
            {proposal.documents.map((d) => (
              <li key={d.id} className="flex items-center justify-between gap-2">
                <span>{d.originalFileName}</span>
                {isDraft ? (
                  <Button size="sm" variant="ghost" onClick={() => removeDocumentMutation.mutate(d.id)}>{t('rfq.remove')}</Button>
                ) : null}
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('proposal.noDocuments')}</p>
        )}
        {isDraft ? (
          <input type="file" aria-label={t('proposal.uploadDocument')} className="mt-3"
            onChange={(e) => { const file = e.target.files?.[0]; if (file) documentMutation.mutate(file) }} />
        ) : null}
      </Card>

      {/*
        T-064: the offer, and the only action that answers it. An AwardOffered proposal with no
        decline control on the screen is the same defect shape as T-067 - a state the product can
        reach and the persona it concerns cannot act on. Accepting is not a supplier action: §4.1
        gives AwardOffered -> Awarded to the manager (award/execute), and "or supplier accept" is
        tagged [ASSUMPTION] - see DECISIONS-TAKEN.md D-21.
      */}
      {proposal.state === 'AwardOffered' ? (
        <Card title={t('proposal.awardOfferedTitle')}>
          <p className="mb-3">{t('proposal.awardOfferedBody')}</p>
          <div className="flex gap-2">
            <Input aria-label={t('proposal.declineReason')} placeholder={t('proposal.declineReasonPlaceholder')}
              value={declineReason} onChange={(e) => setDeclineReason(e.target.value)} />
            <Button variant="ghost" isLoading={declineMutation.isPending} disabled={!declineReason}
              onClick={() => declineMutation.mutate()}>
              {t('proposal.decline')}
            </Button>
          </div>
        </Card>
      ) : null}

      {canWithdraw ? (
        <Card title={t('proposal.withdrawTitle')}>
          <div className="flex gap-2">
            <Input aria-label={t('rfq.fields.reason')} placeholder={t('proposal.withdrawReasonPlaceholder')}
              value={withdrawReason} onChange={(e) => setWithdrawReason(e.target.value)} />
            <Button variant="ghost" isLoading={withdrawMutation.isPending} disabled={!withdrawReason} onClick={() => withdrawMutation.mutate()}>
              {t('proposal.withdraw')}
            </Button>
          </div>
        </Card>
      ) : null}

      <Dialog
        open={conflictOpen}
        onOpenChange={setConflictOpen}
        title={t('proposal.conflictTitle')}
        description={t('proposal.conflictBody')}
      >
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConflictOpen(false)}>{t('common.cancel')}</Button>
          <Button onClick={() => { setConflictOpen(false); invalidate() }}>{t('proposal.conflictReload')}</Button>
        </div>
      </Dialog>
    </div>
  )
}
