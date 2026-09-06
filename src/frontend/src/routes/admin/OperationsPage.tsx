import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Select, SkeletonTable,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import {
  getJobsMonitor, triggerRecurringJob, getOutboxMonitor, replayOutboxMessage, getErpSyncMonitor,
  getSecurityPosture, getStorageSettings,
} from '../../api/admin'
import { retryAwardErpSync } from '../../api/awards'

/**
 * SCR-721 + SCR-722, `/back-office/operations`, `system_admin`, P1.
 *
 * <p>T-083 left both as counters on the admin dashboard — "six jobs registered", "n pending" — which
 * tells an operator that something is wrong and nothing about what. Two inventory rows, one screen,
 * for the same reason the reference-data page covers five: they are the same job. An operator asking
 * "is the system moving?" reads schedules and the queue together, and two routes would mean checking
 * one, forming half a picture, and navigating.</p>
 *
 * <p>SCR-723 is the third card. Its retry calls the award endpoint that already exists rather than a new
 * admin one: §6.1's guard that only a Failed sync retries lives there, and a second path would be a
 * second copy of that guard to keep in step.</p>
 *
 * <p><b>No pause control, and the screen says why.</b> Hangfire has no paused state for a recurring
 * job: the only way to stop one is to delete the registration, which would make "an operator paused
 * this" indistinguishable from "a deployment dropped it" — the exact fault the missing-job row exists
 * to surface. The global switch stays the supported way to stop schedules.</p>
 */
export function OperationsPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [status, setStatus] = useState('')
  const [expanded, setExpanded] = useState<string | null>(null)

  const jobsQuery = useQuery({ queryKey: ['admin-jobs'], queryFn: getJobsMonitor })
  const erpQuery = useQuery({ queryKey: ['admin-erp-sync'], queryFn: () => getErpSyncMonitor() })
  const securityQuery = useQuery({ queryKey: ['admin-security'], queryFn: getSecurityPosture })
  const storageQuery = useQuery({ queryKey: ['admin-storage'], queryFn: getStorageSettings })
  const outboxQuery = useQuery({ queryKey: ['admin-outbox', status], queryFn: () => getOutboxMonitor(status || undefined) })

  const triggerMutation = useMutation({
    mutationFn: (jobId: string) => triggerRecurringJob(jobId),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('operations.jobTriggered') })
      // Both, not just the jobs list: several of these jobs are what drains the outbox, so a run the
      // operator just asked for changes the other half of this screen.
      await queryClient.invalidateQueries({ queryKey: ['admin-jobs'] })
      await queryClient.invalidateQueries({ queryKey: ['admin-outbox'] })
    },
    onError: (error: Error) => notify({
      kind: 'danger',
      title: error.message === 'job_not_registered' ? t('operations.errors.notRegistered') : t('operations.errors.triggerFailed'),
    }),
  })

  const retryErpMutation = useMutation({
    mutationFn: (rfqReferenceCode: string) => retryAwardErpSync(rfqReferenceCode),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('operations.erpRetryQueued') })
      await queryClient.invalidateQueries({ queryKey: ['admin-erp-sync'] })
    },
    onError: () => notify({ kind: 'danger', title: t('operations.errors.erpRetryFailed') }),
  })

  const replayMutation = useMutation({
    mutationFn: (id: string) => replayOutboxMessage(id),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('operations.replayQueued') })
      await queryClient.invalidateQueries({ queryKey: ['admin-outbox'] })
    },
    onError: () => notify({ kind: 'danger', title: t('operations.errors.replayFailed') }),
  })

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('operations.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('operations.subtitle')}</p>
      </div>

      <Card title={t('operations.jobsTitle')}>
        {/*
          Said once, above the table, rather than per row. When recurring jobs are switched off globally
          every NextExecution on the screen is a lie, and repeating that warning six times would train
          the reader to skip it.
        */}
        {jobsQuery.data && !jobsQuery.data.recurringEnabled ? (
          <p role="status" className="mb-3 rounded-[0.5rem] p-3"
            style={{ backgroundColor: 'var(--color-danger-bg)', color: 'var(--color-danger-fg)' }}>
            {t('operations.recurringDisabled')}
          </p>
        ) : null}

        {jobsQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {jobsQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('operations.errors.jobsLoadFailed')}</p>
            <Button variant="ghost" onClick={() => void jobsQuery.refetch()}>{t('operations.retry')}</Button>
          </div>
        ) : null}

        {jobsQuery.data ? (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('operations.fields.job')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.schedule')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.lastRun')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.nextRun')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.actions')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {jobsQuery.data.jobs.map((job) => (
                <TableRow key={job.id}>
                  <TableCell>
                    <span className="font-mono text-[length:var(--text-body-sm)]">{job.id}</span>
                    {/* The row that matters most, and it is the one with the least data on it. */}
                    {!job.registered ? (
                      <span className="ms-2"><Badge tone="danger">{t('operations.notRegistered')}</Badge></span>
                    ) : null}
                  </TableCell>
                  <TableCell><span className="font-mono text-[length:var(--text-body-sm)]">{job.cron ?? '—'}</span></TableCell>
                  <TableCell>
                    {job.lastExecution ? formatDateTime(job.lastExecution, locale) : t('operations.never')}
                    {job.lastState ? (
                      <span className="ms-2">
                        <Badge tone={job.lastState === 'Succeeded' ? 'success' : job.lastState === 'Failed' ? 'danger' : 'neutral'}>
                          {job.lastState}
                        </Badge>
                      </span>
                    ) : null}
                  </TableCell>
                  <TableCell>{job.nextExecution ? formatDateTime(job.nextExecution, locale) : '—'}</TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      disabled={!job.registered || triggerMutation.isPending}
                      onClick={() => triggerMutation.mutate(job.id)}
                    >
                      {t('operations.runNow')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}

        <p className="mt-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('operations.noPauseExplained')}
        </p>
      </Card>

      <Card title={t('operations.outboxTitle')}>
        {outboxQuery.data ? (
          <div className="mb-3 flex flex-wrap gap-2">
            {Object.entries(outboxQuery.data.counts).map(([key, count]) => (
              <Badge key={key} tone={key === 'Failed' && count > 0 ? 'danger' : 'neutral'}>
                {key}: {count}
              </Badge>
            ))}
          </div>
        ) : null}

        <div className="mb-3 max-w-[16rem]">
          <Select
            value={status}
            onValueChange={setStatus}
            placeholder={t('operations.fields.status')}
            options={[
              { value: '', label: t('operations.allStatuses') },
              { value: 'Failed', label: 'Failed' },
              { value: 'Pending', label: 'Pending' },
              { value: 'Sent', label: 'Sent' },
            ]}
          />
        </div>

        {outboxQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {outboxQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('operations.errors.outboxLoadFailed')}</p>
            <Button variant="ghost" onClick={() => void outboxQuery.refetch()}>{t('operations.retry')}</Button>
          </div>
        ) : null}

        {outboxQuery.data && outboxQuery.data.messages.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('operations.outboxEmpty')}</p>
        ) : null}

        {outboxQuery.data && outboxQuery.data.messages.length > 0 ? (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('operations.fields.type')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.created')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.processed')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.actions')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {outboxQuery.data.messages.map((message) => (
                <TableRow key={message.id}>
                  <TableCell>
                    <span className="font-mono text-[length:var(--text-body-sm)]">{message.type}</span>
                    {/* The payload is behind a toggle rather than in the row: it is the thing an operator
                        needs before deciding to replay, and the thing that would make every row
                        unreadable if it were always shown. */}
                    <span className="ms-2">
                      <Button variant="ghost" onClick={() => setExpanded(expanded === message.id ? null : message.id)}>
                        {expanded === message.id ? t('operations.hidePayload') : t('operations.showPayload')}
                      </Button>
                    </span>
                    {expanded === message.id ? (
                      <pre className="mt-2 max-h-[12rem] overflow-auto rounded-[0.5rem] p-2 text-[length:var(--text-caption)]"
                        style={{ backgroundColor: 'var(--color-bg-subtle)', color: 'var(--color-text-primary)' }}>
                        {message.payloadJson}
                      </pre>
                    ) : null}
                  </TableCell>
                  <TableCell>
                    <Badge tone={message.syncStatus === 'Failed' ? 'danger' : message.syncStatus === 'Sent' ? 'success' : 'neutral'}>
                      {message.syncStatus}
                    </Badge>
                  </TableCell>
                  <TableCell>{formatDateTime(message.createdAt, locale)}</TableCell>
                  <TableCell>{message.processedAt ? formatDateTime(message.processedAt, locale) : '—'}</TableCell>
                  <TableCell>
                    {/* Offered only where it means something. Replaying a Pending message duplicates work
                        already queued, and replaying a Sent one would send an integration event twice —
                        the outbox exists to make delivery exactly-once. The server refuses both; the
                        button does not pretend otherwise. */}
                    {message.syncStatus === 'Failed' ? (
                      <Button variant="ghost" disabled={replayMutation.isPending} onClick={() => replayMutation.mutate(message.id)}>
                        {t('operations.replay')}
                      </Button>
                    ) : (
                      <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </Card>

      <Card title={t('operations.erpTitle')}>
        {/*
          Said before any row is read. EPIC-23's adapter has not landed, so what is registered is a
          logging stand-in that accepts everything and sends nothing - a column of Synced without this
          line would be an instrument asserting something untrue.
        */}
        {erpQuery.data && !erpQuery.data.transportConfigured ? (
          <p role="status" className="mb-3 rounded-[0.5rem] p-3"
            style={{ backgroundColor: 'var(--color-warning-bg)', color: 'var(--color-warning-fg)' }}>
            {t('operations.erpNotConfigured')}
          </p>
        ) : null}

        {erpQuery.data ? (
          <div className="mb-3 flex flex-wrap gap-2">
            {Object.entries(erpQuery.data.counts).map(([key, count]) => (
              <Badge key={key} tone={key === 'Failed' && count > 0 ? 'danger' : 'neutral'}>
                {key}: {count}
              </Badge>
            ))}
          </div>
        ) : null}

        {erpQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {erpQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('operations.errors.erpLoadFailed')}</p>
            <Button variant="ghost" onClick={() => void erpQuery.refetch()}>{t('operations.retry')}</Button>
          </div>
        ) : null}

        {erpQuery.data && erpQuery.data.awards.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('operations.erpEmpty')}</p>
        ) : null}

        {erpQuery.data && erpQuery.data.awards.length > 0 ? (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('operations.fields.rfq')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.attempts')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.syncedAt')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.poRef')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.actions')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {erpQuery.data.awards.map((row) => (
                <TableRow key={row.rfqReferenceCode}>
                  <TableCell><span className="font-mono text-[length:var(--text-body-sm)]">{row.rfqReferenceCode}</span></TableCell>
                  <TableCell>
                    <Badge tone={row.erpSyncStatus === 'Failed' ? 'danger' : row.erpSyncStatus === 'Synced' ? 'success' : 'neutral'}>
                      {row.erpSyncStatus}
                    </Badge>
                  </TableCell>
                  <TableCell>{row.erpRetryCount}</TableCell>
                  <TableCell>{row.erpSyncedAt ? formatDateTime(row.erpSyncedAt, locale) : '—'}</TableCell>
                  <TableCell>
                    {/* Shown even when empty on a Synced row, because that combination means the adapter
                        claimed success and returned no reference - which nobody would notice in a count. */}
                    <span className="font-mono text-[length:var(--text-body-sm)]">{row.externalPurchaseOrderRef ?? '—'}</span>
                  </TableCell>
                  <TableCell>
                    {/* §6.1: only a Failed sync retries. The button follows the domain rather than
                        offering an action the aggregate would refuse. */}
                    {row.erpSyncStatus === 'Failed' ? (
                      <Button variant="ghost" disabled={retryErpMutation.isPending}
                        onClick={() => retryErpMutation.mutate(row.rfqReferenceCode)}>
                        {t('operations.retryErp')}
                      </Button>
                    ) : (
                      <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </Card>

      {/*
        SCR-726. A REPORT, with no edit control, and the note at the bottom says why rather than leaving
        the reader to wonder whether the save button is missing. Every number here is read from the thing
        that enforces it - IdentityOptions, the configured clock skew, the expression LoginHandler uses -
        so this panel cannot keep showing an old policy after someone changes the real one.
      */}
      <Card title={t('operations.securityTitle')}>
        {securityQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {securityQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('operations.errors.securityLoadFailed')}</p>
            <Button variant="ghost" onClick={() => void securityQuery.refetch()}>{t('operations.retry')}</Button>
          </div>
        ) : null}

        {securityQuery.data ? (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('operations.fields.control')}</TableHeaderCell>
                <TableHeaderCell>{t('operations.fields.effective')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableRow>
                <TableCell>{t('operations.security.passwordLength')}</TableCell>
                <TableCell>{t('operations.security.characters', { count: securityQuery.data.password.minimumLength })}</TableCell>
              </TableRow>
              <TableRow>
                <TableCell>{t('operations.security.composition')}</TableCell>
                {/* Spelled out as a decision, not left as four blank checkboxes: no forced digit, case or
                    symbol is NIST 800-63B followed on purpose, and a reader who does not know that would
                    file it as a weakness. */}
                <TableCell>
                  {[
                    securityQuery.data.password.requireDigit ? t('operations.security.digit') : null,
                    securityQuery.data.password.requireUppercase ? t('operations.security.uppercase') : null,
                    securityQuery.data.password.requireLowercase ? t('operations.security.lowercase') : null,
                    securityQuery.data.password.requireNonAlphanumeric ? t('operations.security.symbol') : null,
                  ].filter(Boolean).join(', ') || t('operations.security.lengthOnly')}
                </TableCell>
              </TableRow>
              <TableRow>
                <TableCell>{t('operations.security.lockout')}</TableCell>
                <TableCell>
                  {t('operations.security.lockoutValue', {
                    attempts: securityQuery.data.lockout.maxFailedAttempts,
                    minutes: securityQuery.data.lockout.lockoutMinutes,
                  })}
                </TableCell>
              </TableRow>
              <TableRow>
                <TableCell>{t('operations.security.accessToken')}</TableCell>
                {/* The skew is shown with the lifetime rather than on its own row, because the only thing
                    it means is that the lifetime is longer than it says. */}
                <TableCell>
                  {t('operations.security.accessTokenValue', {
                    minutes: securityQuery.data.session.accessTokenMinutes,
                    skew: securityQuery.data.session.clockSkewSeconds,
                  })}
                </TableCell>
              </TableRow>
              <TableRow>
                <TableCell>{t('operations.security.refreshToken')}</TableCell>
                <TableCell>{t('operations.security.days', { count: securityQuery.data.session.refreshTokenDays })}</TableCell>
              </TableRow>
              <TableRow>
                <TableCell>{t('operations.security.mfaRoles')}</TableCell>
                <TableCell>
                  {securityQuery.data.mfaRequiredRoles.length > 0
                    ? securityQuery.data.mfaRequiredRoles.join(', ')
                    /* Not "—": an empty list means NOBODY is required to hold a second factor, which is a
                       finding rather than a blank. */
                    : t('operations.security.mfaNone')}
                </TableCell>
              </TableRow>
              {securityQuery.data.rateLimits.map((limit) => (
                <TableRow key={limit.policy}>
                  <TableCell>
                    {t('operations.security.rateLimit')} <span className="font-mono text-[length:var(--text-body-sm)]">{limit.policy}</span>
                  </TableCell>
                  <TableCell>
                    {t('operations.security.rateLimitValue', { permits: limit.permitLimit, seconds: limit.windowSeconds })}
                  </TableCell>
                </TableRow>
              ))}
              <TableRow>
                <TableCell>{t('operations.security.registration')}</TableCell>
                <TableCell>
                  <Badge tone={securityQuery.data.registrationMode === 'open' ? 'neutral' : 'warning'}>
                    {securityQuery.data.registrationMode}
                  </Badge>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        ) : null}

        <p className="mt-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('operations.security.readOnlyExplained')}
        </p>
      </Card>

      {/*
        SCR-725. Read-only for the same reason as the panel above: the cap and the allow-list are §4.1's
        security control, and the document rules administrators DO own live on SCR-710's screen.
      */}
      <Card title={t('operations.storageTitle')}>
        {storageQuery.isLoading ? <SkeletonTable label={t('common.loading')} /> : null}
        {storageQuery.isError ? (
          <div className="flex flex-col gap-2">
            <p>{t('operations.errors.storageLoadFailed')}</p>
            <Button variant="ghost" onClick={() => void storageQuery.refetch()}>{t('operations.retry')}</Button>
          </div>
        ) : null}

        {storageQuery.data ? (
          <>
            <div className="mb-3 flex flex-wrap gap-2">
              {/* Reachability first and as a chip, because it is the only thing on this card that can be
                  wrong right now. A red one here explains every failing upload in the building. */}
              <Badge tone={storageQuery.data.objectStorageReachable ? 'success' : 'danger'}>
                {t('operations.storage.objectStore')}: {t(storageQuery.data.objectStorageReachable ? 'operations.storage.reachable' : 'operations.storage.unreachable')}
              </Badge>
              <Badge tone={storageQuery.data.virusScannerReachable ? 'success' : 'danger'}>
                {t('operations.storage.scanner')}: {t(storageQuery.data.virusScannerReachable ? 'operations.storage.reachable' : 'operations.storage.unreachable')}
              </Badge>
              <Badge tone={storageQuery.data.pendingScanCount > 0 ? 'warning' : 'neutral'}>
                {t('operations.storage.pendingScans', { count: storageQuery.data.pendingScanCount })}
              </Badge>
            </div>

            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('operations.fields.control')}</TableHeaderCell>
                  <TableHeaderCell>{t('operations.fields.effective')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                <TableRow>
                  <TableCell>{t('operations.storage.maxUpload')}</TableCell>
                  {/* Megabytes, because nobody reads 20971520 as twenty. */}
                  <TableCell>{t('operations.storage.megabytes', { count: Math.round(storageQuery.data.maxUploadBytes / (1024 * 1024)) })}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>{t('operations.storage.allowedTypes')}</TableCell>
                  <TableCell>
                    {/* Both halves of each pair, because the pairing IS the rule: a .pdf whose bytes are a
                        PNG is refused, and a list of bare extensions would hide that. */}
                    <span className="font-mono text-[length:var(--text-body-sm)]">
                      {Object.entries(storageQuery.data.allowedTypes).map(([ext, type]) => `${ext} → ${type}`).join(', ')}
                    </span>
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>{t('operations.storage.bucket')}</TableCell>
                  <TableCell><span className="font-mono text-[length:var(--text-body-sm)]">{storageQuery.data.bucket || '—'}</span></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>{t('operations.storage.documents')}</TableCell>
                  <TableCell>{storageQuery.data.documentCount}</TableCell>
                </TableRow>
              </TableBody>
            </Table>

            <p className="mt-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('operations.storage.readOnlyExplained')}
            </p>
          </>
        ) : null}
      </Card>
    </div>
  )
}
