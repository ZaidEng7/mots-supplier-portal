import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import {
  getRfq, addRfqItem, removeRfqItem, addRequirement, removeRequirement, bindEvaluationTemplate,
  submitRfqForReview, returnRfqForEdits, approveRfq, publishRfq, closeRfqSubmission, cancelRfq,
  RfqApiError,
} from '../../api/rfqs'
import { listEvaluationTemplates } from '../../api/evaluationTemplates'
import { fetchCategories, fetchUnitsOfMeasure } from '../../api/reference'

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

  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const unitsQuery = useQuery({ queryKey: ['units-of-measure'], queryFn: fetchUnitsOfMeasure })
  const templatesQuery = useQuery({ queryKey: ['evaluation-templates'], queryFn: listEvaluationTemplates })

  const rfq = rfqQuery.data
  const categories = categoriesQuery.data ?? []
  const units = unitsQuery.data ?? []
  const activeTemplates = (templatesQuery.data ?? []).filter((tpl) => tpl.status === 'Active')

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

  const cancelMutation = useMutation({
    mutationFn: () => cancelRfq(referenceCode, cancelReason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.cancelled') }); setCancelReason('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  if (rfqQuery.isLoading || !rfq) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  const isDraft = rfq.state === 'Draft'
  const isInternalReview = rfq.state === 'InternalReview'
  const isApproved = rfq.state === 'Approved'
  const isSubmissionOpen = rfq.state === 'SubmissionOpen'
  const canCancel = !['Awarded', 'Completed', 'Cancelled'].includes(rfq.state)

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
