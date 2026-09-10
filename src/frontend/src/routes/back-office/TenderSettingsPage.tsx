import { useState } from 'react'
import { useParams } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../../lib/queryClient'
import { Button, Card, Input, PageHeading, QueryError, Select, SkeletonList, StatusChip, useToast } from '../../components/ui'
import { apiErrorMessage } from '../../api/problem'
import {
  getRfq,
  reassignRfq,
  returnRfqForEdits,
  changeSubmissionDeadline,
  issueAddendum,
  cancelRfq,
  listRfqAssignees,
} from '../../api/rfqs'
import { CancelSection } from './rfq/sections/CancelSection'
import { TenderTabs } from './rfq/TenderTabs'

/**
 * Everything done TO a tender rather than everything the tender IS: who owns it, when it closes, what
 * has been issued against it, and calling it off.
 *
 * <p><b>Why this is its own screen.</b> The workspace stacked these five forms below the tender's own
 * contents, so a buyer opening a tender to read its line items scrolled past a reassignment control, a
 * deadline control, an addendum form and a cancel button to reach them. The comp files them behind a
 * Settings tab for the same reason every application does: they are rare, they are consequential, and
 * they are not what the page is about.</p>
 *
 * <p>Nothing here changed except where it lives. Every gate is the one it had - hide-never-gate
 * throughout, because the endpoints re-enforce permissions the UI does not hold.</p>
 */
export function TenderSettingsPage() {
  const { referenceCode } = useParams({ strict: false }) as { referenceCode: string }
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const [newOwnerDraft, setNewOwnerDraft] = useState('')
  const [reassignReason, setReassignReason] = useState('')
  const [deadlineDraft, setDeadlineDraft] = useState('')
  const [deadlineReason, setDeadlineReason] = useState('')
  const [returnComments, setReturnComments] = useState('')
  const [addendumTitleAr, setAddendumTitleAr] = useState('')
  const [addendumTitleEn, setAddendumTitleEn] = useState('')
  const [addendumDescAr, setAddendumDescAr] = useState('')
  const [addendumDescEn, setAddendumDescEn] = useState('')

  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const assigneesQuery = useQuery({
    queryKey: ['rfq-assignees', referenceCode],
    queryFn: () => listRfqAssignees(referenceCode),
  })
  const rfq = rfqQuery.data

  const errorMessage = (err: unknown, fallback: string) =>
    apiErrorMessage(err, fallback, t('common.concurrencyConflict'))
  const invalidate = () => {
    invalidateQuietly(queryClient, { queryKey: ['rfq', referenceCode] })
    invalidateQuietly(queryClient, { queryKey: ['workspace', referenceCode] })
  }

  const reassignMutation = useMutation({
    mutationFn: () => reassignRfq(referenceCode, newOwnerDraft, reassignReason),
    onSuccess: () => {
      invalidate()
      // The list's owner column and the "mine" filter both read from a different query.
      invalidateQuietly(queryClient, { queryKey: ['rfqs'] })
      notify({ kind: 'success', title: t('rfq.ownership.reassigned') })
      setNewOwnerDraft(''); setReassignReason('')
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const returnMutation = useMutation({
    mutationFn: () => returnRfqForEdits(referenceCode, returnComments),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.returned') }); setReturnComments('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const deadlineMutation = useMutation({
    mutationFn: ({ deadline, reason }: { deadline: string; reason: string }) =>
      changeSubmissionDeadline(referenceCode, new Date(deadline).toISOString(), reason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.deadline.changed') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.deadline.failed')) }),
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
    // The reason comes from the section's dialog rather than from state up here: it is that section's
    // own working value and nothing else reads it.
    mutationFn: (reason: string) => cancelRfq(referenceCode, reason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.cancelled') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  if (rfqQuery.isError) return <QueryError error={rfqQuery.error} onRetry={() => void rfqQuery.refetch()} />
  if (rfqQuery.isLoading || !rfq) return <SkeletonList label={t('common.loading')} />

  const canCancel = !['Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const isInternalReview = rfq.state === 'InternalReview'
  const canIssueAddendum = rfq.state === 'Published' || rfq.state === 'SubmissionOpen'

  return (
    <div className="flex flex-col gap-6">
      <PageHeading
        title={isArabic ? rfq.titleAr : rfq.titleEn}
        subtitle={rfq.referenceCode}
        meta={<StatusChip machine="rfq" value={rfq.state} />}
      />

      <TenderTabs referenceCode={referenceCode} />

      <div className="flex flex-col gap-4">
          {/* A-7. Shown for every non-closed state rather than only Draft: ownership moves when people
              do, not when a tender does. Hide-never-gate as everywhere else - the endpoint re-enforces
              rfq.reassign, which officers do not hold, so an officer sees this card and gets a 403 rather
              than being told the control does not exist. */}
          {canCancel ? (
            <Card title={t('rfq.ownership.title')}>
              <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t('rfq.ownership.help')}
              </p>
              <div className="flex flex-wrap items-end gap-2">
                <Select
                  aria-label={t('rfq.ownership.newOwner')}
                  placeholder={t('rfq.ownership.newOwner')}
                  value={newOwnerDraft}
                  onValueChange={setNewOwnerDraft}
                  options={(assigneesQuery.data?.owners ?? []).map((o) => ({ value: o.userId, label: o.fullName }))}
                />
                {/* Mandatory. The audit row is the whole point of this operation, and a row saying only
                    that ownership moved answers nothing a month later. */}
                <Input
                  aria-label={t('rfq.ownership.reason')}
                  placeholder={t('rfq.ownership.reason')}
                  value={reassignReason}
                  onChange={(e) => setReassignReason(e.target.value)}
                />
                <Button
                  variant="secondary"
                  isLoading={reassignMutation.isPending}
                  disabled={!newOwnerDraft || !reassignReason}
                  onClick={() => reassignMutation.mutate()}
                >
                  {t('rfq.ownership.reassign')}
                </Button>
              </div>
            </Card>
          ) : null}

          {/*
            * F-4: the tender's own fields, editable while Draft - which is what Draft is documented to mean.
            *
            * `PUT /api/v1/rfqs/{code}` and `updateRfqBasics` both existed and nothing called either, so an
            * officer who typed the submission window wrongly at creation had two options: cancel the tender
            * and author it again, or ask an engineer to issue the PUT. Both happened during the walkthrough.
            *
            * Draft only, and that is the domain's rule rather than this screen's: UpdateBasics calls
            * EnsureDraftEditable, because bidders price against what they were shown.
            */}
          {/* T-018/BRULE-035: changeable while Published or SubmissionOpen, the same two states the
              domain accepts. Same gate as the addendum control above, and for the same reason. */}
          {canIssueAddendum ? (
            <Card title={t('rfq.deadline.title')}>
              <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t('rfq.deadline.help')}
              </p>
              <div className="flex flex-wrap items-end gap-2">
                <Input
                  type="datetime-local"
                  aria-label={t('rfq.deadline.newDeadline')}
                  value={deadlineDraft}
                  onChange={(e) => setDeadlineDraft(e.target.value)}
                />
                {/* A-6: mandatory. A deadline moved with no stated basis is what the ruling exists to
                    prevent, and the supplier reads this on their own view of the RFQ. */}
                <Input
                  aria-label={t('rfq.deadline.reason')}
                  placeholder={t('rfq.deadline.reason')}
                  value={deadlineReason}
                  onChange={(e) => setDeadlineReason(e.target.value)}
                />
                <Button
                  variant="secondary"
                  isLoading={deadlineMutation.isPending}
                  disabled={!deadlineDraft || !deadlineReason}
                  onClick={() => deadlineMutation.mutate({ deadline: deadlineDraft, reason: deadlineReason })}
                >
                  {t('rfq.deadline.apply')}
                </Button>
              </div>
            </Card>
          ) : null}

          {isInternalReview ? (
            <Card title={t('rfq.returnForEditsTitle')}>
              <div className="flex gap-2">
                <Input aria-label={t('rfq.fields.comments')} placeholder={t('rfq.fields.comments')} value={returnComments} onChange={(e) => setReturnComments(e.target.value)} />
                <Button variant="ghost" isLoading={returnMutation.isPending} onClick={() => returnMutation.mutate()}>{t('rfq.returnForEdits')}</Button>
              </div>
            </Card>
          ) : null}

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

          {canCancel ? (
            <CancelSection onCancel={(reason) => cancelMutation.mutate(reason)} isPending={cancelMutation.isPending} />
          ) : null}
      </div>
    </div>
  )
}
