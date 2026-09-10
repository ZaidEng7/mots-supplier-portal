import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../lib/queryClient'
import {Button, Card, Field, Input, PageHeading, PhoneInput, QueryError, Select, StatusChip} from '../components/ui'
import { FormMeasure } from '../components/ui/FormMeasure'
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
import { formatDate, formatDateTime } from '../lib/datetime'

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

/**
 * What the gate's progress bar is a fraction OF: the five required profile fields plus the terms.
 *
 * <p>Derived from `REQUIRED_FIELDS` rather than typed as a number, so a field added to that list moves
 * the bar with it instead of leaving a bar that quietly measures the wrong whole.</p>
 */
const GATE_REQUIREMENTS = REQUIRED_FIELDS.length + 1

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
        className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-[var(--radius-md)]"
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

/** Stable per-document anchor, so the submit error summary can link to the row it names. */
function documentAnchorId(code: string): string {
  return `document-row-${code}`
}

function DocumentGroup({
  heading,
  documents,
  emptyLabel,
  isReadOnly,
  isInfoRequested,
  flaggedDocCodes,
  blockingDocCodes,
  supplierCode,
}: {
  heading: string
  documents: DocumentTypeStatus[]
  emptyLabel?: string
  isReadOnly: boolean
  isInfoRequested: boolean
  flaggedDocCodes: Set<string>
  /** Document type codes the server named in its last submit rejection. */
  blockingDocCodes: Set<string>
  supplierCode: string
}) {
  // A required group with nothing in it would mean the document-type catalogue is empty, which is a
  // configuration fault rather than an empty state - so the heading is omitted entirely rather than
  // showing a reassuring "none". The optional group passes an emptyLabel because having no optional
  // documents is ordinary.
  if (documents.length === 0 && emptyLabel === undefined) return null

  return (
    <section>
      {/* text-fg-muted was a class this project never defines - Tailwind dropped it and these two
          lines rendered at the inherited colour. The token is what the rest of the product uses. */}
      <h3 className="mb-2 text-[length:var(--text-body)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-muted)' }}>{heading}</h3>
      {documents.length === 0 ? (
        <p className="text-[length:var(--text-body)]" style={{ color: 'var(--color-text-muted)' }}>{emptyLabel}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {documents.map((doc) => (
            <DocumentRow
              key={doc.documentTypeId}
              doc={doc}
              canEdit={!isReadOnly && (!isInfoRequested || flaggedDocCodes.has(doc.code))}
              isBlocking={blockingDocCodes.has(doc.code)}
              supplierCode={supplierCode}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

/** What the chip beside a document type should say, or nothing when there is nothing to say. */
function documentChipValue(state: string | null | undefined, isRequired: boolean, isBlocking: boolean): string | null {
  if (state) return state
  if (isRequired) return isBlocking ? 'Missing' : 'Required'
  return null
}

function DocumentRow({ doc, canEdit, isBlocking, supplierCode }: {
  doc: DocumentTypeStatus
  canEdit: boolean
  /** §12-A/C3: uploads are addressed by supplier code (§12.3). */
  supplierCode: string
  /** True once a submit attempt came back naming this document type as still missing. */
  isBlocking: boolean
}) {
  const { t, i18n } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const fileRef = useRef<HTMLInputElement>(null)
  const isArabic = i18n.language.startsWith('ar')
  // Backend rejects an expiry-tracked type's upload with no expiryDate (BRULE-020,
  // UploadDocumentHandler) - this field is what was missing on this side of that contract.
  // BRULE-020 also rejects a date that isn't strictly after today ("a document cannot be
  // filed as current while already expired") - min= on the input keeps the native picker
  // from offering an invalid choice in the first place, and minExpiryDate gates the button
  // the same way the empty-date case already was, for a date typed in manually instead of
  // picked.
  const [expiryDate, setExpiryDate] = useState('')
  const minExpiryDate = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
  const expiryInvalid = doc.expiryTracked && (!expiryDate || expiryDate <= new Date().toISOString().slice(0, 10))

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadDocument(supplierCode, doc.documentTypeId, file, undefined, doc.expiryTracked ? expiryDate : undefined),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['own-documents'] })
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

  /**
   * The one line a supplier needs about this document beyond its name.
   *
   * <p>Both halves were already on the row, as fragments between the chip and the name: whether a type
   * is optional, and how long an approved one has left. Neither is a state - the chip says the state -
   * and both are what somebody deciding what to do next actually reads.</p>
   *
   * <p>The comp adds a third line here, "PDF or image, up to 20 MB". Nothing in this client knows the
   * accepted types or the size limit; both are server-side, and printing a guess beside an upload
   * control is how a supplier learns the rule by having a file rejected. It goes to the product owner
   * as a question rather than into the copy.</p>
   */
  const expiry = doc.latestDocument?.expiryDate
  const hint = !doc.isRequired
    ? t('onboarding.optionalHint')
    : expiry && (state === 'Approved' || state === 'ExpiringSoon')
      ? `${formatDate(expiry, i18n.language)} · ${expiryCountdownLabel(expiry, i18n.language)}`
      : null

  return (
    // id: the error summary below links straight to this row, per ACCESSIBILITY §7's error-summary
    // requirement. tabIndex -1 so the link can move focus here at all - a plain <li> is not focusable.
    <li
      id={documentAnchorId(doc.code)}
      tabIndex={-1}
      className="flex items-center justify-between gap-3 rounded-[var(--radius-sm)] p-3"
      style={{ border: '1px solid var(--color-border)' }}
    >
      {/*
        The comp's row: the document's name and what a supplier needs to know about it on the left, its
        state and the control that changes that state together on the right. The chip used to lead the
        row, so a column of them was the first thing read and the names came second - and the two facts
        a supplier acts on, that a document is optional and that one expires soon, were inline fragments
        between them.
      */}
      <div className="flex min-w-0 flex-col gap-0.5">
        <span style={{ color: 'var(--color-text-primary)' }}>{label}</span>
        {hint ? (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
            {hint}
          </span>
        ) : null}
        {doc.latestDocument?.rejectReason ? (
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-danger-fg)' }}>
            {doc.latestDocument.rejectReason}
          </span>
        ) : null}
      </div>
      <div className="flex flex-none items-center gap-2">
        {/* T2-33 addendum: document states resolve through StatusChip like every other machine. The
            no-document branch is not a DocumentState, but it still has documented labels, so it routes
            through the same chip rather than a hand-rolled Badge:

            - `Required` (§7.2's first row; SCR-106 lists it first in its StatusBadge set) is the
              RESTING label for a required type with nothing uploaded.
            - `Missing` is what that becomes once the supplier has attempted to submit and this document
              is still absent. Driven by the server's own 422 list (§12.2: "422 listing exactly what is
              missing"), never by client-side guesswork about what will block.
            - An OPTIONAL type with no upload gets no chip at all. Calling it "Required" would be false,
              and SCR-106 is explicit that "optional docs never block"; the hint beside the name already
              says what it is. */}
        {documentChipValue(state, doc.isRequired, isBlocking) ? (
          <StatusChip machine="document" value={documentChipValue(state, doc.isRequired, isBlocking)!} />
        ) : null}
        {doc.latestDocument && doc.latestDocument.state !== 'PendingScan' && doc.latestDocument.state !== 'ScanRejected' ? (
          <Button variant="ghost" size="sm" isLoading={downloadMutation.isPending} onClick={() => downloadMutation.mutate(doc.latestDocument!.documentId)}>
            {t('onboarding.download')}
          </Button>
        ) : null}
        {canEdit ? (
          <>
            {doc.expiryTracked ? (
              <input
                type="date"
                min={minExpiryDate}
                aria-label={t('onboarding.documentExpiryLabel')}
                value={expiryDate}
                onChange={(e) => setExpiryDate(e.target.value)}
                className="rounded-[var(--radius-sm)] px-2 py-1 text-[length:var(--text-body-sm)]"
                style={{ border: '1px solid var(--color-border-input)', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-primary)' }}
              />
            ) : null}
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
            <Button
              variant="secondary"
              size="sm"
              isLoading={uploadMutation.isPending}
              disabled={expiryInvalid}
              title={expiryInvalid ? t('onboarding.documentExpiryRequired') : undefined}
              onClick={() => fileRef.current?.click()}
            >
              {doc.latestDocument ? t('onboarding.reupload') : t('onboarding.upload')}
            </Button>
          </>
        ) : null}
      </div>
    </li>
  )
}

export function OnboardingPage() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const [termsChecked, setTermsChecked] = useState(false)

  const profileQuery = useQuery({ queryKey: ['own-supplier'], queryFn: getOwnSupplier })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })
  // The AV scan is async (DocumentScanJob, a background job) - a document sits in PendingScan
  // until it completes, and nothing was pushing that update to the client. The DB row was always
  // correct; only a manual reload (a fresh query) ever showed it, which read as the status chip
  // being permanently stuck. Poll while anything is still scanning, stop once nothing is.
  // §12-A/C3: the supplier routes are addressed by supplier code now (§12.2, §12.3). The code
  // comes from the profile the page already loads, so no extra request is made for it - and the
  // dependent queries below are gated on having it rather than firing with an empty string.
  const supplierCode = profileQuery.data?.supplierCode ?? ''

  const documentsQuery = useQuery({
    queryKey: ['own-documents', supplierCode],
    enabled: supplierCode !== '',
    queryFn: () => listOwnDocuments(supplierCode),
    refetchInterval: (query) => (query.state.data?.some((d) => d.latestDocument?.state === 'PendingScan') ? 2000 : false),
    // React Query pauses refetchInterval while the tab isn't visible/focused by default
    // (refetchIntervalInBackground) - reasonable for most polling, wrong for a scan the supplier
    // is actively waiting on: switching tabs for a few seconds shouldn't leave the chip stuck the
    // same way the missing poll did.
    refetchIntervalInBackground: true,
  })
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
        currencyCode: p.defaultCurrency ?? '',
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
      }),
    onSuccess: (data) => {
      onProfile(data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: (err) => notifySaveError(err),
  })

  const saveProfileMutation = useMutation({
    mutationFn: (values: ProfileFormValues) =>
      updateProfile(supplierCode, {
        description: values.description || null,
        website: values.website || null,
        supplierGroup: values.supplierGroup || null,
        currencyCode: values.currencyCode || null,
        primaryContactPhone: values.primaryContactPhone || null,
      }),
    onSuccess: (data) => {
      onProfile(data)
      notify({ kind: 'success', title: t('onboarding.saved') })
    },
    onError: (err) => notifySaveError(err),
  })

  // What the server said was still missing on the last submit attempt. Drives BOTH the escalation
  // of each required document's chip from `Required` to `Missing` and the error summary below.
  // API-ARCHITECTURE §12.2: the submit endpoint returns "422 listing exactly what is missing", so
  // this is the server's answer rather than a second, client-side completeness rule that could
  // disagree with it.
  const [submitBlockers, setSubmitBlockers] = useState<string[]>([])

  /**
   * Focus follows the announcement. `role="alert"` announces to a screen reader; it does not move the
   * keyboard caret, so the supplier who pressed Submit is left standing at the button with the
   * explanation elsewhere on the page. Focusing the summary puts the link to each blocking row one Tab
   * away.
   *
   * <p>Declared here rather than beside the summary because the loading and error branches below return
   * early, and a hook after them is a hook that sometimes does not run. Keyed on the server's list, so a
   * second failed submit naming a different document re-focuses; when the 422 named only profile fields
   * no summary is rendered and the optional call is a no-op.</p>
   */
  const blockerSummaryRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (submitBlockers.length > 0) blockerSummaryRef.current?.focus()
  }, [submitBlockers])

  const submitMutation = useMutation({
    mutationFn: () => submitApplication(supplierCode),
    onSuccess: (data) => {
      setSubmitBlockers([])
      onProfile(data)
    },
    onError: (err) => {
      if (err instanceof SupplierApiError && err.missingFields) {
        setSubmitBlockers(err.missingFields)
        // The toast stays: ACCESSIBILITY §6 says not to move focus to toasts, so it cannot be the
        // only announcement of a blocking failure. The summary region below is the accessible one.
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
      invalidateQuietly(queryClient, { queryKey: ['own-annotation'] })
    },
    onError: () => notify({ kind: 'danger', title: t('onboarding.resubmitFailed') }),
  })

  if (profileQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }
  // A failed fetch is not an empty result: without this the screen below renders its
  // empty state and tells the reader there is nothing here.
  if (profileQuery.isError) return <QueryError error={profileQuery.error} onRetry={() => void profileQuery.refetch()} />


  const profile = profileQuery.data as SupplierProfile | undefined
  const missing = new Set(profile?.missingProfileFields ?? [])
  const state = profile?.onboardingState
  const isInfoRequested = state === 'InfoRequested'

  /**
   * What is still standing between this supplier and a submitted application, in the order they will
   * meet it. Derived from the same two sources the checklist used - the server's `missingProfileFields`
   * and the terms flag - so this cannot disagree with the badge on a section further down.
   */
  const outstanding = [
    ...REQUIRED_FIELDS.filter((field) => missing.has(field)).map((field) => ({
      key: field,
      label: t(`onboarding.fields.${field}`),
      flagged: isInfoRequested && flaggedFields.has(field),
    })),
    ...(missing.has('termsAccepted')
      ? [{ key: 'termsAccepted', label: t('onboarding.termsLabel'), flagged: false }]
      : []),
  ]
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

  // The 422's `missingFields` mixes missing PROFILE fields with missing DOCUMENT TYPE CODES
  // (SubmitApplicationHandler concatenates the two). Intersecting against the real document
  // catalogue is what separates them - and it means a profile-field name can never be rendered as
  // a missing document, nor a code the catalogue no longer has.
  const isArabic = i18n.language.startsWith('ar')
  const submitBlockerCodes = new Set(submitBlockers)
  const blockingDocuments = requiredDocuments.filter((doc) => submitBlockerCodes.has(doc.code))
  const blockingDocCodes = new Set(blockingDocuments.map((doc) => doc.code))
  const currencyCode = profileForm.watch('currencyCode')
  const primaryContactPhone = profileForm.watch('primaryContactPhone')
  const supplierType = legalForm.watch('supplierType')

  const fieldEditable = (field: string) => !isReadOnly && (!isInfoRequested || flaggedFields.has(field))

  if (!profile) return null

  return (
    <FormMeasure>
      <PageHeading title={t('onboarding.title')} meta={<StatusChip machine="onboarding" value={profile.onboardingState} />} />

      <OnboardingStepNav />

      {/*
        The comp's read-only banner. This used to be one green sentence at the bottom of the profile
        card, where a supplier found it after filling in fields that would not save - the notice arrived
        after the disappointment it was meant to prevent.

        What it does NOT say is the comp's third line, "you can still upload a replacement document at
        any time". Documents are gated by the same `canEdit` as every other control on this screen, so
        while an application is with a reviewer nothing can be uploaded. The comp promises a behaviour
        the product does not have, so the words change rather than the code, and the gap goes to the
        product owner as a question.
      */}
      {isReadOnly ? (
        <div
          role="status"
          className="rounded-[var(--radius-lg)] p-4"
          style={{ backgroundColor: 'var(--color-info-bg)', border: '1px solid var(--color-info-solid)' }}
        >
          <p className="font-[var(--fw-semibold)]" style={{ color: 'var(--color-info-fg)' }}>
            {t('onboarding.readOnlyTitle')}
          </p>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('onboarding.readOnlyBody')}
          </p>
        </div>
      ) : null}

      {isInfoRequested && annotation ? (
        <div className="rounded-[var(--radius-lg)] p-6" style={{ backgroundColor: 'var(--color-warning-bg)', border: '1px solid var(--color-warning-solid)' }}>
          <h2 className="mb-2 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-warning-fg)' }}>
            {t('onboarding.infoRequestedTitle')}
          </h2>
          <p style={{ color: 'var(--color-text-primary)' }}>{annotation.reason}</p>
        </div>
      ) : null}

      {/*
        ACCESSIBILITY.md §7, error summary: *"a focusable summary region (`role="alert"` or moved
        focus) listing each error as a link jumping to its field - essential for long onboarding
        forms and SR users."* The chip escalation alone is a colour and a word inside a long list;
        a screen-reader user would have to walk every row to find what is blocking them. This
        region is the announcement, and each entry jumps to the row it names.

        SCREEN-SPECIFICATIONS §2 (SCR-106) adds *"Errors are per-card, `aria-live`"* - the per-card
        half is the chip; this is the summary §7 additionally requires at submit.

        It sits ABOVE the gate rather than below the documents, and that is the whole point of it.
        It used to render after the entire document list: its links pointed backwards, and a keyboard
        user who pressed Submit had to Shift+Tab past every document control to reach the sentence
        explaining why. Now it precedes both the button that produced it and the rows it names.
      */}
      {blockingDocuments.length > 0 ? (
        <div
          ref={blockerSummaryRef}
          tabIndex={-1}
          role="alert"
          className="rounded-[var(--radius-sm)] p-3"
          style={{ border: '1px solid var(--color-danger-fg)', color: 'var(--color-danger-fg)' }}
        >
          <p className="font-[var(--fw-semibold)]">{t('onboarding.submitBlockedTitle')}</p>
          <p className="text-[length:var(--text-body-sm)]">{t('onboarding.submitBlockedIntro')}</p>
          <ul className="mt-1 flex flex-col gap-1">
            {blockingDocuments.map((doc) => (
              <li key={doc.documentTypeId}>
                <a
                  href={`#${documentAnchorId(doc.code)}`}
                  style={{ color: 'var(--color-danger-fg)', textDecoration: 'underline' }}
                >
                  {isArabic ? doc.nameAr : doc.nameEn}
                </a>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {/*
        The gate. It used to be a checklist of every requirement with a badge on each, so a supplier read
        eight rows to find the two that were not done - and the button those rows were about sat 200 lines
        below, disabled, with nothing saying why.

        This lists ONLY what is outstanding, says everything else is saved, and carries the submit it
        gates. When nothing is outstanding it says that instead, which is the one moment on this screen
        worth being unambiguous about.
      */}
      {/* Not rendered once the application is with a reviewer. A gate saying "Ready to submit" above an
          application that has already BEEN submitted is worse than no gate: it invites an action that no
          longer exists, and the read-only notice further down already says what state this is in. */}
      {!isReadOnly ? (
      <Card
        // The comp titles this card with the answer rather than with the question: "Two things left" is
        // what a supplier came to find out, and "Before you can submit" made them read the list to learn
        // it. The same string the step cards use, because it is the same sentence about the same list.
        title={outstanding.length === 0 ? t('onboarding.gateReady') : t('onboarding.stepStatus.left', { count: outstanding.length })}
        action={
          <div className="flex flex-col items-end gap-1">
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
            {!isInfoRequested && missing.size > 0 ? (
              <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t('onboarding.gateBlocked')}
              </span>
            ) : null}
          </div>
        }
      >
        {outstanding.length > 0 ? (
          <p className="mb-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('onboarding.gateHelp')}
          </p>
        ) : null}
        {/*
          The bar is the comp's, and it is the one thing on this card that says how MUCH is left rather
          than what. It carries the same two numbers to a screen reader through `role="progressbar"`,
          because a coloured strip says nothing to one.
        */}
        <div
          role="progressbar"
          aria-label={t('onboarding.gateProgressLabel')}
          aria-valuemin={0}
          aria-valuemax={GATE_REQUIREMENTS}
          aria-valuenow={GATE_REQUIREMENTS - outstanding.length}
          className="mb-4 h-1.5 w-full overflow-hidden rounded-[var(--radius-pill)]"
          style={{ backgroundColor: 'var(--color-bg-sunken)' }}
        >
          <div
            className="h-full rounded-[var(--radius-pill)]"
            style={{
              width: `${((GATE_REQUIREMENTS - outstanding.length) / GATE_REQUIREMENTS) * 100}%`,
              backgroundColor: 'var(--color-brand-solid)',
            }}
          />
        </div>
        {outstanding.length > 0 ? (
          // Chips rather than badge-and-label rows. Every row carried the word "Missing", which is what
          // the card's own title now says once, and eight repetitions of it were eight things to read
          // past to reach the two names that mattered.
          <ul aria-label={t('onboarding.gateOutstandingLabel')} className="m-0 flex list-none flex-wrap gap-2 p-0">
            {outstanding.map((item) => (
              <li
                key={item.key}
                className="rounded-[var(--radius-pill)] px-3 py-1 text-[length:var(--text-body-sm)]"
                style={{
                  border: `1px solid ${item.flagged ? 'var(--color-danger-fg)' : 'var(--color-warning-fg)'}`,
                  color: item.flagged ? 'var(--color-danger-fg)' : 'var(--color-warning-fg)',
                }}
              >
                {item.label}
                {item.flagged ? ` · ${t('onboarding.flagged')}` : ''}
              </li>
            ))}
          </ul>
        ) : null}
      </Card>
      ) : null}

      <Card title={t('onboarding.logoTitle')}>
        <LogoUploader profile={profile} canEdit={!isReadOnly} onProfile={onProfile} />
      </Card>

      <Card title={t('onboarding.legalTitle')}>
        <form className="flex flex-col gap-4" onSubmit={legalForm.handleSubmit((values) => saveLegalMutation.mutate(values))}>
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
                  disabled={!fieldEditable('legalInfo')}
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
                {t('onboarding.saveLegal')}
              </Button>
            </div>
          ) : null}
        </form>
      </Card>

      <Card title={t('onboarding.profileTitle')}>
        <form className="flex flex-col gap-4" onSubmit={profileForm.handleSubmit((values) => saveProfileMutation.mutate(values))}>
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
                  disabled={!fieldEditable('currencyCode')}
                />
              )}
            </Field>
            <Field label={t('onboarding.fields.primaryContactPhone')} required>
              {(p) => (
                <PhoneInput
                  {...p}
                  disabled={!fieldEditable('primaryContactPhone')}
                  value={primaryContactPhone ?? ''}
                  onChange={(v) => profileForm.setValue('primaryContactPhone', v, { shouldValidate: true })}
                />
              )}
            </Field>
          </div>
          {!isReadOnly ? (
            <div>
              <Button type="submit" variant="secondary" isLoading={saveProfileMutation.isPending}>
                {t('onboarding.saveProfile')}
              </Button>
            </div>
          ) : null}
        </form>
      </Card>

      <Card title={t('onboarding.termsTitle')}>
        {/* Two independent facts, not one refined twice: whether the terms have been accepted, and
            whether this supplier can still act. An accepted application shows when and which version;
            an editable one that has not accepted shows the checkbox; a read-only one shows neither. */}
        {profile.termsAcceptedAt ? (
          <p style={{ color: 'var(--color-success-fg)' }}>
            {t('onboarding.termsAcceptedNotice', {
              date: formatDateTime(profile.termsAcceptedAt, i18n.language),
              version: profile.termsAcceptedVersion,
            })}
          </p>
        ) : null}
        {!profile.termsAcceptedAt && !isReadOnly ? (
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
        ) : null}
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
            blockingDocCodes={blockingDocCodes}
            supplierCode={supplierCode}
          />
          <DocumentGroup
            heading={t('onboarding.optionalDocuments')}
            documents={optionalDocuments}
            emptyLabel={t('onboarding.noOptionalDocuments')}
            isReadOnly={isReadOnly}
            isInfoRequested={isInfoRequested}
            flaggedDocCodes={flaggedDocCodes}
            blockingDocCodes={blockingDocCodes}
            supplierCode={supplierCode}
          />
        </div>
      </Card>
    </FormMeasure>
  )
}
