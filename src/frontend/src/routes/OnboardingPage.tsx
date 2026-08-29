import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Field, Input, Select } from '../components/ui'
import { useToast } from '../components/ui'
import { OnboardingStepNav } from '../components/OnboardingStepNav'
import {
  getOwnSupplier,
  updateProfile,
  updateLegalInfo,
  uploadLogo,
  getLogoDownloadUrl,
  acceptTerms,
  submitApplication,
  resubmitApplication,
  SupplierApiError,
  type SupplierProfile,
} from '../api/supplier'
import { fetchCurrencies } from '../api/reference'
import { listOwnDocuments, uploadDocument, getDocumentDownloadUrl, DocumentApiError, type DocumentTypeStatus } from '../api/documents'
import { getOwnActiveAnnotation } from '../api/review'

const SUPPLIER_TYPES = ['Company', 'Individual', 'Partnership'] as const

const legalSchema = z.object({
  legalNameAr: z.string().min(1),
  legalNameEn: z.string().min(1),
  registrationNumber: z.string().optional(),
  taxId: z.string().optional(),
  supplierType: z.enum(SUPPLIER_TYPES),
  establishedOn: z.string().optional(),
})
type LegalFormValues = z.infer<typeof legalSchema>

const profileSchema = z.object({
  description: z.string().optional(),
  website: z.string().optional(),
  supplierGroup: z.string().optional(),
  currencyCode: z.string().optional(),
  primaryContactPhone: z.string().optional(),
})
type ProfileFormValues = z.infer<typeof profileSchema>

// Matches SupplierDto.missingProfileFields' exact string values (Domain/Suppliers/Supplier.cs
// GetMissingProfileFields), not arbitrary display keys - keep in sync if the backend list changes.
const REQUIRED_FIELDS = ['legalInfo', 'currencyCode', 'address', 'categoryLink', 'primaryContactPhone'] as const

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

// FEAT-05.8: "N days" countdown for documents approaching/past expiry, next to the state chip.
// Western digits (numberingSystem latn) to match the rest of the app's tabular-numeral convention
// (ASSUMPTIONS.md FEAT-27.3), not the locale's native digits.
function expiryCountdownLabel(expiryDate: string, locale: string): string {
  const days = Math.round((new Date(expiryDate + 'T00:00:00Z').getTime() - Date.now()) / 86_400_000)
  const rtf = new Intl.RelativeTimeFormat(`${locale}-u-nu-latn`, { numeric: 'auto' })
  return rtf.format(days, 'day')
}

function LogoUploader({ profile, canEdit, onProfile }: { profile: SupplierProfile; canEdit: boolean; onProfile: (p: SupplierProfile) => void }) {
  const { t } = useTranslation()
  const { notify } = useToast()
  const fileRef = useRef<HTMLInputElement>(null)
  const logoUrlQuery = useQuery({ queryKey: ['logo-url', profile.logoStorageKey], queryFn: getLogoDownloadUrl, enabled: !!profile.logoStorageKey })

  const uploadMutation = useMutation({
    mutationFn: uploadLogo,
    onSuccess: (data) => { onProfile(data); notify({ kind: 'success', title: t('onboarding.logoUploaded') }) },
    onError: (err) => notify({ kind: 'danger', title: t('onboarding.logoUploadFailed'), description: err instanceof SupplierApiError ? err.message : undefined }),
  })

  return (
    <div className="flex items-center gap-4">
      <div
        className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-[0.5rem]"
        style={{ backgroundColor: 'var(--color-bg-sunken)', border: '1px solid var(--color-border)' }}
      >
        {logoUrlQuery.data ? (
          <img src={logoUrlQuery.data} alt={t('onboarding.logoAlt')} className="h-full w-full object-cover" />
        ) : (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
            {t('onboarding.noLogo')}
          </span>
        )}
      </div>
      {canEdit ? (
        <>
          <input
            ref={fileRef}
            type="file"
            accept=".png,.jpg,.jpeg"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) uploadMutation.mutate(file)
              e.target.value = ''
            }}
          />
          <Button variant="secondary" size="sm" isLoading={uploadMutation.isPending} onClick={() => fileRef.current?.click()}>
            {profile.logoStorageKey ? t('onboarding.logoReplace') : t('onboarding.logoUpload')}
          </Button>
        </>
      ) : null}
    </div>
  )
}

function DocumentGroup({
  heading,
  documents,
  emptyLabel,
  isReadOnly,
  isInfoRequested,
  flaggedDocCodes,
}: {
  heading: string
  documents: DocumentTypeStatus[]
  emptyLabel?: string
  isReadOnly: boolean
  isInfoRequested: boolean
  flaggedDocCodes: Set<string>
}) {
  // A required group with nothing in it would mean the document-type catalogue is empty, which is a
  // configuration fault rather than an empty state - so the heading is omitted entirely rather than
  // showing a reassuring "none". The optional group passes an emptyLabel because having no optional
  // documents is ordinary.
  if (documents.length === 0 && emptyLabel === undefined) return null

  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold text-fg-muted">{heading}</h3>
      {documents.length === 0 ? (
        <p className="text-sm text-fg-muted">{emptyLabel}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {documents.map((doc) => (
            <DocumentRow
              key={doc.documentTypeId}
              doc={doc}
              canEdit={!isReadOnly && (!isInfoRequested || flaggedDocCodes.has(doc.code))}
            />
          ))}
        </ul>
      )}
    </section>
  )
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
        {doc.latestDocument?.expiryDate && (state === 'Approved' || state === 'ExpiringSoon') ? (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
            {expiryCountdownLabel(doc.latestDocument.expiryDate, i18n.language)}
          </span>
        ) : null}
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
  const [termsChecked, setTermsChecked] = useState(false)

  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })
  const documentsQuery = useQuery({ queryKey: ['own-documents'], queryFn: listOwnDocuments })
  const annotationQuery = useQuery({ queryKey: ['own-annotation'], queryFn: getOwnActiveAnnotation })

  const onProfile = (data: SupplierProfile) => queryClient.setQueryData(['own-supplier'], data)

  const legalForm = useForm<LegalFormValues>({ resolver: zodResolver(legalSchema) })
  const profileForm = useForm<ProfileFormValues>({ resolver: zodResolver(profileSchema) })

  useEffect(() => {
    if (profileQuery.data) {
      const p = profileQuery.data
      legalForm.reset({
        legalNameAr: p.legalInfo?.legalNameAr ?? '',
        legalNameEn: p.legalInfo?.legalNameEn ?? '',
        registrationNumber: p.legalInfo?.registrationNumber ?? '',
        taxId: p.legalInfo?.taxId ?? '',
        supplierType: (p.legalInfo?.supplierType as (typeof SUPPLIER_TYPES)[number]) ?? 'Company',
        establishedOn: p.legalInfo?.establishedOn ?? '',
      })
      profileForm.reset({
        description: p.description ?? '',
        website: p.website ?? '',
        supplierGroup: p.supplierGroup ?? '',
        currencyCode: p.currencyCode ?? '',
        primaryContactPhone: p.primaryContactPhone ?? '',
      })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profileQuery.data])

  // MSP-65 / NFR-USE-004: a concurrency conflict is a distinct, actionable situation - the user's
  // work was NOT saved because someone else edited first - so it gets its own localized message
  // telling them to reload, not the generic "could not save".
  const notifySaveError = (err: unknown) => {
    if (err instanceof SupplierApiError && err.isFieldNotFlagged) {
      notify({
        kind: 'danger',
        title: t('onboarding.notFlaggedTitle'),
        description: t('onboarding.notFlaggedBody'),
      })
      return
    }
    if (err instanceof SupplierApiError && err.isConcurrencyConflict) {
      notify({
        kind: 'danger',
        title: t('onboarding.conflictTitle'),
        description: t('onboarding.conflictBody'),
      })
      return
    }
    notify({
      kind: 'danger',
      title: t('onboarding.saveFailed'),
      description: err instanceof SupplierApiError ? err.message : undefined,
    })
  }

  const saveLegalMutation = useMutation({
    mutationFn: (values: LegalFormValues) =>
      updateLegalInfo({
        legalNameAr: values.legalNameAr,
        legalNameEn: values.legalNameEn,
        registrationNumber: values.registrationNumber || null,
        taxId: values.taxId || null,
        supplierType: values.supplierType,
        establishedOn: values.establishedOn || null,
      }, profileQuery.data?.rowVersion),
    onSuccess: (data) => {
      onProfile(data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: (err) => notifySaveError(err),
  })

  const saveProfileMutation = useMutation({
    mutationFn: (values: ProfileFormValues) =>
      updateProfile({
        description: values.description || null,
        website: values.website || null,
        supplierGroup: values.supplierGroup || null,
        currencyCode: values.currencyCode || null,
        primaryContactPhone: values.primaryContactPhone || null,
      }, profileQuery.data?.rowVersion),
    onSuccess: (data) => {
      onProfile(data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: (err) => notifySaveError(err),
  })

  const submitMutation = useMutation({
    mutationFn: submitApplication,
    onSuccess: onProfile,
    onError: (err) => {
      if (err instanceof SupplierApiError && err.missingFields) {
        notify({ kind: 'danger', title: t('onboarding.incomplete'), description: err.missingFields.join(', ') })
      } else {
        notify({ kind: 'danger', title: t('onboarding.submitFailed') })
      }
    },
  })

  const acceptTermsMutation = useMutation({
    mutationFn: acceptTerms,
    onSuccess: onProfile,
    onError: () => notify({ kind: 'danger', title: t('onboarding.termsAcceptFailed') }),
  })

  const resubmitMutation = useMutation({
    mutationFn: resubmitApplication,
    onSuccess: (data) => {
      onProfile(data)
      queryClient.invalidateQueries({ queryKey: ['own-annotation'] })
    },
    onError: () => notify({ kind: 'danger', title: t('onboarding.resubmitFailed') }),
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
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
  const currencyOptions = (currenciesQuery.data ?? []).map((c) => ({ value: c.code, label: c.code }))
  const documents = documentsQuery.data ?? []

  // Partitioned rather than filtered twice, so a type that is somehow neither cannot vanish from
  // the page: every document the API returned appears in exactly one group.
  const requiredDocuments = documents.filter((doc) => doc.isRequired)
  const optionalDocuments = documents.filter((doc) => !doc.isRequired)
  const currencyCode = profileForm.watch('currencyCode')
  const supplierType = legalForm.watch('supplierType')

  const fieldEditable = (field: string) => !isReadOnly && (!isInfoRequested || flaggedFields.has(field))

  if (!profile) return null

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.title')}
        </h1>
        <Badge tone={profile.onboardingState === 'Approved' ? 'success' : 'brand'}>{profile.onboardingState}</Badge>
      </div>

      <OnboardingStepNav />

      {isInfoRequested && annotation ? (
        <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--warning-50)', border: '1px solid var(--warning-500)' }}>
          <h2 className="mb-2 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--warning-600)' }}>
            {t('onboarding.infoRequestedTitle')}
          </h2>
          <p style={{ color: 'var(--color-text-primary)' }}>{annotation.reason}</p>
        </div>
      ) : null}

      <Card title={t('onboarding.checklist')}>
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
          <li className="flex items-center gap-2 text-[length:var(--text-body-sm)]">
            <Badge tone={missing.has('termsAccepted') ? 'warning' : 'success'}>
              {missing.has('termsAccepted') ? t('onboarding.missing') : t('onboarding.complete')}
            </Badge>
            <span style={{ color: 'var(--color-text-secondary)' }}>{t('onboarding.termsLabel')}</span>
          </li>
        </ul>
      </Card>

      <Card title={t('onboarding.logoTitle')}>
        <LogoUploader profile={profile} canEdit={!isReadOnly} onProfile={onProfile} />
      </Card>

      <form
        className="flex flex-col gap-4 rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        onSubmit={legalForm.handleSubmit((values) => saveLegalMutation.mutate(values))}
      >
        <h2 className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.legalTitle')}
        </h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label={t('onboarding.fields.legalNameAr')} error={legalForm.formState.errors.legalNameAr ? t('onboarding.errors.legalNameArRequired') : undefined} required>
            {(p) => <Input dir="rtl" disabled={!fieldEditable('legalInfo')} {...p} {...legalForm.register('legalNameAr')} />}
          </Field>
          <Field label={t('onboarding.fields.legalNameEn')} error={legalForm.formState.errors.legalNameEn ? t('onboarding.errors.legalNameEnRequired') : undefined} required>
            {(p) => <Input dir="ltr" disabled={!fieldEditable('legalInfo')} {...p} {...legalForm.register('legalNameEn')} />}
          </Field>
          <Field label={t('onboarding.fields.registrationNumber')}>
            {(p) => <Input disabled={!fieldEditable('legalInfo')} {...p} {...legalForm.register('registrationNumber')} />}
          </Field>
          <Field label={t('onboarding.fields.taxId')}>
            {(p) => <Input disabled={!fieldEditable('legalInfo')} {...p} {...legalForm.register('taxId')} />}
          </Field>
          <Field label={t('onboarding.fields.supplierType')} required>
            {(p) => (
              <Select
                id={p.id}
                value={supplierType}
                onValueChange={(v) => legalForm.setValue('supplierType', v as (typeof SUPPLIER_TYPES)[number])}
                options={SUPPLIER_TYPES.map((v) => ({ value: v, label: t(`onboarding.supplierTypes.${v}`) }))}
              />
            )}
          </Field>
          <Field label={t('onboarding.fields.establishedOn')}>
            {(p) => <Input type="date" disabled={!fieldEditable('legalInfo')} {...p} {...legalForm.register('establishedOn')} />}
          </Field>
        </div>
        {!isReadOnly ? (
          <div>
            <Button type="submit" variant="secondary" isLoading={saveLegalMutation.isPending}>
              {t('onboarding.save')}
            </Button>
          </div>
        ) : null}
      </form>

      <form
        className="flex flex-col gap-4 rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        onSubmit={profileForm.handleSubmit((values) => saveProfileMutation.mutate(values))}
      >
        <h2 className="text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('onboarding.profileTitle')}
        </h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label={t('onboarding.fields.description')}>
            {(p) => <Input disabled={!fieldEditable('description')} {...p} {...profileForm.register('description')} />}
          </Field>
          <Field label={t('onboarding.fields.website')}>
            {(p) => <Input disabled={!fieldEditable('website')} {...p} {...profileForm.register('website')} />}
          </Field>
          <Field label={t('onboarding.fields.supplierGroup')}>
            {(p) => <Input disabled={!fieldEditable('supplierGroup')} {...p} {...profileForm.register('supplierGroup')} />}
          </Field>
          <Field label={t('onboarding.fields.currencyCode')} required>
            {(p) => (
              <Select
                id={p.id}
                aria-describedby={p['aria-describedby']}
                aria-invalid={p['aria-invalid']}
                value={currencyCode || undefined}
                onValueChange={(v) => profileForm.setValue('currencyCode', v)}
                options={currencyOptions}
                placeholder={t('onboarding.fields.currencyCode')}
              />
            )}
          </Field>
          <Field label={t('onboarding.fields.primaryContactPhone')} required>
            {(p) => <Input disabled={!fieldEditable('primaryContactPhone')} {...p} {...profileForm.register('primaryContactPhone')} />}
          </Field>
        </div>
        {!isReadOnly ? (
          <div>
            <Button type="submit" variant="secondary" isLoading={saveProfileMutation.isPending}>
              {t('onboarding.save')}
            </Button>
          </div>
        ) : (
          <p role="status" style={{ color: 'var(--success-600)' }}>
            {t('onboarding.readOnlyNotice')}
          </p>
        )}
      </form>

      <Card title={t('onboarding.termsTitle')}>
        {profile.termsAcceptedAt ? (
          <p style={{ color: 'var(--success-600)' }}>
            {t('onboarding.termsAcceptedNotice', {
              date: new Date(profile.termsAcceptedAt).toLocaleString(),
              version: profile.termsAcceptedVersion,
            })}
          </p>
        ) : isReadOnly ? null : (
          <div className="flex flex-col gap-3">
            <label className="flex items-start gap-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
              <input type="checkbox" checked={termsChecked} onChange={(e) => setTermsChecked(e.target.checked)} className="mt-1" />
              {t('onboarding.termsCheckboxLabel')}
            </label>
            <div>
              <Button
                variant="secondary"
                size="sm"
                disabled={!termsChecked}
                isLoading={acceptTermsMutation.isPending}
                onClick={() => acceptTermsMutation.mutate()}
              >
                {t('onboarding.termsAccept')}
              </Button>
            </div>
          </div>
        )}
      </Card>

      {/*
        FR-DOC-009: required and optional documents are separate sections rather than one list.
        isRequired was already on the DTO and simply never read - the supplier could not tell which
        of these blocked their submission without opening each one, which is the whole point of the
        grouping. Required comes first in both directions; that is reading order, and it is correct
        in RTL for the same reason it is in LTR.
      */}
      <Card title={t('onboarding.documents')}>
        <div className="flex flex-col gap-5">
          <DocumentGroup
            heading={t('onboarding.requiredDocuments')}
            documents={requiredDocuments}
            isReadOnly={isReadOnly}
            isInfoRequested={isInfoRequested}
            flaggedDocCodes={flaggedDocCodes}
          />
          <DocumentGroup
            heading={t('onboarding.optionalDocuments')}
            documents={optionalDocuments}
            emptyLabel={t('onboarding.noOptionalDocuments')}
            isReadOnly={isReadOnly}
            isInfoRequested={isInfoRequested}
            flaggedDocCodes={flaggedDocCodes}
          />
        </div>
      </Card>

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
