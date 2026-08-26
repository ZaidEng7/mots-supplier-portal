import { useEffect, useRef } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Field, Input, Select } from '../components/ui'
import { useToast } from '../components/ui'
import {
  getOwnSupplier,
  updateProfile,
  submitApplication,
  resubmitApplication,
  SupplierApiError,
  type SupplierProfile,
} from '../api/supplier'
import { fetchCurrencies } from '../api/reference'
import { listOwnDocuments, uploadDocument, getDocumentDownloadUrl, DocumentApiError, type DocumentTypeStatus } from '../api/documents'
import { getOwnActiveAnnotation } from '../api/review'

const schema = z.object({
  registrationNumber: z.string().optional(),
  taxId: z.string().optional(),
  addressLine: z.string().optional(),
  city: z.string().optional(),
  country: z.string().optional(),
  currencyCode: z.string().optional(),
  primaryContactPhone: z.string().optional(),
})

type FormValues = z.infer<typeof schema>

const REQUIRED_FIELDS = ['registrationNumber', 'taxId', 'addressLine', 'city', 'country', 'currencyCode', 'primaryContactPhone'] as const

const DOC_STATE_TONE: Record<string, 'success' | 'warning' | 'danger' | 'info' | 'neutral'> = {
  PendingScan: 'info',
  Uploaded: 'success',
  UnderReview: 'info',
  Approved: 'success',
  Rejected: 'danger',
  ScanRejected: 'danger',
  ExpiringSoon: 'warning',
  Expired: 'danger',
}

function DocumentRow({ doc, canEdit }: { doc: DocumentTypeStatus; canEdit: boolean }) {
  const { t, i18n } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const fileRef = useRef<HTMLInputElement>(null)
  const isArabic = i18n.language.startsWith('ar')

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadDocument(doc.documentTypeId, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['own-documents'] })
      notify({ kind: 'success', title: t('onboarding.documentUploaded') })
    },
    onError: (err) => {
      const message = err instanceof DocumentApiError ? err.message : t('onboarding.documentUploadFailed')
      notify({ kind: 'danger', title: t('onboarding.documentUploadFailed'), description: message })
    },
  })

  const downloadMutation = useMutation({
    mutationFn: getDocumentDownloadUrl,
    onSuccess: (url) => window.open(url, '_blank', 'noopener,noreferrer'),
  })

  const state = doc.latestDocument?.state
  const label = isArabic ? doc.nameAr : doc.nameEn

  return (
    <li className="flex items-center justify-between gap-3 rounded-[0.375rem] p-3" style={{ border: '1px solid var(--color-border)' }}>
      <div className="flex items-center gap-2">
        <Badge tone={state ? DOC_STATE_TONE[state] : 'warning'}>{state ? t(`onboarding.docState.${state}`) : t('onboarding.missing')}</Badge>
        <span style={{ color: 'var(--color-text-primary)' }}>{label}</span>
        {doc.isRequired ? null : (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
            ({t('onboarding.optional')})
          </span>
        )}
        {doc.latestDocument?.rejectReason ? (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--danger-500)' }}>
            {doc.latestDocument.rejectReason}
          </span>
        ) : null}
      </div>
      <div className="flex items-center gap-2">
        {doc.latestDocument && doc.latestDocument.state !== 'PendingScan' && doc.latestDocument.state !== 'ScanRejected' ? (
          <Button variant="ghost" size="sm" isLoading={downloadMutation.isPending} onClick={() => downloadMutation.mutate(doc.latestDocument!.id)}>
            {t('onboarding.download')}
          </Button>
        ) : null}
        {canEdit ? (
          <>
            <input
              ref={fileRef}
              type="file"
              accept=".pdf,.png,.jpg,.jpeg"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) uploadMutation.mutate(file)
                e.target.value = ''
              }}
            />
            <Button variant="secondary" size="sm" isLoading={uploadMutation.isPending} onClick={() => fileRef.current?.click()}>
              {doc.latestDocument ? t('onboarding.reupload') : t('onboarding.upload')}
            </Button>
          </>
        ) : null}
      </div>
    </li>
  )
}

export function OnboardingPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()

  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })
  const documentsQuery = useQuery({ queryKey: ['own-documents'], queryFn: listOwnDocuments })
  const annotationQuery = useQuery({ queryKey: ['own-annotation'], queryFn: getOwnActiveAnnotation })

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })
  const currencyCode = watch('currencyCode')

  useEffect(() => {
    if (profileQuery.data) {
      reset({
        registrationNumber: profileQuery.data.registrationNumber ?? '',
        taxId: profileQuery.data.taxId ?? '',
        addressLine: profileQuery.data.addressLine ?? '',
        city: profileQuery.data.city ?? '',
        country: profileQuery.data.country ?? '',
        currencyCode: profileQuery.data.currencyCode ?? '',
        primaryContactPhone: profileQuery.data.primaryContactPhone ?? '',
      })
    }
  }, [profileQuery.data, reset])

  const saveMutation = useMutation({
    mutationFn: (values: FormValues) => updateProfile(values),
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: () => notify({ kind: 'danger', title: t('onboarding.saveFailed') }),
  })

  const submitMutation = useMutation({
    mutationFn: submitApplication,
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      notify({ kind: 'success', title: t('onboarding.submitted') })
    },
    onError: (err) => {
      if (err instanceof SupplierApiError && err.missingFields) {
        notify({ kind: 'danger', title: t('onboarding.incomplete'), description: err.missingFields.join(', ') })
      } else {
        notify({ kind: 'danger', title: t('onboarding.submitFailed') })
      }
    },
  })

  const resubmitMutation = useMutation({
    mutationFn: resubmitApplication,
    onSuccess: (data) => {
      queryClient.setQueryData(['own-supplier'], data)
      queryClient.invalidateQueries({ queryKey: ['own-annotation'] })
      notify({ kind: 'success', title: t('onboarding.resubmitted') })
    },
    onError: () => notify({ kind: 'danger', title: t('onboarding.resubmitFailed') }),
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>...</p>
  }

  const profile = profileQuery.data as SupplierProfile | undefined
  const missing = new Set(profile?.missingProfileFields ?? [])
  const state = profile?.onboardingState
  const isInfoRequested = state === 'InfoRequested'
  const isEditableState = state === 'EmailVerified' || state === 'ProfileInProgress' || isInfoRequested
  const isReadOnly = !isEditableState
  const annotation = annotationQuery.data
  const flaggedFields = new Set(annotation?.flaggedProfileFields ?? [])
  const flaggedDocCodes = new Set(annotation?.flaggedDocumentTypeCodes ?? [])
  const currencyOptions = (currenciesQuery.data ?? []).map((c) => ({ value: c.code, label: `${c.code}` }))
  const documents = documentsQuery.data ?? []

  const fieldEditable = (field: string) => !isReadOnly && (!isInfoRequested || flaggedFields.has(field))

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.title')}
        </h1>
        {profile ? <Badge tone={profile.onboardingState === 'Approved' ? 'success' : 'brand'}>{profile.onboardingState}</Badge> : null}
      </div>

      {isInfoRequested && annotation ? (
        <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--warning-50)', border: '1px solid var(--warning-500)' }}>
          <h2 className="mb-2 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--warning-600)' }}>
            {t('onboarding.infoRequestedTitle')}
          </h2>
          <p style={{ color: 'var(--color-text-primary)' }}>{annotation.reason}</p>
        </div>
      ) : null}

      <div
        className="rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.checklist')}
        </h2>
        <ul className="flex flex-col gap-1.5">
          {REQUIRED_FIELDS.map((field) => (
            <li key={field} className="flex items-center gap-2 text-[length:var(--text-body-sm)]">
              <Badge tone={missing.has(field) ? 'warning' : 'success'}>
                {missing.has(field) ? t('onboarding.missing') : t('onboarding.complete')}
              </Badge>
              <span style={{ color: 'var(--color-text-secondary)' }}>{t(`onboarding.fields.${field}`)}</span>
              {isInfoRequested && flaggedFields.has(field) ? <Badge tone="danger">{t('onboarding.flagged')}</Badge> : null}
            </li>
          ))}
        </ul>
      </div>

      <form
        className="flex flex-col gap-4 rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        onSubmit={handleSubmit((values) => saveMutation.mutate(values))}
      >
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label={t('onboarding.fields.registrationNumber')}>
            {(p) => <Input disabled={!fieldEditable('registrationNumber')} {...p} {...register('registrationNumber')} />}
          </Field>
          <Field label={t('onboarding.fields.taxId')}>
            {(p) => <Input disabled={!fieldEditable('taxId')} {...p} {...register('taxId')} />}
          </Field>
          <Field label={t('onboarding.fields.addressLine')}>
            {(p) => <Input disabled={!fieldEditable('addressLine')} {...p} {...register('addressLine')} />}
          </Field>
          <Field label={t('onboarding.fields.city')}>{(p) => <Input disabled={!fieldEditable('city')} {...p} {...register('city')} />}</Field>
          <Field label={t('onboarding.fields.country')}>
            {(p) => <Input disabled={!fieldEditable('country')} {...p} {...register('country')} />}
          </Field>
          <Field label={t('onboarding.fields.currencyCode')}>
            {(p) => (
              <Select
                id={p.id}
                aria-describedby={p['aria-describedby']}
                aria-invalid={p['aria-invalid']}
                value={currencyCode || undefined}
                onValueChange={(v) => setValue('currencyCode', v)}
                options={currencyOptions}
                placeholder={t('onboarding.fields.currencyCode')}
              />
            )}
          </Field>
          <Field label={t('onboarding.fields.primaryContactPhone')}>
            {(p) => <Input disabled={!fieldEditable('primaryContactPhone')} {...p} {...register('primaryContactPhone')} />}
          </Field>
        </div>
        {!isReadOnly ? (
          <div>
            <Button type="submit" variant="secondary" isLoading={isSubmitting || saveMutation.isPending}>
              {t('onboarding.save')}
            </Button>
          </div>
        ) : (
          <p role="status" style={{ color: 'var(--success-600)' }}>
            {t('onboarding.readOnlyNotice')}
          </p>
        )}
      </form>

      <div
        className="rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.documents')}
        </h2>
        <ul className="flex flex-col gap-2">
          {documents.map((doc) => (
            <DocumentRow
              key={doc.documentTypeId}
              doc={doc}
              canEdit={!isReadOnly && (!isInfoRequested || flaggedDocCodes.has(doc.code))}
            />
          ))}
        </ul>
      </div>

      {!isReadOnly ? (
        <div className="flex gap-3">
          {isInfoRequested ? (
            <Button isLoading={resubmitMutation.isPending} onClick={() => resubmitMutation.mutate()}>
              {t('onboarding.resubmit')}
            </Button>
          ) : (
            <Button
              isLoading={submitMutation.isPending}
              disabled={missing.size > 0}
              onClick={() => submitMutation.mutate()}
            >
              {t('onboarding.submit')}
            </Button>
          )}
        </div>
      ) : null}
    </div>
  )
}
