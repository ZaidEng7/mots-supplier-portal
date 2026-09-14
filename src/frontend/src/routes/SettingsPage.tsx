// SCR-902's account screen, and the three cards under it: the password change, MFA and sessions, and the supplier's own
// audit trail.
//
// THE ACCOUNT ITSELF is a name and an interface language. Both were fixed at registration with no screen to change
// either, so a user whose name was mistyped by whoever invited them was stuck with it, and a user who wanted the other
// language had only the header toggle, which resets on the next sign-in because nothing stored the choice.
//
// Email is displayed and not editable. Changing it means re-verifying it, and no document here defines that flow -
// SCREEN-INVENTORY.md's own row for SCR-902 lists "Name, language, numerals, contact" and not email. It is shown anyway,
// because "which address do my notifications go to" is a question this screen should answer.
//
// Numerals are derived, not chosen. The inventory row asks for a numerals preference; lib/datetime.ts records the
// decision that numerals follow the locale - Arabic renders ٠-٩, English 0-9 - so that a price and a deadline on one
// screen cannot disagree. A second control here would be a second source of truth for the same formatting, so the screen
// states the rule instead. Logged as an open question rather than decided quietly.
//
// The server's values are the initial ones and the local state only exists once the user types: seeding state from the
// query in an effect would fight the query on every refetch. The saved language takes effect IMMEDIATELY, because without
// that the user saves "English", the request succeeds, and the page they are looking at stays Arabic until they sign in
// again - which reads as the save having failed.
//
// The language control is the shared Select. It was a bare select hand-styling that control's surface, and with the wrong
// border: it drew --color-border, which is the separator, where an input's edge answers to SC 1.4.11 and the shared
// control draws --color-border-input for exactly that reason.
//
// THE PASSWORD CARD is SCR-903, and it is first on the screen for a reason: until now a signed-in user had no way to
// change their own password, the only path being to sign out and use the forgotten-password email, which is a recovery
// flow doing routine work. It sits above MFA because it is the more ordinary of the two.
//
// The server's refusals are shown against the FIELD they are about - a wrong current password is a mistake somebody can
// correct by retyping, and rendering it as a page-level failure would leave them guessing which of the three boxes was
// wrong. The confirmation match is checked here rather than on the server, which never sees that field, because a
// mismatch is a typing mistake rather than a rule about passwords. And the consequence is said before it happens rather
// than discovered afterwards: the change signs out every other device.
//
// SESSIONS page with useInfiniteQuery. MSP-84: sessions are bounded per person, but real pagination is scoped for all
// four client-facing lists, and loading page one and stopping would silently hide the rest. The count is guarded on
// hasNextPage against under-counting, because more sessions may exist beyond what is loaded even if only the current one
// has been fetched so far.
//
// THE AUDIT TRAIL is B-1 and FR-AUD-003's. GET /suppliers/me/audit and its CSV export have existed since EPIC-01 and
// nothing called either - a compliance affordance that shipped unreachable. It is here rather than on its own route
// because it is the supplier's own record of their own account, which is what this screen is; a separate page would be one
// more route for one table.
//
// It is gated on the caller actually BEING a supplier. SCR-902 made this screen reachable by every authenticated persona
// and GET /suppliers/me/audit is supplier-scoped, so a procurement officer opening it would meet a failed panel on an
// otherwise working page, which reads as a defect rather than as a section that does not apply to them.
//
// Each row shows the action's own TOKEN rather than a translated label: §7 has no table for audit actions, and inventing
// one here would put a second vocabulary beside the one the trail records.

import { useState } from 'react'
import { nextPageParam } from '../api/listEnvelope'
import { useTranslation } from 'react-i18next'
import { listOwnAuditTrail, downloadOwnAuditTrail } from '../api/audit'
import { formatDateTime } from '../lib/datetime'
import { changePassword, getAccount, updateAccount, ApiError } from '../api/auth'
import { useAuthStore } from '../lib/authStore'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../lib/queryClient'
import { Badge, Button, Card, Field, Input, LoadMore, PageHeading, QueryError, Select, SkeletonList, useToast} from '../components/ui'
import {
  enrollMfa,
  confirmMfaEnrollment,
  listSessions,
  revokeSession,
  revokeAllOtherSessions,
  type EnrollMfaResponse,
} from '../api/settings'

function AccountSection() {
  const { t, i18n } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const accountQuery = useQuery({ queryKey: ['account'], queryFn: getAccount })

  const [fullName, setFullName] = useState<string | null>(null)
  const [language, setLanguage] = useState<string | null>(null)

  const nameValue = fullName ?? accountQuery.data?.fullName ?? ''
  const languageValue = language ?? accountQuery.data?.language ?? 'ar'

  const saveMutation = useMutation({
    mutationFn: () => updateAccount(nameValue, languageValue),
    onSuccess: (account) => {
      invalidateQuietly(queryClient, { queryKey: ['account'] })
      setFullName(null)
      setLanguage(null)
      if (i18n.language !== account.language) void i18n.changeLanguage(account.language)
      notify({ kind: 'success', title: t('account.saved') })
    },
    onError: () => notify({ kind: 'danger', title: t('account.errors.saveFailed') }),
  })

  if (accountQuery.isLoading) return <SkeletonList label={t('common.loading')} />
  if (accountQuery.isError) {
    return (
      <div className="flex flex-col gap-2">
        <QueryError error={accountQuery.error} onRetry={() => void accountQuery.refetch()} />
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <Field label={t('account.fields.fullName')} required>
        {(p) => <Input {...p} value={nameValue} onChange={(e) => setFullName(e.target.value)} />}
      </Field>

      <Field label={t('account.fields.language')}>
        {(p) => (
          <Select
            id={p.id}
            aria-describedby={p['aria-describedby']}
            value={languageValue}
            onValueChange={setLanguage}
            options={[
              { value: 'ar', label: t('account.languages.ar') },
              { value: 'en', label: t('account.languages.en') },
            ]}
          />
        )}
      </Field>

      <div>
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('account.fields.email')}
        </p>
        <p style={{ color: 'var(--color-text-primary)' }}>{accountQuery.data?.email}</p>
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('account.emailFixed')}
        </p>
      </div>

      <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('account.numeralsFollowLanguage')}
      </p>

      <div>
        <Button
          isLoading={saveMutation.isPending}
          disabled={!nameValue.trim() || (fullName === null && language === null)}
          onClick={() => saveMutation.mutate()}
        >
          {t('account.save')}
        </Button>
      </div>
    </div>
  )
}

function ChangePasswordSection() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [currentError, setCurrentError] = useState<string | null>(null)
  const [nextError, setNextError] = useState<string | null>(null)

  const mismatch = confirm.length > 0 && next !== confirm

  const mutation = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: () => {
      notify({ kind: 'success', title: t('settings.passwordChanged') })
      setCurrent(''); setNext(''); setConfirm(''); setCurrentError(null); setNextError(null)
    },
    onError: (raised) => {
      setCurrentError(null); setNextError(null)
      const code = raised instanceof ApiError
        ? ((raised.body as { code?: string } | null)?.code ?? '')
        : ''
      if (code === 'INCORRECT_CURRENT_PASSWORD') setCurrentError(t('settings.passwordIncorrect'))
      else if (code === 'PASSWORD_UNCHANGED') setNextError(t('settings.passwordUnchanged'))
      else if (code === 'WEAK_PASSWORD') setNextError(t('settings.passwordWeak'))
      else notify({ kind: 'danger', title: t('settings.passwordChangeFailed') })
    },
  })

  return (
    <Card title={t('settings.passwordTitle')}>
      <p className="mb-4 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('settings.passwordHint')}
      </p>
      <form
        className="flex max-w-sm flex-col gap-3"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
        noValidate
      >
        <Field label={t('settings.currentPassword')} error={currentError ?? undefined}>
          {(p) => <Input {...p} type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />}
        </Field>
        <Field label={t('settings.newPasswordLabel')} error={nextError ?? undefined} hint={t('settings.passwordRule')}>
          {(p) => <Input {...p} type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />}
        </Field>
        <Field label={t('settings.confirmPassword')} error={mismatch ? t('settings.passwordMismatch') : undefined}>
          {(p) => <Input {...p} type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} />}
        </Field>
        <Button
          type="submit"
          className="self-start"
          isLoading={mutation.isPending}
          disabled={!current || !next || mismatch || next.length < 12}
        >
          {t('settings.changePassword')}
        </Button>
      </form>
      <p className="mt-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('settings.passwordRevokesOthers')}
      </p>
    </Card>
  )
}

function MfaSection() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const [enrollment, setEnrollment] = useState<EnrollMfaResponse | null>(null)
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  const enrollMutation = useMutation({
    mutationFn: enrollMfa,
    onSuccess: setEnrollment,
    onError: () => notify({ kind: 'danger', title: t('settings.mfaEnrollFailed') }),
  })

  const confirmMutation = useMutation({
    mutationFn: (c: string) => confirmMfaEnrollment(c),
    onSuccess: (result) => {
      setRecoveryCodes(result.recoveryCodes)
      setEnrollment(null)
      notify({ kind: 'success', title: t('settings.mfaEnabled') })
    },
    onError: () => notify({ kind: 'danger', title: t('settings.mfaInvalidCode') }),
  })

  if (recoveryCodes) {
    return (
      <div className="flex flex-col gap-3">
        <p style={{ color: 'var(--color-success-fg)' }}>{t('settings.mfaEnabled')}</p>
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('settings.recoveryCodesNotice')}
        </p>
        <ul className="num grid grid-cols-2 gap-2 rounded-[var(--radius-sm)] p-3" style={{ backgroundColor: 'var(--color-bg-sunken)' }}>
          {recoveryCodes.map((c) => (
            <li key={c} className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
              {c}
            </li>
          ))}
        </ul>
      </div>
    )
  }

  if (enrollment) {
    return (
      <div className="flex flex-col gap-3">
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('settings.mfaScanOrEnter')}</p>
        <p className="num rounded-[var(--radius-sm)] p-2 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--color-bg-sunken)', color: 'var(--color-text-primary)' }}>
          {enrollment.sharedKey}
        </p>
        <Field label={t('settings.mfaCodeLabel')}>
          {(p) => <Input {...p} value={code} onChange={(e) => setCode(e.target.value)} maxLength={6} inputMode="numeric" />}
        </Field>
        <Button isLoading={confirmMutation.isPending} onClick={() => confirmMutation.mutate(code)} disabled={code.length !== 6}>
          {t('settings.mfaConfirm')}
        </Button>
      </div>
    )
  }

  return (
    <Button variant="secondary" isLoading={enrollMutation.isPending} onClick={() => enrollMutation.mutate()}>
      {t('settings.mfaEnroll')}
    </Button>
  )
}

function SessionsSection() {
  const { t, i18n } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const sessionsQuery = useInfiniteQuery({
    queryKey: ['sessions'],
    queryFn: ({ pageParam }) => listSessions(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })

  const revokeMutation = useMutation({
    mutationFn: revokeSession,
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['sessions'] })
      notify({ kind: 'success', title: t('settings.sessionRevoked') })
    },
  })

  const revokeAllMutation = useMutation({
    mutationFn: revokeAllOtherSessions,
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['sessions'] })
      notify({ kind: 'success', title: t('settings.sessionsRevokedAll') })
    },
  })

  if (sessionsQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>...</p>
  }

  const sessions = sessionsQuery.data?.pages.flatMap((p) => p.data) ?? []

  return (
    <div className="flex flex-col gap-3">
      <ul className="flex flex-col gap-2">
        {sessions.map((s) => (
          <li
            key={s.familyId}
            className="flex items-center justify-between rounded-[var(--radius-sm)] p-3"
            style={{ border: '1px solid var(--color-border)' }}
          >
            <div>
              <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
                {s.userAgent ?? t('settings.unknownDevice')} {s.isCurrent ? <Badge tone="brand">{t('settings.currentSession')}</Badge> : null}
              </p>
              <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
                {s.ip ?? '—'} · {formatDateTime(s.createdAt, i18n.language)}
              </p>
            </div>
            {!s.isCurrent ? (
              <Button variant="ghost" size="sm" onClick={() => revokeMutation.mutate(s.familyId)}>
                {t('settings.revoke')}
              </Button>
            ) : null}
          </li>
        ))}
      </ul>
      <LoadMore
        hasNextPage={sessionsQuery.hasNextPage}
        isFetching={sessionsQuery.isFetchingNextPage}
        onClick={() => sessionsQuery.fetchNextPage()}
        label={t('settings.loadMoreSessions')}
      />
      <Button
        variant="secondary"
        isLoading={revokeAllMutation.isPending}
        onClick={() => revokeAllMutation.mutate()}
        disabled={sessions.length <= 1 && !sessionsQuery.hasNextPage}
      >
        {t('settings.revokeAllOthers')}
      </Button>
    </div>
  )
}

export function SettingsPage() {
  const { t } = useTranslation()
  const isSupplier = useAuthStore((state) => state.claims?.supplierId) !== undefined

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('settings.title')} />

      <Card title={t('account.title')}>
        <AccountSection />
      </Card>

      <ChangePasswordSection />

      <Card title={t('settings.mfaTitle')}>
        <MfaSection />
      </Card>

      <Card title={t('settings.sessionsTitle')}>
        <SessionsSection />
      </Card>

      {isSupplier ? (
        <Card title={t('settings.auditTitle')}>
          <AuditTrailSection />
        </Card>
      ) : null}
    </div>
  )
}

function AuditTrailSection() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const { notify } = useToast()
  const trailQuery = useQuery({ queryKey: ['own-audit'], queryFn: () => listOwnAuditTrail() })

  const exportMutation = useMutation({
    mutationFn: downloadOwnAuditTrail,
    onError: () => notify({ kind: 'danger', title: t('settings.auditExportFailed') }),
  })

  if (trailQuery.isLoading) return <SkeletonList label={t('common.loading')} />
  if (trailQuery.isError) {
    return (
      <div className="flex flex-col gap-2">
        <p>{t('settings.auditLoadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void trailQuery.refetch()}>{t('settings.retry')}</Button>
      </div>
    )
  }

  const entries = trailQuery.data?.data ?? []

  return (
    <div className="flex flex-col gap-3">
      <p style={{ color: 'var(--color-text-secondary)' }}>{t('settings.auditHint')}</p>

      {entries.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('settings.auditEmpty')}</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {entries.map((entry) => (
            <li key={entry.id} className="flex flex-wrap items-baseline justify-between gap-2">
              <span><code>{entry.action}</code></span>
              <span className="num text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {formatDateTime(entry.occurredAt, locale)}
              </span>
            </li>
          ))}
        </ul>
      )}

      <Button
        size="sm"
        variant="secondary"
        isLoading={exportMutation.isPending}
        onClick={() => exportMutation.mutate()}
      >
        {t('settings.auditExport')}
      </Button>
    </div>
  )
}
