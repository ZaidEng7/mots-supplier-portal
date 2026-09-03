import { formatCurrency } from '../lib/datetime'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Button, Card, Input, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../components/ui'
import { invalidateQuietly } from '../lib/queryClient'
import { getInvitedRfq } from '../api/supplierRfqs'
import {
  startProposal, getProposal, setItemPricing, setCommercialTerms, answerRequirement,
  addProposalDocument, removeProposalDocument, submitProposal, withdrawProposal, ProposalApiError,
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
  const [currencyCode, setCurrencyCode] = useState('SYP')
  const [paymentTerms, setPaymentTerms] = useState('')
  const [incotermCode, setIncotermCode] = useState('')
  const [validityEnd, setValidityEnd] = useState('')
  const [withdrawReason, setWithdrawReason] = useState('')

  const rfqQuery = useQuery({ queryKey: ['supplier-rfq', referenceCode], queryFn: () => getInvitedRfq(referenceCode) })
  const proposalQuery = useQuery({
    queryKey: ['proposal', referenceCode],
    queryFn: () => getProposal(referenceCode),
    retry: false,
  })

  // §12-A/C2: every mutation below addresses the proposal by its OWN public code, not by the RFQ's.
  // `referenceCode` from the route is the RFQ; the proposal's code comes back on the fetch above.
  // Both are strings, so passing the wrong one type-checks - hence the named local rather than
  // threading `referenceCode` into functions that no longer mean it.
  const proposalCode = proposalQuery.data?.referenceCode ?? ''

  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['proposal', referenceCode] })
  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof ProposalApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof ProposalApiError ? err.message : fallback

  const startMutation = useMutation({
    mutationFn: () => startProposal(referenceCode),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.startFailed')) }),
  })

  const priceMutation = useMutation({
    mutationFn: ({ rfqItemId, quantity, unitPrice }: { rfqItemId: string; quantity: number; unitPrice: number }) =>
      setItemPricing(proposalCode, rfqItemId, { quantity, unitPrice, discount: null, leadTimeDays: null, notesAr: null, notesEn: null }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.itemPriced') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.saveFailed')) }),
  })

  const answerMutation = useMutation({
    mutationFn: ({ requirementId, ar, en }: { requirementId: string; ar: string; en: string }) =>
      answerRequirement(proposalCode, requirementId, ar, en),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.requirementAnswered') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.saveFailed')) }),
  })

  const termsMutation = useMutation({
    mutationFn: () => setCommercialTerms(proposalCode, {
      currencyCode, paymentTerms: paymentTerms || null, incotermCode: incotermCode || null,
      deliveryTermsAr: null, deliveryTermsEn: null, warranty: null,
      validityStart: null, validityEnd: validityEnd || null,
    }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.termsSaved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.saveFailed')) }),
  })

  const documentMutation = useMutation({
    mutationFn: (file: File) => addProposalDocument(proposalCode, file),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('proposal.documentAdded') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.saveFailed')) }),
  })

  const removeDocumentMutation = useMutation({
    mutationFn: (documentId: string) => removeProposalDocument(proposalCode, documentId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('proposal.errors.saveFailed')) }),
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
          {t('proposal.title')} — {rfq.referenceCode}
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
            {t('proposal.title')} — {rfq.referenceCode}
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
                  <TableCell>{item.quantity}</TableCell>
                  <TableCell>{priced ? formatCurrency(priced.unitPrice, proposal.currencyCode, locale) : '—'}</TableCell>
                  <TableCell>{priced ? formatCurrency(priced.lineTotal, proposal.currencyCode, locale) : '—'}</TableCell>
                  {isDraft ? (
                    <TableCell>
                      <div className="flex items-center gap-1">
                        <Input type="number" aria-label={`${t('rfq.fields.quantity')} - ${item.titleEn}`} placeholder={t('rfq.fields.quantity')}
                          value={draft.quantity || (priced?.quantity ?? '')} className="w-20"
                          onChange={(e) => setPricingDrafts((prev) => ({ ...prev, [item.id]: { ...draft, quantity: e.target.value } }))} />
                        <Input type="number" aria-label={`${t('proposal.unitPrice')} - ${item.titleEn}`} placeholder={t('proposal.unitPrice')}
                          value={draft.unitPrice || (priced?.unitPrice ?? '')} className="w-24"
                          onChange={(e) => setPricingDrafts((prev) => ({ ...prev, [item.id]: { ...draft, unitPrice: e.target.value } }))} />
                        <Button size="sm" isLoading={priceMutation.isPending}
                          onClick={() => priceMutation.mutate({
                            rfqItemId: item.id,
                            quantity: Number(draft.quantity || priced?.quantity || item.quantity),
                            unitPrice: Number(draft.unitPrice || priced?.unitPrice || 0),
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
            <dt style={{ color: 'var(--color-text-secondary)' }}>{t('proposal.currency')}</dt><dd>{proposal.currencyCode ?? '—'}</dd>
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
    </div>
  )
}
