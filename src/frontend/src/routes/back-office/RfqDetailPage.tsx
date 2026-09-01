import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import {
  getRfq, addRfqItem, removeRfqItem, addRequirement, removeRequirement, bindEvaluationTemplate,
  submitRfqForReview, returnRfqForEdits, approveRfq, publishRfq, closeRfqSubmission, cancelRfq,
  inviteSupplier, suggestInvitationCandidates, answerClarification, publishClarification, issueAddendum,
  RfqApiError,
} from '../../api/rfqs'
import { listEvaluationTemplates } from '../../api/evaluationTemplates'
import { fetchCategories, fetchUnitsOfMeasure } from '../../api/reference'
import {
  getEvaluation, openEvaluation, assignEvaluators, recuseEvaluator, consolidateEvaluation, finalizeEvaluation, reopenEvaluation,
  EvaluationApiError,
} from '../../api/evaluations'

/** FEAT-07.1..07.10: the RFQ workspace. State-gated actions shown here are a UI convenience only
 * (hide, never gate, per this codebase's own established rule) - every action re-enforces its own
 * state guard server-side regardless of what this page shows. */
export function RfqDetailPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode' })
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [itemTitleAr, setItemTitleAr] = useState('')
  const [itemTitleEn, setItemTitleEn] = useState('')
  const [itemCategory, setItemCategory] = useState('')
  const [itemUom, setItemUom] = useState('')
  const [itemQty, setItemQty] = useState('1')
  const [reqTextAr, setReqTextAr] = useState('')
  const [reqTextEn, setReqTextEn] = useState('')
  const [reqMandatory, setReqMandatory] = useState(true)
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [returnComments, setReturnComments] = useState('')
  const [cancelReason, setCancelReason] = useState('')
  const [answerDrafts, setAnswerDrafts] = useState<Record<string, { text: string; publish: boolean }>>({})
  const [addendumTitleAr, setAddendumTitleAr] = useState('')
  const [addendumTitleEn, setAddendumTitleEn] = useState('')
  const [addendumDescAr, setAddendumDescAr] = useState('')
  const [addendumDescEn, setAddendumDescEn] = useState('')
  const [evaluatorUserId, setEvaluatorUserId] = useState('')
  const [recuseReason, setRecuseReason] = useState('')
  const [recuseTargetId, setRecuseTargetId] = useState('')
  const [reopenReason, setReopenReason] = useState('')

  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const unitsQuery = useQuery({ queryKey: ['units-of-measure'], queryFn: fetchUnitsOfMeasure })
  const templatesQuery = useQuery({ queryKey: ['evaluation-templates'], queryFn: listEvaluationTemplates })
  const candidatesQuery = useQuery({
    queryKey: ['rfq-candidates', referenceCode],
    queryFn: () => suggestInvitationCandidates(referenceCode),
  })
  const rfq = rfqQuery.data
  const evaluationEligible = !!rfq && ['SubmissionClosed', 'UnderEvaluation'].includes(rfq.state)
  const evaluationQuery = useQuery({
    queryKey: ['evaluation', referenceCode],
    queryFn: () => getEvaluation(referenceCode),
    enabled: evaluationEligible,
  })

  const categories = categoriesQuery.data ?? []
  const units = unitsQuery.data ?? []
  const activeTemplates = (templatesQuery.data ?? []).filter((tpl) => tpl.status === 'Active')
  const candidates = candidatesQuery.data ?? []
  const evaluation = evaluationQuery.data ?? null

  const errorMessage = (err: unknown, fallback: string) => (err instanceof RfqApiError ? err.message : fallback)
  const invalidate = () => invalidateQuietly(queryClient, { queryKey: ['rfq', referenceCode] })

  const addItemMutation = useMutation({
    mutationFn: () => addRfqItem(referenceCode, {
      titleAr: itemTitleAr, titleEn: itemTitleEn, specificationAr: null, specificationEn: null,
      categoryCode: itemCategory, quantity: Number(itemQty), unitOfMeasureCode: itemUom, isUnitPrice: true, isOptional: false,
    }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.itemAdded') }); setItemTitleAr(''); setItemTitleEn(''); setItemQty('1') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const removeItemMutation = useMutation({
    mutationFn: (itemId: string) => removeRfqItem(referenceCode, itemId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const addRequirementMutation = useMutation({
    mutationFn: () => addRequirement(referenceCode, { textAr: reqTextAr, textEn: reqTextEn, isMandatory: reqMandatory, documentTypeCode: null }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.requirementAdded') }); setReqTextAr(''); setReqTextEn('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const removeRequirementMutation = useMutation({
    mutationFn: (requirementId: string) => removeRequirement(referenceCode, requirementId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const bindTemplateMutation = useMutation({
    mutationFn: () => bindEvaluationTemplate(referenceCode, selectedTemplateId),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.templateBound') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const submitMutation = useMutation({
    mutationFn: () => submitRfqForReview(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.submitted') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const returnMutation = useMutation({
    mutationFn: () => returnRfqForEdits(referenceCode, returnComments),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.returned') }); setReturnComments('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const approveMutation = useMutation({
    mutationFn: () => approveRfq(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.approved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const publishMutation = useMutation({
    mutationFn: () => publishRfq(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.published') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const closeMutation = useMutation({
    mutationFn: () => closeRfqSubmission(referenceCode, t('rfq.manualCloseReason')),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.closed') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const inviteMutation = useMutation({
    mutationFn: (supplierId: string) => inviteSupplier(referenceCode, supplierId),
    onSuccess: () => {
      invalidate()
      invalidateQuietly(queryClient, { queryKey: ['rfq-candidates', referenceCode] })
      notify({ kind: 'success', title: t('rfq.invitations.invited') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.invitations.errors.inviteFailed')) }),
  })

  const answerMutation = useMutation({
    mutationFn: ({ clarificationId, answer, publish }: { clarificationId: string; answer: string; publish: boolean }) =>
      answerClarification(referenceCode, clarificationId, answer, publish),
    onSuccess: (_, { clarificationId }) => {
      invalidate()
      notify({ kind: 'success', title: t('rfq.clarifications.answered') })
      setAnswerDrafts((prev) => { const next = { ...prev }; delete next[clarificationId]; return next })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  const publishClarificationMutation = useMutation({
    mutationFn: (clarificationId: string) => publishClarification(referenceCode, clarificationId),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.clarifications.published') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  const addendumMutation = useMutation({
    mutationFn: () => issueAddendum(referenceCode, {
      titleAr: addendumTitleAr, titleEn: addendumTitleEn, descriptionAr: addendumDescAr, descriptionEn: addendumDescEn,
    }),
    onSuccess: () => {
      invalidate()
      notify({ kind: 'success', title: t('rfq.addenda.issued') })
      setAddendumTitleAr(''); setAddendumTitleEn(''); setAddendumDescAr(''); setAddendumDescEn('')
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.addenda.errors.issueFailed')) }),
  })

  const cancelMutation = useMutation({
    mutationFn: () => cancelRfq(referenceCode, cancelReason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.cancelled') }); setCancelReason('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const evaluationErrorMessage = (err: unknown, fallback: string) => (err instanceof EvaluationApiError ? err.message : fallback)
  const invalidateEvaluation = () => invalidateQuietly(queryClient, { queryKey: ['evaluation', referenceCode] })

  const openEvaluationMutation = useMutation({
    mutationFn: () => openEvaluation(referenceCode),
    onSuccess: () => { invalidate(); invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.opened') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const assignEvaluatorsMutation = useMutation({
    mutationFn: () => assignEvaluators(referenceCode, [evaluatorUserId]),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.assigned') }); setEvaluatorUserId('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const recuseEvaluatorMutation = useMutation({
    mutationFn: () => recuseEvaluator(referenceCode, recuseTargetId, recuseReason),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.recused') }); setRecuseReason(''); setRecuseTargetId('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const consolidateMutation = useMutation({
    mutationFn: () => consolidateEvaluation(referenceCode),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.consolidated') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const finalizeMutation = useMutation({
    mutationFn: () => finalizeEvaluation(referenceCode),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.finalized') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const reopenEvaluationMutation = useMutation({
    mutationFn: () => reopenEvaluation(referenceCode, reopenReason),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.reopened') }); setReopenReason('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  if (rfqQuery.isLoading || !rfq) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const isDraft = rfq.state === 'Draft'
  const isInternalReview = rfq.state === 'InternalReview'
  const isApproved = rfq.state === 'Approved'
  const isSubmissionOpen = rfq.state === 'SubmissionOpen'
  const canCancel = !['Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const canInvite = !['SubmissionClosed', 'UnderEvaluation', 'Clarification', 'Shortlisting', 'Recommendation', 'AwardApproval', 'Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const invitedSupplierIds = new Set(rfq.invitations.map((i) => i.supplierId))
  const uninvitedCandidates = candidates.filter((c) => !invitedSupplierIds.has(c.supplierId))
  const canIssueAddendum = rfq.state === 'Published' || rfq.state === 'SubmissionOpen'
  const draftFor = (id: string) => answerDrafts[id] ?? { text: '', publish: false }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {rfq.referenceCode} — {isArabic ? rfq.titleAr : rfq.titleEn}
          </h1>
          <Badge tone={rfq.state === 'Cancelled' ? 'danger' : 'info'}>{rfq.state}</Badge>
        </div>
        <div className="flex gap-2">
          {isDraft ? (
            <Button isLoading={submitMutation.isPending} onClick={() => submitMutation.mutate()}>{t('rfq.submitForReview')}</Button>
          ) : null}
          {isInternalReview ? (
            <Button isLoading={approveMutation.isPending} onClick={() => approveMutation.mutate()}>{t('rfq.approve')}</Button>
          ) : null}
          {isApproved ? (
            <Button isLoading={publishMutation.isPending} onClick={() => publishMutation.mutate()}>{t('rfq.publish')}</Button>
          ) : null}
          {isSubmissionOpen ? (
            <Button variant="secondary" isLoading={closeMutation.isPending} onClick={() => closeMutation.mutate()}>{t('rfq.closeSubmission')}</Button>
          ) : null}
        </div>
      </div>

      {isInternalReview ? (
        <Card title={t('rfq.returnForEditsTitle')}>
          <div className="flex gap-2">
            <Input aria-label={t('rfq.fields.comments')} placeholder={t('rfq.fields.comments')} value={returnComments} onChange={(e) => setReturnComments(e.target.value)} />
            <Button variant="ghost" isLoading={returnMutation.isPending} onClick={() => returnMutation.mutate()}>{t('rfq.returnForEdits')}</Button>
          </div>
        </Card>
      ) : null}

      <Card title={t('rfq.fields.items')}>
        {rfq.items.length > 0 ? (
          <Table caption={t('rfq.fields.items')}>
            <TableHead>
              <TableHeaderCell>#</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.category')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.quantity')}</TableHeaderCell>
              {isDraft ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {rfq.items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{item.lineNo}</TableCell>
                  <TableCell>{isArabic ? item.titleAr : item.titleEn}</TableCell>
                  <TableCell>{item.categoryCode}</TableCell>
                  <TableCell>{item.quantity}</TableCell>
                  {isDraft ? (
                    <TableCell>
                      <Button size="sm" variant="ghost" onClick={() => removeItemMutation.mutate(item.id)}>{t('rfq.remove')}</Button>
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noItems')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex flex-wrap items-end gap-2">
            <Input aria-label={t('rfq.fields.titleEn')} placeholder={t('rfq.fields.titleEn')} value={itemTitleEn} onChange={(e) => setItemTitleEn(e.target.value)} />
            <Input aria-label={t('rfq.fields.titleAr')} placeholder={t('rfq.fields.titleAr')} value={itemTitleAr} onChange={(e) => setItemTitleAr(e.target.value)} />
            <Select value={itemCategory} onValueChange={setItemCategory} placeholder={t('rfq.fields.category')}
              options={categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))} />
            <Select value={itemUom} onValueChange={setItemUom} placeholder={t('rfq.fields.unit')}
              options={units.map((u) => ({ value: u.code, label: isArabic ? u.nameAr : u.nameEn }))} />
            <Input type="number" aria-label={t('rfq.fields.quantity')} placeholder={t('rfq.fields.quantity')} value={itemQty} onChange={(e) => setItemQty(e.target.value)} className="w-24" />
            <Button size="sm" isLoading={addItemMutation.isPending} onClick={() => addItemMutation.mutate()}>{t('rfq.addItem')}</Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.fields.requirements')}>
        {rfq.requirements.length > 0 ? (
          <Table caption={t('rfq.fields.requirements')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.text')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.mandatory')}</TableHeaderCell>
              {isDraft ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {rfq.requirements.map((req) => (
                <TableRow key={req.id}>
                  <TableCell>{isArabic ? req.textAr : req.textEn}</TableCell>
                  <TableCell>{req.isMandatory ? t('rfq.yes') : t('rfq.no')}</TableCell>
                  {isDraft ? (
                    <TableCell>
                      <Button size="sm" variant="ghost" onClick={() => removeRequirementMutation.mutate(req.id)}>{t('rfq.remove')}</Button>
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noRequirements')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex flex-wrap items-end gap-2">
            <Input aria-label={t('rfq.fields.textEn')} placeholder={t('rfq.fields.textEn')} value={reqTextEn} onChange={(e) => setReqTextEn(e.target.value)} />
            <Input aria-label={t('rfq.fields.textAr')} placeholder={t('rfq.fields.textAr')} value={reqTextAr} onChange={(e) => setReqTextAr(e.target.value)} />
            <label className="flex items-center gap-1 text-[length:var(--text-body-sm)]">
              <input type="checkbox" checked={reqMandatory} onChange={(e) => setReqMandatory(e.target.checked)} />
              {t('rfq.fields.mandatory')}
            </label>
            <Button size="sm" isLoading={addRequirementMutation.isPending} onClick={() => addRequirementMutation.mutate()}>{t('rfq.addRequirement')}</Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.fields.evaluationTemplate')}>
        {rfq.evaluationTemplateId ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.boundTemplate', { id: rfq.evaluationTemplateId, version: rfq.evaluationTemplateVersion })}
          </p>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noTemplateBound')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex items-end gap-2">
            <Select value={selectedTemplateId} onValueChange={setSelectedTemplateId} placeholder={t('rfq.fields.evaluationTemplate')}
              options={activeTemplates.map((tpl) => ({ value: tpl.id, label: `${tpl.nameEn} (v${tpl.version})` }))} />
            <Button size="sm" isLoading={bindTemplateMutation.isPending} disabled={!selectedTemplateId} onClick={() => bindTemplateMutation.mutate()}>
              {t('rfq.bindTemplate')}
            </Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.invitations.title')}>
        {rfq.invitations.length > 0 ? (
          <Table caption={t('rfq.invitations.title')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.invitations.fields.supplier')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.invitedAt')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.viewedAt')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.declineReason')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.invitations.map((inv) => (
                <TableRow key={inv.id}>
                  <TableCell>{isArabic ? inv.supplierDisplayNameAr : inv.supplierDisplayNameEn}</TableCell>
                  <TableCell>
                    <Badge tone={inv.status === 'Declined' ? 'danger' : inv.status === 'Submitted' ? 'success' : 'info'}>{inv.status}</Badge>
                  </TableCell>
                  <TableCell>{new Date(inv.invitedAt).toLocaleDateString()}</TableCell>
                  <TableCell>{inv.viewedAt ? new Date(inv.viewedAt).toLocaleDateString() : '—'}</TableCell>
                  <TableCell>{inv.declineReason ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.invitations.none')}</p>
        )}
        {canInvite && uninvitedCandidates.length > 0 ? (
          <div className="mt-4">
            <p className="mb-2 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('rfq.invitations.candidatesTitle')}
            </p>
            <ul className="flex flex-col gap-2">
              {uninvitedCandidates.map((c) => (
                <li key={c.supplierId} className="flex items-center justify-between gap-2">
                  <span>{isArabic ? c.displayNameAr : c.displayNameEn} ({t('rfq.invitations.matchCount', { count: c.matchCount })})</span>
                  <Button size="sm" isLoading={inviteMutation.isPending} onClick={() => inviteMutation.mutate(c.supplierId)}>
                    {t('rfq.invitations.invite')}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.clarifications.title')}>
        {rfq.clarifications.length > 0 ? (
          <ul className="flex flex-col gap-4">
            {rfq.clarifications.map((c) => {
              const draft = draftFor(c.id)
              return (
                <li key={c.id} className="border-b pb-4 last:border-b-0" style={{ borderColor: 'var(--color-border)' }}>
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-[var(--fw-medium)]">{isArabic ? c.askedBySupplierNameAr : c.askedBySupplierNameEn}: {c.question}</p>
                    <Badge tone={c.visibility === 'PublishedToAll' ? 'success' : 'info'}>
                      {c.visibility === 'PublishedToAll' ? t('rfq.clarifications.published') : t('rfq.clarifications.private')}
                    </Badge>
                  </div>
                  {c.answer ? (
                    <p className="mt-1" style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.answerLabel')}: {c.answer}</p>
                  ) : (
                    <div className="mt-2 flex flex-wrap items-end gap-2">
                      <Input aria-label={t('rfq.clarifications.answerLabel')} placeholder={t('rfq.clarifications.answerLabel')}
                        value={draft.text} onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [c.id]: { ...draft, text: e.target.value } }))} />
                      <label className="flex items-center gap-1 text-[length:var(--text-body-sm)]">
                        <input type="checkbox" checked={draft.publish}
                          onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [c.id]: { ...draft, publish: e.target.checked } }))} />
                        {t('rfq.clarifications.publishNow')}
                      </label>
                      <Button size="sm" isLoading={answerMutation.isPending} disabled={!draft.text}
                        onClick={() => answerMutation.mutate({ clarificationId: c.id, answer: draft.text, publish: draft.publish })}>
                        {t('rfq.clarifications.answer')}
                      </Button>
                    </div>
                  )}
                  {c.answer && c.visibility === 'PrivateToAsker' ? (
                    <Button size="sm" variant="secondary" className="mt-2" isLoading={publishClarificationMutation.isPending}
                      onClick={() => publishClarificationMutation.mutate(c.id)}>
                      {t('rfq.clarifications.publish')}
                    </Button>
                  ) : null}
                </li>
              )
            })}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.none')}</p>
        )}
      </Card>

      <Card title={t('rfq.addenda.title')}>
        {rfq.addenda.length > 0 ? (
          <ul className="flex flex-col gap-2">
            {rfq.addenda.map((a) => (
              <li key={a.id}>
                <p className="font-[var(--fw-medium)]">{isArabic ? a.titleAr : a.titleEn}</p>
                <p style={{ color: 'var(--color-text-secondary)' }}>{isArabic ? a.descriptionAr : a.descriptionEn}</p>
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.addenda.none')}</p>
        )}
        {canIssueAddendum ? (
          <form className="mt-4 flex flex-col gap-2" onSubmit={(e) => { e.preventDefault(); addendumMutation.mutate() }}>
            <div className="flex flex-wrap gap-2">
              <Input aria-label={t('rfq.fields.titleEn')} placeholder={t('rfq.fields.titleEn')} value={addendumTitleEn} onChange={(e) => setAddendumTitleEn(e.target.value)} />
              <Input aria-label={t('rfq.fields.titleAr')} placeholder={t('rfq.fields.titleAr')} value={addendumTitleAr} onChange={(e) => setAddendumTitleAr(e.target.value)} />
            </div>
            <div className="flex flex-wrap gap-2">
              <Input aria-label={t('rfq.addenda.descriptionEn')} placeholder={t('rfq.addenda.descriptionEn')} value={addendumDescEn} onChange={(e) => setAddendumDescEn(e.target.value)} />
              <Input aria-label={t('rfq.addenda.descriptionAr')} placeholder={t('rfq.addenda.descriptionAr')} value={addendumDescAr} onChange={(e) => setAddendumDescAr(e.target.value)} />
            </div>
            <Button type="submit" size="sm" className="self-start" isLoading={addendumMutation.isPending}
              disabled={!addendumTitleAr || !addendumTitleEn || !addendumDescAr || !addendumDescEn}>
              {t('rfq.addenda.issue')}
            </Button>
          </form>
        ) : null}
      </Card>

      {rfq.approvals.length > 0 ? (
        <Card title={t('rfq.fields.approvals')}>
          <Table caption={t('rfq.fields.approvals')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.step')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.decision')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.comments')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.approvals.map((a) => (
                <TableRow key={a.stepNo}>
                  <TableCell>{a.stepNo}</TableCell>
                  <TableCell>{a.decision ? <Badge tone={a.decision === 'Approved' ? 'success' : 'warning'}>{a.decision}</Badge> : <Badge tone="info">{t('rfq.pending')}</Badge>}</TableCell>
                  <TableCell>{a.comment ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      ) : null}

      {evaluationEligible ? (
        <Card title={t('evaluation.title')}>
          <div className="mb-4">
            <a href={`/back-office/rfqs/${referenceCode}/comparison`}>
              <Button size="sm" variant="secondary">{t('comparison.title')}</Button>
            </a>
          </div>
          {!evaluation ? (
            rfq.state === 'SubmissionClosed' ? (
              <Button isLoading={openEvaluationMutation.isPending} onClick={() => openEvaluationMutation.mutate()}>
                {t('evaluation.open')}
              </Button>
            ) : (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.notOpened')}</p>
            )
          ) : (
            <div className="flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <Badge tone={evaluation.state === 'Finalized' ? 'success' : 'info'}>{evaluation.state}</Badge>
                {evaluation.state !== 'NotStarted' ? (
                  <a href={`/back-office/rfqs/${referenceCode}/my-evaluation`}>
                    <Button size="sm" variant="secondary">{t('evaluation.my.title')}</Button>
                  </a>
                ) : null}
              </div>

              <div>
                <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.criteria')}</p>
                <Table caption={t('evaluation.criteria')}>
                  <TableHead>
                    <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.dimension')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.weight')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.threshold')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.envelope')}</TableHeaderCell>
                  </TableHead>
                  <TableBody>
                    {evaluation.criteria.map((c) => (
                      <TableRow key={c.id}>
                        <TableCell>{isArabic ? c.nameAr : c.nameEn}</TableCell>
                        <TableCell>{c.dimension}</TableCell>
                        <TableCell>{c.weight}</TableCell>
                        <TableCell>{c.threshold ?? '—'}</TableCell>
                        <TableCell>
                          <Badge tone={c.isFinancial ? 'warning' : 'info'}>
                            {c.isFinancial ? t('evaluation.financialEnvelope') : t('evaluation.technicalEnvelope')}
                          </Badge>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>

              <div>
                <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.assignments')}</p>
                {evaluation.assignments.length > 0 ? (
                  <Table caption={t('evaluation.assignments')}>
                    <TableHead>
                      <TableHeaderCell>{t('evaluation.evaluator')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.submittedAt')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.recusedAt')}</TableHeaderCell>
                      {evaluation.state !== 'Finalized' ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
                    </TableHead>
                    <TableBody>
                      {evaluation.assignments.map((a) => (
                        <TableRow key={a.evaluatorUserId}>
                          <TableCell>{a.evaluatorUserId}</TableCell>
                          <TableCell>{a.submittedAt ? new Date(a.submittedAt).toLocaleString() : '—'}</TableCell>
                          <TableCell>{a.recusedAt ? t('evaluation.recusedWithReason', { reason: a.recusalReason }) : '—'}</TableCell>
                          {evaluation.state !== 'Finalized' ? (
                            <TableCell>
                              {!a.recusedAt && !a.submittedAt ? (
                                <Button size="sm" variant="ghost" onClick={() => setRecuseTargetId(a.evaluatorUserId)}>
                                  {t('evaluation.recuse')}
                                </Button>
                              ) : null}
                            </TableCell>
                          ) : null}
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                ) : (
                  <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.noAssignments')}</p>
                )}

                {evaluation.state !== 'Finalized' && evaluation.state !== 'Consolidated' ? (
                  <div className="mt-4 flex flex-wrap items-end gap-2">
                    <Input aria-label={t('evaluation.evaluatorUserId')} placeholder={t('evaluation.evaluatorUserId')}
                      value={evaluatorUserId} onChange={(e) => setEvaluatorUserId(e.target.value)} />
                    <Button size="sm" isLoading={assignEvaluatorsMutation.isPending} disabled={!evaluatorUserId}
                      onClick={() => assignEvaluatorsMutation.mutate()}>
                      {t('evaluation.assign')}
                    </Button>
                  </div>
                ) : null}

                {recuseTargetId ? (
                  <div className="mt-2 flex flex-wrap items-end gap-2">
                    <Input aria-label={t('evaluation.recuseReason')} placeholder={t('evaluation.recuseReason')}
                      value={recuseReason} onChange={(e) => setRecuseReason(e.target.value)} />
                    <Button size="sm" variant="ghost" isLoading={recuseEvaluatorMutation.isPending} disabled={!recuseReason}
                      onClick={() => recuseEvaluatorMutation.mutate()}>
                      {t('evaluation.confirmRecuse')}
                    </Button>
                  </div>
                ) : null}
              </div>

              {evaluation.results.length > 0 ? (
                <div>
                  <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.results')}</p>
                  <Table caption={t('evaluation.results')}>
                    <TableHead>
                      <TableHeaderCell>{t('evaluation.rank')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.proposal')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.qualified')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.technicalScore')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.financialScore')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.total')}</TableHeaderCell>
                    </TableHead>
                    <TableBody>
                      {[...evaluation.results].sort((a, b) => (a.rank ?? 999) - (b.rank ?? 999)).map((r) => (
                        <TableRow key={r.proposalId}>
                          <TableCell>{r.rank ?? '—'}</TableCell>
                          <TableCell>{r.proposalId}</TableCell>
                          <TableCell>
                            <Badge tone={r.technicallyQualified ? 'success' : 'danger'}>
                              {r.technicallyQualified ? t('evaluation.qualifiedYes') : t('evaluation.qualifiedNo')}
                            </Badge>
                          </TableCell>
                          <TableCell>{r.technicalWeightedScore.toFixed(2)}</TableCell>
                          <TableCell>{r.financialWeightedScore !== null ? r.financialWeightedScore.toFixed(2) : '—'}</TableCell>
                          <TableCell>{r.weightedTotal.toFixed(2)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              ) : null}

              <div className="flex flex-wrap gap-2">
                {evaluation.state === 'EvaluatorSubmitted' ? (
                  <Button isLoading={consolidateMutation.isPending} onClick={() => consolidateMutation.mutate()}>
                    {t('evaluation.consolidate')}
                  </Button>
                ) : null}
                {evaluation.state === 'Consolidated' ? (
                  <Button isLoading={finalizeMutation.isPending} onClick={() => finalizeMutation.mutate()}>
                    {t('evaluation.finalize')}
                  </Button>
                ) : null}
              </div>

              {evaluation.state === 'Consolidated' ? (
                <div className="flex flex-wrap items-end gap-2">
                  <Input aria-label={t('evaluation.reopenReason')} placeholder={t('evaluation.reopenReason')}
                    value={reopenReason} onChange={(e) => setReopenReason(e.target.value)} />
                  <Button size="sm" variant="ghost" isLoading={reopenEvaluationMutation.isPending} disabled={!reopenReason}
                    onClick={() => reopenEvaluationMutation.mutate()}>
                    {t('evaluation.reopen')}
                  </Button>
                </div>
              ) : null}
            </div>
          )}
        </Card>
      ) : null}

      {canCancel ? (
        <Card title={t('rfq.cancelTitle')}>
          <div className="flex gap-2">
            <Input aria-label={t('rfq.fields.reason')} placeholder={t('rfq.fields.reason')} value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
            <Button variant="ghost" isLoading={cancelMutation.isPending} disabled={!cancelReason} onClick={() => cancelMutation.mutate()}>
              {t('rfq.cancelRfq')}
            </Button>
          </div>
        </Card>
      ) : null}
    </div>
  )
}
