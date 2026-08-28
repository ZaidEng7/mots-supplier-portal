import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { Badge, Button, Dialog } from '../components/ui'
import { useToast } from '../components/ui'
import {
  getReviewerSupplierView,
  pickUpApplication,
  approveApplication,
  rejectApplication,
  changeSupplierLifecycle,
  type SupplierLifecycleAction,
  requestApplicationInfo,
  ReviewApiError,
} from '../api/review'
import { getDocumentDownloadUrl, approveDocument, rejectDocument, DocumentApiError } from '../api/documents'
import { PROFILE_DISPLAY_FIELDS, profileDisplayValue } from './profileDisplayFields'
import { lifecycleActionsFor } from './lifecycleActions'
import { ReasonDialog } from '../components/ReasonDialog'

// MSP-77: must match Domain/Suppliers/ProfileFieldCodes.cs exactly - the backend now rejects
// unknown codes, and these are the codes the server enforces the supplier's edit restriction
// against. Previously this list and the supplier screen's list were invented independently and
// overlapped on one entry, so flagging e.g. 'registrationNumber' unlocked nothing for the supplier.
const PROFILE_FIELDS = [
  'description', 'website', 'supplierGroup', 'currencyCode', 'primaryContactPhone',
  'legalInfo', 'address', 'contact', 'representative', 'branch', 'bankAccount', 'categoryLink', 'logo',
] as const

function RequestInfoDialog({
  open,
  onOpenChange,
  onSubmit,
  isLoading,
  documentTypeCodes,
}: {
  open: boolean
  onOpenChange: (v: boolean) => void
  onSubmit: (reason: string, fields: string[], docCodes: string[]) => void
  isLoading: boolean
  documentTypeCodes: { code: string; label: string }[]
}) {
  const { t } = useTranslation()
  const [reason, setReason] = useState('')
  const [fields, setFields] = useState<string[]>([])
  const [docCodes, setDocCodes] = useState<string[]>([])

  const toggle = (list: string[], set: (v: string[]) => void, value: string) => {
    set(list.includes(value) ? list.filter((v) => v !== value) : [...list, value])
  }

  const canSubmit = reason.trim().length > 0 && (fields.length > 0 || docCodes.length > 0)

  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={t('review.requestInfo')}>
      <div className="flex flex-col gap-4">
        <textarea
          className="rounded-[0.375rem] p-2"
          style={{ border: '1px solid var(--color-border-input)', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-primary)' }}
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={t('review.reason')}
        />
        <fieldset className="flex flex-col gap-1.5">
          <legend className="text-[length:var(--text-body-sm)] font-[var(--fw-medium)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('review.flagProfileFields')}
          </legend>
          {/* The flagged-field CODES, not the display fields - these are what the supplier is asked
              to correct, and they must match ProfileFieldCodes.cs exactly (MSP-77). Conflating the
              two lists is what crashed the profile grid below. */}
          {PROFILE_FIELDS.map((f) => (
            <label key={f} className="flex items-center gap-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
              <input type="checkbox" checked={fields.includes(f)} onChange={() => toggle(fields, setFields, f)} />
              {t(`onboarding.fields.${f}`)}
            </label>
          ))}
        </fieldset>
        <fieldset className="flex flex-col gap-1.5">
          <legend className="text-[length:var(--text-body-sm)] font-[var(--fw-medium)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('review.flagDocuments')}
          </legend>
          {documentTypeCodes.map((d) => (
            <label key={d.code} className="flex items-center gap-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
              <input type="checkbox" checked={docCodes.includes(d.code)} onChange={() => toggle(docCodes, setDocCodes, d.code)} />
              {d.label}
            </label>
          ))}
        </fieldset>
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            {t('review.cancel')}
          </Button>
          <Button isLoading={isLoading} disabled={!canSubmit} onClick={() => onSubmit(reason, fields, docCodes)}>
            {t('review.submit')}
          </Button>
        </div>
      </div>
    </Dialog>
  )
}

export function ReviewApplicationPage() {
  const { referenceCode } = useParams({ from: '/back-office/review/$referenceCode' })
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [rejectOpen, setRejectOpen] = useState(false)
  const [infoOpen, setInfoOpen] = useState(false)
  const [lifecycleAction, setLifecycleAction] = useState<SupplierLifecycleAction | null>(null)

  const viewQuery = useQuery({
    queryKey: ['review-application', referenceCode],
    queryFn: () => getReviewerSupplierView(referenceCode),
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['review-application', referenceCode] })
    queryClient.invalidateQueries({ queryKey: ['review-queue'] })
  }

  const pickUpMutation = useMutation({
    mutationFn: () => pickUpApplication(referenceCode),
    onSuccess: invalidate,
    onError: (err) => notify({ kind: 'danger', title: t('review.pickUpFailed'), description: err instanceof ReviewApiError ? err.message : undefined }),
  })

  const approveMutation = useMutation({
    mutationFn: () => approveApplication(referenceCode),
    onSuccess: () => {
      invalidate()
      notify({ kind: 'success', title: t('review.decisionSuccess') })
    },
    onError: (err) => notify({ kind: 'danger', title: t('review.approveFailed'), description: err instanceof ReviewApiError ? err.message : undefined }),
  })

  const rejectMutation = useMutation({
    mutationFn: (reason: string) => rejectApplication(referenceCode, reason),
    onSuccess: () => {
      invalidate()
      setRejectOpen(false)
      notify({ kind: 'success', title: t('review.decisionSuccess') })
    },
    onError: (err) => notify({ kind: 'danger', title: t('review.rejectFailed'), description: err instanceof ReviewApiError ? err.message : undefined }),
  })

  const requestInfoMutation = useMutation({
    mutationFn: ({ reason, fields, docCodes }: { reason: string; fields: string[]; docCodes: string[] }) =>
      requestApplicationInfo(referenceCode, reason, fields, docCodes),
    onSuccess: () => {
      invalidate()
      setInfoOpen(false)
      notify({ kind: 'success', title: t('review.decisionSuccess') })
    },
    onError: (err) => notify({ kind: 'danger', title: t('review.requestInfoFailed'), description: err instanceof ReviewApiError ? err.message : undefined }),
  })

  const lifecycleMutation = useMutation({
    mutationFn: ({ action, reason }: { action: SupplierLifecycleAction; reason: string }) =>
      changeSupplierLifecycle(referenceCode, action, reason),
    onSuccess: () => {
      invalidate()
      setLifecycleAction(null)
      notify({ kind: 'success', title: t('review.decisionSuccess') })
    },
    // The server's 409 message names which state is required (NFR-CMP-003/BRULE-097). Surfaced
    // rather than swallowed: the UI hides inapplicable actions, but if one is somehow attempted the
    // reviewer should see why it was refused.
    onError: (err) => notify({ kind: 'danger', title: t('review.lifecycleFailed'), description: err instanceof ReviewApiError ? err.message : undefined }),
  })

  const downloadMutation = useMutation({
    mutationFn: getDocumentDownloadUrl,
    onSuccess: (url) => window.open(url, '_blank', 'noopener,noreferrer'),
  })

  const approveDocMutation = useMutation({
    mutationFn: approveDocument,
    onSuccess: invalidate,
    onError: (err) => notify({ kind: 'danger', title: t('review.approveFailed'), description: err instanceof DocumentApiError ? err.message : undefined }),
  })

  const rejectDocMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => rejectDocument(id, reason),
    onSuccess: invalidate,
    onError: (err) => notify({ kind: 'danger', title: t('review.rejectFailed'), description: err instanceof DocumentApiError ? err.message : undefined }),
  })

  if (viewQuery.isLoading) return <p style={{ color: 'var(--color-text-secondary)' }}>...</p>
  const view = viewQuery.data
  if (!view) return <p style={{ color: 'var(--color-text-secondary)' }}>{t('errors.notFound')}</p>

  const { supplier, documents, annotationHistory } = view
  const state = supplier.onboardingState
  const canPickUp = state === 'Submitted' || state === 'Resubmitted'
  const canDecide = state === 'UnderReview'

  // FR-ONB-009 lifecycle actions, offered only where the domain would accept them. This mirrors the
  // server's rules; it does not replace them - the endpoints reject illegal transitions regardless.
  const lifecycle = supplier.lifecycleState
  const { canSuspend, canReactivate, canDeactivate } = lifecycleActionsFor(lifecycle)

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <Link to="/back-office/review" style={{ color: 'var(--color-text-brand)' }}>
            {t('review.backToQueue')}
          </Link>
          <h1 className="mt-2 text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {isArabic ? supplier.displayNameAr : supplier.displayNameEn}
          </h1>
        </div>
        <div className="flex items-center gap-2">
          <Badge tone={state === 'InfoRequested' ? 'warning' : 'info'}>{state}</Badge>
          {/* Lifecycle is shown only once it has begun; None would be noise on an application
              that has not been approved yet. */}
          {lifecycle !== 'None' ? (
            <Badge tone={lifecycle === 'Active' ? 'success' : 'danger'}>{lifecycle}</Badge>
          ) : null}
        </div>
      </div>

      <div className="flex gap-3">
        {canPickUp ? (
          <Button isLoading={pickUpMutation.isPending} onClick={() => pickUpMutation.mutate()}>
            {t('review.pickUp')}
          </Button>
        ) : null}
        {canDecide ? (
          <>
            <Button isLoading={approveMutation.isPending} onClick={() => approveMutation.mutate()}>
              {t('review.approve')}
            </Button>
            <Button variant="danger" onClick={() => setRejectOpen(true)}>
              {t('review.reject')}
            </Button>
            <Button variant="secondary" onClick={() => setInfoOpen(true)}>
              {t('review.requestInfo')}
            </Button>
          </>
        ) : null}
        {canSuspend ? (
          <Button variant="secondary" onClick={() => setLifecycleAction('suspend')}>
            {t('review.suspend')}
          </Button>
        ) : null}
        {canReactivate ? (
          <Button variant="secondary" onClick={() => setLifecycleAction('reactivate')}>
            {t('review.reactivate')}
          </Button>
        ) : null}
        {canDeactivate ? (
          <Button variant="danger" onClick={() => setLifecycleAction('deactivate')}>
            {t('review.deactivate')}
          </Button>
        ) : null}
      </div>

      <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('review.profile')}
        </h2>
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {PROFILE_DISPLAY_FIELDS.map((f) => (
            <div key={f}>
              <dt className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t(`onboarding.fields.${f}`)}
              </dt>
              <dd style={{ color: 'var(--color-text-primary)' }}>{profileDisplayValue(supplier, f)}</dd>
            </div>
          ))}
        </dl>
      </div>

      <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('review.documents')}
        </h2>
        <ul className="flex flex-col gap-2">
          {documents.map((doc) => (
            <li key={doc.documentTypeId} className="flex items-center justify-between gap-3 rounded-[0.375rem] p-3" style={{ border: '1px solid var(--color-border)' }}>
              <div className="flex items-center gap-2">
                <Badge tone={doc.latestDocument?.state === 'Rejected' || doc.latestDocument?.state === 'Expired' ? 'danger' : 'neutral'}>
                  {doc.latestDocument?.state ?? t('onboarding.missing')}
                </Badge>
                <span style={{ color: 'var(--color-text-primary)' }}>{isArabic ? doc.nameAr : doc.nameEn}</span>
              </div>
              {doc.latestDocument ? (
                <div className="flex items-center gap-2">
                  <Button variant="ghost" size="sm" isLoading={downloadMutation.isPending} onClick={() => downloadMutation.mutate(doc.latestDocument!.id)}>
                    {t('onboarding.download')}
                  </Button>
                  {doc.latestDocument.state === 'Uploaded' || doc.latestDocument.state === 'UnderReview' ? (
                    <>
                      <Button size="sm" isLoading={approveDocMutation.isPending} onClick={() => approveDocMutation.mutate(doc.latestDocument!.id)}>
                        {t('review.approve')}
                      </Button>
                      <Button
                        variant="danger"
                        size="sm"
                        isLoading={rejectDocMutation.isPending}
                        onClick={() => {
                          const reason = window.prompt(t('review.reason')) ?? ''
                          if (reason.trim()) rejectDocMutation.mutate({ id: doc.latestDocument!.id, reason })
                        }}
                      >
                        {t('review.reject')}
                      </Button>
                    </>
                  ) : null}
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      </div>

      {annotationHistory.length > 0 ? (
        <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
          <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('review.annotationHistory')}
          </h2>
          <ul className="flex flex-col gap-3">
            {annotationHistory.map((a) => (
              <li key={a.id} className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
                <div className="flex items-center gap-2">
                  <Badge tone={a.resolvedAt ? 'success' : 'warning'}>{a.resolvedAt ? t('onboarding.complete') : t('onboarding.missing')}</Badge>
                  <span>{new Date(a.requestedAt).toLocaleString()}</span>
                </div>
                <p style={{ color: 'var(--color-text-secondary)' }}>{a.reason}</p>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {/* Keyed by action so React REMOUNTS it on each open. Without this the dialog keeps its
          previous reason: opening Deactivate straight after a Suspend pre-fills the suspension's
          text, and the reviewer can commit it without noticing. The reason is the audit record
          (BRULE-096), so a stale one is a false record, not a cosmetic annoyance. Found by opening
          the page rather than by any test. */}
      <ReasonDialog
        key={lifecycleAction ?? 'none'}
        open={lifecycleAction !== null}
        onOpenChange={(v) => setLifecycleAction(v ? lifecycleAction : null)}
        isLoading={lifecycleMutation.isPending}
        title={lifecycleAction ? t(`review.${lifecycleAction}`) : ''}
        confirmLabel={lifecycleAction ? t(`review.${lifecycleAction}`) : ''}
        variant={lifecycleAction === 'reactivate' ? 'primary' : 'danger'}
        warning={lifecycleAction === 'deactivate' ? t('review.deactivateWarning') : undefined}
        onSubmit={(reason) => lifecycleAction && lifecycleMutation.mutate({ action: lifecycleAction, reason })}
      />

      {/* Reject uses the same ReasonDialog as the lifecycle actions. It had its own near-identical
          component; keeping both would have meant two copies of the same mandatory-reason form
          drifting apart, which Sonar flagged as duplication on this very PR. */}
      <ReasonDialog
        key="reject"
        open={rejectOpen}
        onOpenChange={setRejectOpen}
        isLoading={rejectMutation.isPending}
        title={t('review.reject')}
        confirmLabel={t('review.reject')}
        variant="danger"
        onSubmit={(reason) => rejectMutation.mutate(reason)}
      />
      <RequestInfoDialog
        open={infoOpen}
        onOpenChange={setInfoOpen}
        isLoading={requestInfoMutation.isPending}
        documentTypeCodes={documents.map((d) => ({ code: d.code, label: isArabic ? d.nameAr : d.nameEn }))}
        onSubmit={(reason, fields, docCodes) => requestInfoMutation.mutate({ reason, fields, docCodes })}
      />
    </div>
  )
}
