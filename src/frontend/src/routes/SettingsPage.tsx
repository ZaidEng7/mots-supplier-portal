import { useState } from 'react'
import { nextPageParam } from '../api/listEnvelope'
import { useTranslation } from 'react-i18next'
import { listOwnAuditTrail, downloadOwnAuditTrail } from '../api/audit'
import { formatDateTime } from '../lib/datetime'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from '../lib/queryClient'
import { Badge, Button, Field, Input, SkeletonList, useToast } from '../components/ui'
import {
  enrollMfa,
  confirmMfaEnrollment,
  listSessions,
  revokeSession,
  revokeAllOtherSessions,
  type EnrollMfaResponse,
} from '../api/settings'

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
        <p style={{ color: 'var(--success-600)' }}>{t('settings.mfaEnabled')}</p>
        <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('settings.recoveryCodesNotice')}
        </p>
        <ul className="num grid grid-cols-2 gap-2 rounded-[0.375rem] p-3" style={{ backgroundColor: 'var(--color-bg-sunken)' }}>
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
        <p className="num rounded-[0.375rem] p-2 text-[length:var(--text-body-sm)]" style={{ backgroundColor: 'var(--color-bg-sunken)', color: 'var(--color-text-primary)' }}>
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
  // MSP-84: sessions are bounded per person, but real pagination is scoped for all four
  // client-facing lists - loading page one and stopping would silently hide the rest.
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
            className="flex items-center justify-between rounded-[0.375rem] p-3"
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
      {sessionsQuery.hasNextPage ? (
        <Button variant="secondary" isLoading={sessionsQuery.isFetchingNextPage} onClick={() => sessionsQuery.fetchNextPage()}>
          {t('settings.loadMoreSessions')}
        </Button>
      ) : null}
      <Button
        variant="secondary"
        isLoading={revokeAllMutation.isPending}
        onClick={() => revokeAllMutation.mutate()}
        // hasNextPage guards against under-counting: more sessions may exist beyond what's
        // loaded, even if only one (the current one) has been fetched so far.
        disabled={sessions.length <= 1 && !sessionsQuery.hasNextPage}
      >
        {t('settings.revokeAllOthers')}
      </Button>
    </div>
  )
}

export function SettingsPage() {
  const { t } = useTranslation()

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
        {t('settings.title')}
      </h1>

      <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('settings.mfaTitle')}
        </h2>
        <MfaSection />
      </div>

      <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('settings.sessionsTitle')}
        </h2>
        <SessionsSection />
      </div>

      {/*
        B-1/FR-AUD-003. `GET /suppliers/me/audit` and its CSV export have existed since EPIC-01 and
        nothing called either - a compliance affordance that shipped unreachable. Here rather than on its
        own route because it is the supplier's own record of their own account, which is what this screen
        is; a separate page would be one more route for one table.
      */}
      <div className="rounded-[0.75rem] p-6" style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
        <h2 className="mb-3 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('settings.auditTitle')}
        </h2>
        <AuditTrailSection />
      </div>
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
              {/* The action's own token, not a translated label: §7 has no table for audit actions, and
                  inventing one here would put a second vocabulary beside the one the trail records. */}
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
