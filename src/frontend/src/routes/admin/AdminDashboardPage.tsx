// The administrator's dashboard, shown at /back-office/dashboard to holders of admin.users.manage, from the mock-up
// the owner approved on 2026-10-03.
//
// FIVE PARTS, IN THE MOCK-UP'S ORDER. Needs attention first, because it is the one thing an administrator opening the
// page has to act on. Then system health, the ERP, people and access, and finally security beside recent activity.
// Each part is one section of GET /api/v1/admin/dashboard, and each answers on its own: a section the viewer may not
// see is left out with no heading, and a section that failed says so in its own place with a way to try again,
// while the others still show. Trying again refetches the whole dashboard, because the server answers every section
// in one request; a section that failed once is usually fine on the next one.
//
// FRESHNESS. The page refetches every minute while it is open, says when the figures it shows were counted (the
// server's generatedAt, not the moment the browser received them) and offers Refresh for anyone who will not wait.
// The running version comes from /api/v1/meta, the same read the about page makes.
//
// THE STORAGE AND SCANNER CHECK is asked for, never automatic. It calls the object store and the virus scanner, and
// opening a page should not call either. Its answer is kept beside the file-storage tile until the next check, with
// the time it was asked, because a yes from an hour ago is a different fact from a yes now.
//
// SCAN AGAIN, on the virus-scan tile, appears only while documents are stuck. It requeues up to a hundred, says how
// many went back into the queue and which documents could not be requeued because their file was gone, and then
// refetches, so the tile shows what is left.
//
// COUNTS ARE LABELS AND NUMBERS, never sentences built around a number. "Failed: 3" reads correctly in Arabic and
// English for every value, where "3 jobs failed" needs a plural form per language and per number.
//
// NOTHING HERE CALLS THE ERP. The ERP block shows what the database records about the connection, the hourly sync
// and the push; the server builds it from rows and configuration only.

import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AlertTriangle, CheckCircle2, Info, RefreshCw, ShieldCheck, XCircle, ArrowRight } from 'lucide-react'
import {
  Badge, Button, Card, PageHeading, QueryError, SkeletonList,
  Table, TableBody, TableCell, TableHead, TableRow, useToast, type Tone,
} from '../../components/ui'
import { formatDateTime, formatNumber } from '../../lib/datetime'
import { getMeta } from '../../api/meta'
import { probeStorage, type StorageProbe } from '../../api/admin'
import { dashboardAuditActionKey } from '../../api/dashboardAuditActions'
import {
  DASHBOARD_ATTENTION_GROUPS, attentionGroupOf, getAdminDashboard, retryStuckScans,
  type AdminDashboard, type DashboardAttentionItem, type DashboardAuditRow,
  type DashboardErp, type DashboardJobVerdict, type DashboardNeedsAttention, type DashboardPeopleAndAccess,
  type DashboardRecentActivity, type DashboardSection, type DashboardSecurity, type DashboardSystemHealth,
} from '../../api/adminDashboard'

const REFRESH_EVERY_MS = 60_000

const SERIOUS_CHECKS = new Set([
  'no_live_job_servers', 'object_storage_unreachable', 'purchase_order_sends_failed', 'outbox_failed',
  'erp_connection_test_failed', 'erp_sync_failed', 'erp_push_failed_or_stalled',
])

const LINK_TITLES: Record<string, string> = {
  '/back-office/operations': 'operations.title',
  '/back-office/reference': 'referenceAdmin.title',
  '/back-office/integrations': 'integrations.title',
  '/back-office/erp-import': 'erpImport.title',
  '/back-office/staff': 'staff.title',
  '/back-office/audit': 'auditExplorer.title',
  '/back-office/review': 'review.title',
}

const VERDICT_TONE: Record<DashboardJobVerdict, Tone> = {
  ok: 'success',
  late: 'warning',
  failed: 'danger',
  retrying: 'warning',
  missing: 'danger',
  disabled: 'neutral',
}

function useFormatters() {
  const { i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  return {
    n: (value: number) => formatNumber(value, locale, 0),
    when: (value: string | null | undefined) => formatDateTime(value, locale),
  }
}

export function AdminDashboardPage() {
  const { t } = useTranslation()
  const { when } = useFormatters()
  const { notify } = useToast()

  const query = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: getAdminDashboard,
    refetchInterval: REFRESH_EVERY_MS,
  })
  const metaQuery = useQuery({ queryKey: ['meta'], queryFn: getMeta })

  const [lastProbe, setLastProbe] = useState<StorageProbe | null>(null)
  const probe = useMutation({ mutationFn: probeStorage, onSuccess: (answer) => setLastProbe(answer) })

  const rescan = useMutation({
    mutationFn: retryStuckScans,
    onSuccess: (result) => {
      notify({
        kind: result.quarantineFileMissing.length > 0 ? 'info' : 'success',
        title: t('adminDashboard.health.scans.requeued', { value: result.requeued }),
        description: result.quarantineFileMissing.length > 0
          ? t('adminDashboard.health.scans.fileMissing', { codes: result.quarantineFileMissing.join(', ') })
          : undefined,
      })
      void query.refetch()
    },
    onError: () => notify({ kind: 'danger', title: t('adminDashboard.health.scans.requeueFailed') }),
  })

  const retry = () => void query.refetch()

  const version = metaQuery.data?.version
  const commit = metaQuery.data?.commit

  const header = (
    <PageHeading
      title={t('adminDashboard.title')}
      subtitle={t('adminDashboard.eyebrow')}
      meta={
        <span className="flex flex-wrap items-center gap-x-3.5 gap-y-1 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
          {query.data ? <span className="num">{t('adminDashboard.updatedAt', { time: when(query.data.generatedAt) })}</span> : null}
          <span>{t('adminDashboard.autoRefresh')}</span>
          {version ? (
            <span className="num">
              {commit ? t('adminDashboard.versionWithCommit', { version, commit: commit.slice(0, 7) }) : t('adminDashboard.version', { version })}
            </span>
          ) : null}
        </span>
      }
      actions={
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" disabled={probe.isPending} onClick={() => probe.mutate()}>
            <ShieldCheck aria-hidden="true" size={16} />
            {t(probe.isPending ? 'adminDashboard.probing' : 'adminDashboard.probe')}
          </Button>
          <Button disabled={query.isFetching} onClick={retry}>
            <RefreshCw aria-hidden="true" size={16} />
            {t(query.isFetching ? 'adminDashboard.refreshing' : 'adminDashboard.refresh')}
          </Button>
        </div>
      }
    />
  )

  if (query.isPending) {
    return (
      <div className="flex flex-col gap-6">
        {header}
        <SkeletonList label={t('common.loading')} />
      </div>
    )
  }

  if (query.isError) {
    return (
      <div className="flex flex-col gap-6">
        {header}
        <QueryError error={query.error} errorText={t('adminDashboard.loadFailed')} onRetry={retry} />
      </div>
    )
  }

  const data: AdminDashboard = query.data

  return (
    <div className="flex flex-col gap-6">
      {header}
      {probe.isError ? (
        <p role="alert" style={{ color: 'var(--color-danger-fg)' }}>{t('adminDashboard.probeFailed')}</p>
      ) : null}

      <SectionSlot section={data.needsAttention} title={t('adminDashboard.attention.title')} onRetry={retry}>
        {(attention) => <NeedsAttention attention={attention} />}
      </SectionSlot>

      <SectionSlot section={data.systemHealth} title={t('adminDashboard.health.title')} onRetry={retry}>
        {(health) => (
          <SystemHealth
            health={health}
            lastProbe={lastProbe}
            rescanning={rescan.isPending}
            onRescan={() => rescan.mutate()}
          />
        )}
      </SectionSlot>

      <SectionSlot section={data.erp} title={t('adminDashboard.erp.title')} onRetry={retry}>
        {(erp) => <Erp erp={erp} onRetry={retry} />}
      </SectionSlot>

      <SectionSlot section={data.peopleAndAccess} title={t('adminDashboard.people.title')} onRetry={retry}>
        {(people) => <PeopleAndAccess people={people} />}
      </SectionSlot>

      <div className="grid items-start gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(100%,23.75rem),1fr))]">
        <SectionSlot section={data.security} title={t('adminDashboard.security.title')} onRetry={retry}>
          {(security) => <Security security={security} />}
        </SectionSlot>
        <SectionSlot section={data.recentActivity} title={t('adminDashboard.activity.title')} onRetry={retry}>
          {(activity) => <RecentActivity activity={activity} />}
        </SectionSlot>
      </div>
    </div>
  )
}

function SectionSlot<T>({ section, title, onRetry, children }: Readonly<{
  section: DashboardSection<T>
  title: string
  onRetry: () => void
  children: (data: T) => ReactNode
}>) {
  if (section.status === 'hidden') return null
  if (section.status === 'failed' || section.data === null || section.data === undefined) {
    return <SectionFailed title={title} onRetry={onRetry} />
  }
  return <>{children(section.data)}</>
}

function SectionFailed({ title, onRetry }: Readonly<{ title: string; onRetry: () => void }>) {
  const { t } = useTranslation()
  return (
    <Card title={title}>
      <div className="flex flex-col items-start gap-3">
        <p className="m-0 font-[var(--fw-medium)]">{t('adminDashboard.sectionFailed')}</p>
        <p className="m-0 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('adminDashboard.sectionFailedHint')}
        </p>
        <Button size="sm" variant="ghost" onClick={onRetry}>{t('adminDashboard.retry')}</Button>
      </div>
    </Card>
  )
}

function Muted({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>{children}</span>
  )
}

function Pair({ label, children }: Readonly<{ label: ReactNode; children: ReactNode }>) {
  return (
    <div
      className="flex items-baseline justify-between gap-3 py-1.5 text-[length:var(--text-body-sm)]"
      style={{ borderBlockStart: '1px solid var(--color-border)' }}
    >
      <span>{label}</span>
      <span className="num text-end">{children}</span>
    </div>
  )
}

function Pairs({ children }: Readonly<{ children: ReactNode }>) {
  return <div className="flex flex-col [&>*:first-child]:border-t-0">{children}</div>
}

function Big({ children, muted = false }: Readonly<{ children: ReactNode; muted?: boolean }>) {
  return (
    <span
      className="num text-[length:var(--text-display)] font-[var(--fw-semibold)] leading-none tracking-[-0.02em]"
      style={{ color: muted ? 'var(--color-text-secondary)' : 'var(--color-text-primary)' }}
    >
      {children}
    </span>
  )
}

function SubHeading({ id, children }: Readonly<{ id: string; children: ReactNode }>) {
  return (
    <h2 id={id} className="m-0 text-[length:var(--text-h4)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
      {children}
    </h2>
  )
}

function NeedsAttention({ attention }: Readonly<{ attention: DashboardNeedsAttention }>) {
  const { t } = useTranslation()
  const { n } = useFormatters()

  const groupsNotRun = DASHBOARD_ATTENTION_GROUPS.filter((group) =>
    attention.checksNotRun.some((check) => attentionGroupOf(check) === group))

  const chip = attention.allClear
    ? <Badge tone="success">{t('adminDashboard.attention.allClearChip')}</Badge>
    : attention.items.length > 0 ? <Badge tone="danger">{n(attention.items.length)}</Badge> : null

  return (
    <Card title={t('adminDashboard.attention.title')} action={chip} flush>
      <ul className="m-0 list-none p-0">
        {attention.items.map((item) => <AttentionRow key={item.key} item={item} />)}
        {groupsNotRun.length > 0 ? (
          <li className="flex items-center gap-3 px-4 py-3" style={{ backgroundColor: 'var(--color-bg-sunken)', borderBlockStart: '1px solid var(--color-border)' }}>
            <SeverityIcon tone="info" />
            <span>
              {t('adminDashboard.attention.notRun', {
                sections: groupsNotRun.map((group) => t(`adminDashboard.attention.groups.${group}`)).join(t('adminDashboard.listSeparator')),
              })}
            </span>
          </li>
        ) : null}
        {attention.allClear ? (
          <li className="flex items-center gap-3 px-4 py-3">
            <SeverityIcon tone="success" />
            <span className="font-[var(--fw-medium)]">{t('adminDashboard.attention.allClear')}</span>
          </li>
        ) : null}
      </ul>
    </Card>
  )
}

function SeverityIcon({ tone }: Readonly<{ tone: 'danger' | 'warning' | 'info' | 'success' }>) {
  const Icon = { danger: XCircle, warning: AlertTriangle, info: Info, success: CheckCircle2 }[tone]
  return (
    <span
      className="grid size-8 flex-none place-items-center rounded-[var(--radius-md)]"
      style={{ backgroundColor: `var(--color-${tone}-bg)`, color: `var(--color-${tone}-fg)` }}
    >
      <Icon aria-hidden="true" size={18} />
    </span>
  )
}

function AttentionRow({ item }: Readonly<{ item: DashboardAttentionItem }>) {
  const { t } = useTranslation()
  const { n } = useFormatters()
  const tone = SERIOUS_CHECKS.has(item.key) ? 'danger' : 'warning'
  const destination = item.link ? LINK_TITLES[item.link] : undefined

  return (
    <li className="flex flex-wrap items-center gap-3 px-4 py-3 [&:not(:first-child)]:border-t" style={{ borderColor: 'var(--color-border)' }}>
      <SeverityIcon tone={tone} />
      <div className="flex min-w-0 flex-[1_1_16rem] flex-col gap-0.5">
        <span className="flex flex-wrap items-center gap-2 font-[var(--fw-medium)]">
          {t(`adminDashboard.attention.items.${item.key}`, { defaultValue: item.key })}
          {item.count !== null ? <Badge tone={tone}>{n(item.count)}</Badge> : null}
        </span>
        {item.references.length > 0 ? (
          <span className="flex flex-wrap gap-x-2 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {item.references.map((reference) => reference.link ? (
              <Link key={reference.code} to={reference.link} className="num">{reference.code}</Link>
            ) : (
              <span key={reference.code} className="num">{reference.code}</span>
            ))}
          </span>
        ) : null}
      </div>
      {item.link ? (
        <Link to={item.link} className="inline-flex items-center gap-1 whitespace-nowrap font-[var(--fw-medium)]" style={{ color: 'var(--color-text-link)' }}>
          {destination ? t(destination) : t('adminDashboard.open')}
          <ArrowRight aria-hidden="true" size={16} className="rtl:-scale-x-100" />
        </Link>
      ) : null}
    </li>
  )
}

function Tile({ label, chip, value, children, action }: Readonly<{
  label: string
  chip: ReactNode
  value: ReactNode
  children?: ReactNode
  action?: ReactNode
}>) {
  return (
    <li
      className="flex flex-col gap-2 rounded-[var(--radius-lg)] p-4"
      style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{label}</span>
        {chip}
      </div>
      <span className="num text-[length:var(--text-h3)] font-[var(--fw-semibold)] leading-tight">{value}</span>
      {children ? <div className="flex flex-col gap-0.5">{children}</div> : null}
      {action ? <div className="mt-1">{action}</div> : null}
    </li>
  )
}

function lateAfter(minutes: number, t: (key: string, options?: Record<string, unknown>) => string, n: (value: number) => string) {
  return minutes % 60 === 0
    ? t('adminDashboard.health.jobs.lateAfterHours', { value: n(minutes / 60) })
    : t('adminDashboard.health.jobs.lateAfterMinutes', { value: n(minutes) })
}

function SystemHealth({ health, lastProbe, rescanning, onRescan }: Readonly<{
  health: DashboardSystemHealth
  lastProbe: StorageProbe | null
  rescanning: boolean
  onRescan: () => void
}>) {
  const { t } = useTranslation()
  const { n, when } = useFormatters()

  const jobs = health.jobs.jobs
  const onTime = jobs.filter((job) => job.verdict === 'ok').length
  const queue = health.queue
  const queueTone: Tone = queue.liveServers === 0 ? 'danger' : queue.retrying > 0 || queue.failed > 0 ? 'warning' : 'success'
  const queueState = queue.liveServers === 0 ? 'noServer' : queueTone === 'warning' ? 'needsLook' : 'healthy'
  const emptyLists = health.referenceLists.filter((list) => list.active === 0)
  const listsWithCodes = health.referenceLists.length - emptyLists.length
  const storageReachable = lastProbe ? lastProbe.objectStorageReachable : health.objectStorage.reachable

  const healthy = <Badge tone="success">{t('adminDashboard.health.healthy')}</Badge>
  const needsLook = <Badge tone="warning">{t('adminDashboard.health.needsLook')}</Badge>

  return (
    <section aria-labelledby="dashboard-health" className="flex flex-col gap-3">
      <SubHeading id="dashboard-health">{t('adminDashboard.health.title')}</SubHeading>
      <div className="grid items-start gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(100%,20rem),1fr))]">
        <Card
          title={t('adminDashboard.health.jobs.title')}
          action={
            health.jobs.recurringEnabled
              ? <Badge tone={onTime === jobs.length ? 'success' : 'warning'}>{t('adminDashboard.health.jobs.onTime', { onTime: n(onTime), total: n(jobs.length) })}</Badge>
              : <Badge tone="neutral">{t('adminDashboard.health.jobs.switchedOff')}</Badge>
          }
          flush
        >
          <Table flush>
            <TableHead labels={[
              t('adminDashboard.health.jobs.job'),
              t('adminDashboard.health.jobs.lateAfter'),
              t('adminDashboard.health.jobs.lastRun'),
              t('adminDashboard.health.jobs.status'),
            ]} />
            <TableBody>
              {jobs.map((job) => (
                <TableRow key={job.id}>
                  <TableCell>{t(`adminDashboard.health.jobs.names.${job.id}`, { defaultValue: job.id })}</TableCell>
                  <TableCell><Muted>{lateAfter(job.lateAfterMinutes, t, n)}</Muted></TableCell>
                  <TableCell><span className="num">{job.lastExecution ? when(job.lastExecution) : t('adminDashboard.never')}</span></TableCell>
                  <TableCell><Badge tone={VERDICT_TONE[job.verdict] ?? 'neutral'}>{t(`adminDashboard.health.jobs.verdicts.${job.verdict}`, { defaultValue: job.verdict })}</Badge></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>

        <ul className="m-0 grid list-none gap-4 p-0 [grid-template-columns:repeat(auto-fit,minmax(min(100%,11.875rem),1fr))]">
          <Tile
            label={t('adminDashboard.health.queue.label')}
            chip={<Badge tone={queueTone}>{t(`adminDashboard.health.queue.${queueState}`)}</Badge>}
            value={t('adminDashboard.health.queue.waiting', { value: n(queue.enqueued) })}
          >
            <Muted>{t('adminDashboard.health.queue.running', { value: n(queue.processing) })}</Muted>
            <Muted>{t('adminDashboard.health.queue.retrying', { value: n(queue.retrying) })}</Muted>
            <Muted>{t('adminDashboard.health.queue.failed', { value: n(queue.failed) })}</Muted>
            <Muted>{t('adminDashboard.health.queue.servers', { value: n(queue.liveServers), minutes: n(queue.heartbeatWithinMinutes) })}</Muted>
          </Tile>

          <Tile
            label={t('adminDashboard.health.email.label', { days: n(health.email.windowDays) })}
            chip={health.email.failedInWindow > 0 ? needsLook : healthy}
            value={t('adminDashboard.health.email.failed', { value: n(health.email.failedInWindow) })}
          >
            <Muted>{t('adminDashboard.health.email.retrying', { value: n(health.email.retrying) })}</Muted>
          </Tile>

          <Tile
            label={t('adminDashboard.health.outbox.label')}
            chip={health.outbox.stuck > 0 || health.outbox.failed > 0 ? needsLook : healthy}
            value={t('adminDashboard.health.outbox.waiting', { value: n(health.outbox.pending) })}
          >
            <Muted>{t('adminDashboard.health.outbox.stuck', { value: n(health.outbox.stuck), minutes: n(health.outbox.stuckAfterMinutes) })}</Muted>
            <Muted>{t('adminDashboard.health.outbox.failed', { value: n(health.outbox.failed) })}</Muted>
          </Tile>

          <Tile
            label={t('adminDashboard.health.scans.label')}
            chip={health.scans.stuck > 0 ? <Badge tone="warning">{t('adminDashboard.health.scans.stuckChip')}</Badge> : healthy}
            value={t('adminDashboard.health.scans.stuck', { value: n(health.scans.stuck) })}
            action={health.scans.stuck > 0 ? (
              <Button size="sm" variant="secondary" disabled={rescanning} onClick={onRescan}>
                {t(rescanning ? 'adminDashboard.health.scans.rescanning' : 'adminDashboard.health.scans.rescan')}
              </Button>
            ) : undefined}
          >
            <Muted>{t('adminDashboard.health.scans.threshold', { minutes: n(health.scans.stuckAfterMinutes) })}</Muted>
          </Tile>

          <Tile
            label={t('adminDashboard.health.storage.label')}
            chip={<Badge tone={storageReachable ? 'success' : 'danger'}>{t(storageReachable ? 'adminDashboard.health.storage.reachable' : 'adminDashboard.health.storage.unreachable')}</Badge>}
            value={t(storageReachable ? 'adminDashboard.health.storage.reachable' : 'adminDashboard.health.storage.unreachable')}
          >
            {lastProbe ? (
              <>
                <Muted>
                  {t(lastProbe.virusScannerReachable ? 'adminDashboard.health.storage.scannerReachable' : 'adminDashboard.health.storage.scannerUnreachable')}
                </Muted>
                <Muted>{t('adminDashboard.health.storage.checkedAt', { time: when(lastProbe.checkedAt) })}</Muted>
              </>
            ) : (
              <Muted>{t('adminDashboard.health.storage.scannerNotChecked')}</Muted>
            )}
          </Tile>

          <Tile
            label={t('adminDashboard.health.migrations.label')}
            chip={health.pendingMigrations.length > 0
              ? <Badge tone="warning">{t('adminDashboard.health.migrations.pendingChip')}</Badge>
              : <Badge tone="success">{t('adminDashboard.health.migrations.upToDate')}</Badge>}
            value={health.pendingMigrations.length > 0
              ? t('adminDashboard.health.migrations.pending', { value: n(health.pendingMigrations.length) })
              : t('adminDashboard.health.migrations.allApplied')}
          >
            {health.pendingMigrations.map((name) => <Muted key={name}><code>{name}</code></Muted>)}
          </Tile>

          <Tile
            label={t('adminDashboard.health.reference.label')}
            chip={emptyLists.length > 0 ? needsLook : healthy}
            value={t('adminDashboard.health.reference.withCodes', { active: n(listsWithCodes), total: n(health.referenceLists.length) })}
          >
            {emptyLists.length > 0 ? (
              <Muted>
                {t('adminDashboard.health.reference.empty', {
                  tables: emptyLists.map((list) => t(`adminOverview.tables.${list.table}`, { defaultValue: list.table })).join(t('adminDashboard.listSeparator')),
                })}
              </Muted>
            ) : (
              <Muted>{t('adminDashboard.health.reference.allHaveCodes')}</Muted>
            )}
          </Tile>

          <Tile
            label={t('adminDashboard.health.purchaseOrders.label')}
            chip={health.purchaseOrderTransport.configured
              ? <Badge tone={health.purchaseOrderTransport.failedSends > 0 ? 'danger' : 'success'}>{t('adminDashboard.health.purchaseOrders.sent')}</Badge>
              : <Badge tone="neutral">{t('adminDashboard.health.purchaseOrders.notSent')}</Badge>}
            value={t('adminDashboard.health.purchaseOrders.failed', { value: n(health.purchaseOrderTransport.failedSends) })}
          >
            {health.purchaseOrderTransport.configured ? null : <Muted>{t('adminDashboard.health.purchaseOrders.notSentBody')}</Muted>}
          </Tile>
        </ul>
      </div>
    </section>
  )
}

function PartFailed() {
  const { t } = useTranslation()
  return <Muted>{t('adminDashboard.erp.partFailed')}</Muted>
}

function Erp({ erp, onRetry }: Readonly<{ erp: DashboardErp; onRetry: () => void }>) {
  const { t } = useTranslation()
  const { n, when } = useFormatters()
  const anyFailed = [erp.connection, erp.sync, erp.push].some((part) => part.status !== 'ok')

  const connection = erp.connection.status === 'ok' ? erp.connection.data : null
  const sync = erp.sync.status === 'ok' ? erp.sync.data : null
  const push = erp.push.status === 'ok' ? erp.push.data : null

  const syncTone: Tone = !sync?.outcome ? 'neutral'
    : sync.outcome === 'Succeeded' ? 'success'
      : sync.outcome === 'Failed' ? 'danger' : 'warning'

  return (
    <Card title={t('adminDashboard.erp.title')} action={<Muted>{t('adminDashboard.erp.scope')}</Muted>} flush>
      <div className="grid [grid-template-columns:repeat(auto-fit,minmax(min(100%,16.25rem),1fr))]">
        <div className="flex flex-col gap-2.5 p-4" style={{ borderInlineEnd: '1px solid var(--color-border)' }}>
          <div className="flex items-center justify-between gap-2">
            <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.erp.connection.title')}</span>
            {connection ? (
              connection.source === 'None' ? <Badge tone="neutral">{t('adminDashboard.erp.connection.notSetUp')}</Badge>
                : connection.https === false ? <Badge tone="warning">{t('adminDashboard.erp.connection.http')}</Badge>
                  : connection.https === true ? <Badge tone="success">{t('adminDashboard.erp.connection.https')}</Badge> : null
            ) : null}
          </div>
          {connection ? (
            <>
              <span className="text-[length:var(--text-h3)] font-[var(--fw-semibold)]">
                {t(connection.enabled ? 'adminDashboard.erp.connection.on' : 'adminDashboard.erp.connection.off')}
              </span>
              <Pairs>
                <Pair label={<Muted>{t('adminDashboard.erp.connection.host')}</Muted>}>{connection.host ?? '—'}</Pair>
                <Pair label={<Muted>{t('adminDashboard.erp.connection.source')}</Muted>}>{t(`adminDashboard.erp.connection.sources.${connection.source}`, { defaultValue: connection.source })}</Pair>
                <Pair label={<Muted>{t('adminDashboard.erp.connection.lastTest')}</Muted>}>
                  {connection.lastTestedAt === null || connection.lastTestSucceeded === null ? (
                    <Badge tone="neutral">{t('adminDashboard.erp.connection.neverTested')}</Badge>
                  ) : (
                    <Badge tone={connection.lastTestSucceeded ? 'success' : 'danger'}>
                      {t(connection.lastTestSucceeded ? 'adminDashboard.erp.connection.passed' : 'adminDashboard.erp.connection.failed', { time: when(connection.lastTestedAt) })}
                    </Badge>
                  )}
                </Pair>
              </Pairs>
            </>
          ) : <PartFailed />}
        </div>

        <div className="flex flex-col gap-2.5 p-4" style={{ borderInlineEnd: '1px solid var(--color-border)' }}>
          <div className="flex items-center justify-between gap-2">
            <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.erp.sync.title')}</span>
            {sync ? (
              <span className="flex flex-wrap gap-1">
                {sync.stale ? <Badge tone="warning">{t('adminDashboard.erp.sync.stale')}</Badge> : null}
                <Badge tone={syncTone}>{t(sync.outcome ? `adminDashboard.erp.sync.outcomes.${sync.outcome}` : 'adminDashboard.erp.sync.notRunYet', { defaultValue: sync.outcome ?? '' })}</Badge>
              </span>
            ) : null}
          </div>
          {sync ? (
            <>
              <span className="num text-[length:var(--text-h3)] font-[var(--fw-semibold)]">
                {sync.lastRunAt ? t('adminDashboard.erp.sync.lastRun', { time: when(sync.lastRunAt) }) : t('adminDashboard.erp.sync.notRunYet')}
              </span>
              {sync.enabled ? null : <Muted>{t('adminDashboard.erp.sync.connectionOff')}</Muted>}
              {sync.unfinishedRunStartedAt ? (
                <Badge tone="warning">{t('adminDashboard.erp.sync.unfinished', { time: when(sync.unfinishedRunStartedAt) })}</Badge>
              ) : null}
              {sync.counts ? (
                <Pairs>
                  <Pair label={t('adminDashboard.erp.sync.inErp')}>{n(sync.counts.erpSuppliers)}</Pair>
                  <Pair label={t('adminDashboard.erp.sync.created')}>{n(sync.counts.created)}</Pair>
                  <Pair label={t('adminDashboard.erp.sync.updated')}>{n(sync.counts.updated)}</Pair>
                  <Pair label={t('adminDashboard.erp.sync.suspended')}>{n(sync.counts.suspended)}</Pair>
                  <Pair label={t('adminDashboard.erp.sync.refused')}>{n(sync.counts.refused)}</Pair>
                  <Pair label={t('adminDashboard.erp.sync.failed')}>{n(sync.counts.failed)}</Pair>
                </Pairs>
              ) : null}
            </>
          ) : <PartFailed />}
        </div>

        <div className="flex flex-col gap-2.5 p-4">
          <div className="flex items-center justify-between gap-2">
            <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.erp.push.title')}</span>
            {push ? <Badge tone={push.switchOn ? 'success' : 'neutral'}>{t(push.switchOn ? 'adminDashboard.erp.push.on' : 'adminDashboard.erp.push.off')}</Badge> : null}
          </div>
          {push ? (
            <>
              {push.switchOn ? null : <span className="font-[var(--fw-medium)]">{t('adminDashboard.erp.push.offLine')}</span>}
              <Pairs>
                <Pair label={t('adminDashboard.erp.push.waiting')}>{n(push.waiting)}</Pair>
                <Pair label={t('adminDashboard.erp.push.failed')}>{n(push.failed)}</Pair>
                {push.stalled === null ? null : <Pair label={t('adminDashboard.erp.push.stalled')}>{n(push.stalled)}</Pair>}
                <Pair label={<Muted>{t('adminDashboard.erp.push.defaultGroup')}</Muted>}>{push.defaultGroup ?? t('adminDashboard.erp.push.noGroup')}</Pair>
                <Pair label={<Muted>{t('adminDashboard.erp.push.writeHost')}</Muted>}>
                  <span className="font-[var(--fw-semibold)]">{t(push.hostOnWriteHosts ? 'adminDashboard.yes' : 'adminDashboard.no')}</span>
                </Pair>
              </Pairs>
            </>
          ) : <PartFailed />}
        </div>
      </div>
      {anyFailed ? (
        <div className="px-4 pb-4">
          <Button size="sm" variant="ghost" onClick={onRetry}>{t('adminDashboard.retry')}</Button>
        </div>
      ) : null}
    </Card>
  )
}

function PeopleAndAccess({ people }: Readonly<{ people: DashboardPeopleAndAccess }>) {
  const { t } = useTranslation()
  const { n } = useFormatters()
  const invited = people.staffInvitedNeverSignedIn
  const lockTone = (value: number): Tone => (value > 0 ? 'warning' : 'neutral')

  return (
    <section aria-labelledby="dashboard-people" className="flex flex-col gap-3">
      <SubHeading id="dashboard-people">{t('adminDashboard.people.title')}</SubHeading>
      <div className="grid items-start gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(100%,16.25rem),1fr))]">
        <Card title={t('adminDashboard.people.accounts')}>
          <div className="flex flex-col gap-3.5">
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1">
                <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.people.staffAccounts')}</span>
                <Big>{n(people.staff.active)}</Big>
                <Muted>{t('adminDashboard.people.activeInactive', { active: n(people.staff.active), inactive: n(people.staff.inactive) })}</Muted>
              </div>
              <div className="flex flex-col gap-1">
                <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.people.supplierLogins')}</span>
                <Big>{n(people.suppliers.active)}</Big>
                <Muted>{t('adminDashboard.people.activeInactive', { active: n(people.suppliers.active), inactive: n(people.suppliers.inactive) })}</Muted>
              </div>
            </div>
            <Pairs>
              <Pair label={t('adminDashboard.people.staffSessions')}>{n(people.staff.activeSessions)}</Pair>
              <Pair label={t('adminDashboard.people.supplierSessions')}>{n(people.suppliers.activeSessions)}</Pair>
              <Pair label={t('adminDashboard.people.placeholderLogins')}>{n(people.supplierLoginsOnPlaceholderAddresses)}</Pair>
            </Pairs>
          </div>
        </Card>

        <Card title={t('adminDashboard.people.roles')} action={<Muted>{t('adminDashboard.people.rolesTotal', { value: n(people.staff.activeWithARole) })}</Muted>}>
          <Pairs>
            {people.activeUsersByRole.map((role) => (
              <Pair key={role.role} label={t(`staff.roles.${role.role}`, { defaultValue: role.role })}>{n(role.activeUsers)}</Pair>
            ))}
          </Pairs>
          {people.twoFactorRequiredRoles.length > 0 ? (
            <p className="m-0 mt-3 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('adminDashboard.people.twoFactorRoles', {
                roles: people.twoFactorRequiredRoles.map((role) => t(`staff.roles.${role}`, { defaultValue: role })).join(t('adminDashboard.listSeparator')),
              })}
            </p>
          ) : null}
        </Card>

        <Card title={t('adminDashboard.people.access')}>
          <Pairs>
            <Pair label={t('adminDashboard.people.staffInvitations')}>{n(people.staff.pendingInvitations)}</Pair>
            <Pair label={t('adminDashboard.people.supplierInvitations')}>{n(people.suppliers.pendingInvitations)}</Pair>
            <Pair label={t('adminDashboard.people.neverSignedInValid')}>{n(invited.linkStillValid)}</Pair>
            <Pair label={t('adminDashboard.people.neverSignedInExpired')}><Badge tone={lockTone(invited.linkExpired)}>{n(invited.linkExpired)}</Badge></Pair>
            <Pair label={t('adminDashboard.people.neverSignedInNoLink')}>{n(invited.noLinkYet)}</Pair>
            <Pair label={t('adminDashboard.people.neverSignedInUsed')}>{n(invited.linkUsed)}</Pair>
            <Pair label={t('adminDashboard.people.staffLockedOut')}><Badge tone={lockTone(people.staff.lockedOut)}>{n(people.staff.lockedOut)}</Badge></Pair>
            <Pair label={t('adminDashboard.people.supplierLockedOut')}><Badge tone={lockTone(people.suppliers.lockedOut)}>{n(people.suppliers.lockedOut)}</Badge></Pair>
            <Pair label={t('adminDashboard.people.staffCannotSignIn')}><Badge tone={lockTone(people.staff.cannotSignIn)}>{n(people.staff.cannotSignIn)}</Badge></Pair>
            <Pair label={t('adminDashboard.people.supplierCannotSignIn')}><Badge tone={lockTone(people.suppliers.cannotSignIn)}>{n(people.suppliers.cannotSignIn)}</Badge></Pair>
          </Pairs>
        </Card>

        <Card title={t('adminDashboard.people.organisations')}>
          <Pairs>
            {people.organisationsByType.map((type) => (
              <Pair key={type.type} label={t(`organizations.types.${type.type}`, { defaultValue: type.type })}>
                {t('adminDashboard.people.activeInactive', { active: n(type.active), inactive: n(type.inactive) })}
              </Pair>
            ))}
          </Pairs>
        </Card>
      </div>
    </section>
  )
}

function actionLabel(action: string, t: (key: string, options?: Record<string, unknown>) => string) {
  return t(`dashboard.auditActions.${dashboardAuditActionKey(action)}`, { defaultValue: action })
}

function AuditRows({ rows }: Readonly<{ rows: DashboardAuditRow[] }>) {
  const { t } = useTranslation()
  const { when } = useFormatters()
  if (rows.length === 0) {
    return <p className="m-0 px-4 py-3"><Muted>{t('adminDashboard.nothingYet')}</Muted></p>
  }
  return (
    <Table flush>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.id}>
            <TableCell><span className="num whitespace-nowrap"><Muted>{when(row.occurredAt)}</Muted></span></TableCell>
            <TableCell>
              <div>{actionLabel(row.action, t)}</div>
              <Muted>{row.actorName}</Muted>
            </TableCell>
            <TableCell><span className="num whitespace-nowrap"><Muted>{row.referenceCode ?? '—'}</Muted></span></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

function Security({ security }: Readonly<{ security: DashboardSecurity }>) {
  const { t, i18n } = useTranslation()
  const { n } = useFormatters()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'

  return (
    <Card title={t('adminDashboard.security.title')} action={<Muted>{t('adminDashboard.auditOnly')}</Muted>} flush>
      <Table flush>
        <TableHead labels={[
          t('adminDashboard.security.event'),
          t('adminDashboard.security.last24Hours'),
          t('adminDashboard.security.last7Days'),
        ]} />
        <TableBody>
          {security.events.map((event) => (
            <TableRow key={event.action}>
              <TableCell>{actionLabel(event.action, t)}</TableCell>
              <TableCell>
                <span className="flex flex-wrap items-center gap-2">
                  <Badge tone={event.spiking ? 'warning' : 'neutral'}>{n(event.last24Hours)}</Badge>
                  {event.spiking ? <Muted>{t('adminDashboard.security.spiking')}</Muted> : null}
                </span>
              </TableCell>
              <TableCell><span className="num">{n(event.last7Days)}</span></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {security.countedSince ? (
        <p className="m-0 px-4 py-2.5 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)', backgroundColor: 'var(--color-bg-sunken)', borderBlockStart: '1px solid var(--color-border)' }}>
          {t('adminDashboard.security.countedSince', { date: formatDateTime(security.countedSince, locale) })}
        </p>
      ) : null}
      <h3 className="m-0 px-4 pb-1.5 pt-3.5 text-[length:var(--text-body-sm)] font-[var(--fw-semibold)]" style={{ borderBlockStart: '1px solid var(--color-border)' }}>
        {t('adminDashboard.security.sensitive')}
      </h3>
      <AuditRows rows={security.sensitiveChanges} />
    </Card>
  )
}

function RecentActivity({ activity }: Readonly<{ activity: DashboardRecentActivity }>) {
  const { t } = useTranslation()
  const { n } = useFormatters()

  return (
    <Card title={t('adminDashboard.activity.title')} action={<Muted>{t('adminDashboard.auditOnly')}</Muted>} flush>
      <div className="grid grid-cols-2 gap-3 p-4" style={{ borderBlockEnd: '1px solid var(--color-border)' }}>
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.activity.people')}</span>
          <Big>{n(activity.last24Hours)}</Big>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>{t('adminDashboard.activity.system')}</span>
          <Big muted>{n(activity.systemLast24Hours)}</Big>
        </div>
      </div>
      <AuditRows rows={activity.latest} />
      <div className="px-4 py-2.5" style={{ borderBlockStart: '1px solid var(--color-border)' }}>
        <Link to="/back-office/audit" className="inline-flex items-center gap-1 font-[var(--fw-medium)]" style={{ color: 'var(--color-text-link)' }}>
          {t('adminDashboard.activity.openAudit')}
          <ArrowRight aria-hidden="true" size={16} className="rtl:-scale-x-100" />
        </Link>
      </div>
    </Card>
  )
}
