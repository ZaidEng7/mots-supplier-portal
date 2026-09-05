import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery } from '@tanstack/react-query'
import {
  Button, Card, Field, Input, SkeletonTable,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../../components/ui'
import { formatDateTime } from '../../lib/datetime'
import { AuditApiError, downloadAuditLog, searchAuditLog, type AuditSearchFilters } from '../../api/audit'

/**
 * SCR-720, `/back-office/audit`, `system_admin`, P2 (FR-AUD-004).
 *
 * <p>Three audit endpoints existed and no screen called any of them. The supplier-facing two were
 * closed in T-079 by putting them on the supplier's own settings screen; this is the third — the
 * platform-wide search and its filtered CSV export, behind `audit.read`.</p>
 *
 * <p><b>Every filter is applied server-side, and nothing is filtered here.</b> The API refuses an
 * unrecognised value with a 422 naming the field (MSP-75), and filtering a page in the browser would
 * silently reintroduce the exact defect that refusal exists to prevent: a search narrowed to one actor
 * answering with every actor's rows, on the screen a compliance officer trusts most.</p>
 *
 * <p><b>The refusal is shown against the field it names.</b> A malformed id or date is a mistake
 * somebody can correct, and "invalid request" would leave them guessing which of six boxes to fix.</p>
 */
export function AuditExplorerPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const { notify } = useToast()

  // Two pieces of state, not one: what is typed, and what was searched. Refetching on every keystroke
  // would issue a query per character against the widest read in the product, and a filter half-typed
  // is a filter that means something else.
  const [draft, setDraft] = useState<AuditSearchFilters>({})
  const [applied, setApplied] = useState<AuditSearchFilters>({})
  const [rejectedField, setRejectedField] = useState<string | null>(null)

  const query = useInfiniteQuery({
    queryKey: ['audit-search', applied],
    queryFn: ({ pageParam }) => searchAuditLog(applied, pageParam ?? undefined),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => (last.pagination.hasMore ? last.pagination.nextCursor : null),
  })

  const rows = query.data?.pages.flatMap((page) => page.data) ?? []
  const error = query.error instanceof AuditApiError ? query.error : null
  const refusedField = error?.code === 'INVALID_FILTER_VALUE' ? error.field ?? null : null

  const field = (key: keyof AuditSearchFilters) => ({
    value: draft[key] ?? '',
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setDraft((prev) => ({ ...prev, [key]: e.target.value })),
  })

  const search = () => {
    setRejectedField(null)
    setApplied(draft)
  }

  const exportCsv = async () => {
    try {
      await downloadAuditLog(applied)
    } catch (raised) {
      // The export takes the SAME filters as the list, so a refusal here names the same field - but it
      // is a separate request and can fail on its own (a session that expired between the two).
      setRejectedField(raised instanceof AuditApiError ? raised.field ?? null : null)
      notify({ kind: 'danger', title: t('auditExplorer.errors.exportFailed') })
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('auditExplorer.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('auditExplorer.subtitle')}</p>
      </div>

      <Card title={t('auditExplorer.filtersTitle')}>
        <div className="flex flex-wrap items-end gap-2">
          <Field
            label={t('auditExplorer.fields.aggregateType')}
            error={(refusedField ?? rejectedField) === 'aggregateType' ? error?.message : undefined}
          >
            {(p) => <Input {...p} {...field('aggregateType')} />}
          </Field>
          <Field
            label={t('auditExplorer.fields.aggregateId')}
            error={(refusedField ?? rejectedField) === 'aggregateId' ? error?.message : undefined}
          >
            {(p) => <Input {...p} {...field('aggregateId')} />}
          </Field>
          <Field
            label={t('auditExplorer.fields.actorUserId')}
            error={(refusedField ?? rejectedField) === 'actorUserId' ? error?.message : undefined}
          >
            {(p) => <Input {...p} {...field('actorUserId')} />}
          </Field>
          <Field label={t('auditExplorer.fields.action')}>
            {(p) => <Input {...p} {...field('action')} />}
          </Field>
          <Field
            label={t('auditExplorer.fields.from')}
            error={(refusedField ?? rejectedField) === 'from' ? error?.message : undefined}
          >
            {(p) => <Input {...p} type="date" {...field('from')} />}
          </Field>
          <Field
            label={t('auditExplorer.fields.to')}
            error={(refusedField ?? rejectedField) === 'to' ? error?.message : undefined}
          >
            {(p) => <Input {...p} type="date" {...field('to')} />}
          </Field>
          <Button size="sm" onClick={search}>{t('auditExplorer.search')}</Button>
          <Button size="sm" variant="ghost" onClick={() => { setDraft({}); setApplied({}); setRejectedField(null) }}>
            {t('auditExplorer.clear')}
          </Button>
          <Button size="sm" variant="secondary" onClick={() => void exportCsv()}>{t('auditExplorer.export')}</Button>
        </div>
        {/* Which filters the SERVER says it applied, echoed from meta.filtersApplied - so "no rows"
            can be told apart from "the filter you thought you set was not one of them". */}
        {query.data?.pages[0]?.meta?.filtersApplied?.length ? (
          <p className="mt-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('auditExplorer.filtersApplied', { filters: query.data.pages[0].meta.filtersApplied.join(', ') })}
          </p>
        ) : null}
      </Card>

      {query.isLoading ? (
        <SkeletonTable label={t('common.loading')} />
      ) : error && !refusedField ? (
        <Card title={t('auditExplorer.title')}>
          <p>{t('auditExplorer.errors.loadFailed')}</p>
          <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('auditExplorer.retry')}</Button>
        </Card>
      ) : (
        <Card title={t('auditExplorer.resultsTitle')}>
          {rows.length === 0 ? (
            <p style={{ color: 'var(--color-text-secondary)' }}>
              {Object.values(applied).some(Boolean) ? t('auditExplorer.emptyFiltered') : t('auditExplorer.empty')}
            </p>
          ) : (
            <>
              <Table caption={t('auditExplorer.resultsTitle')}>
                <TableHead>
                  <TableHeaderCell>{t('auditExplorer.fields.occurredAt')}</TableHeaderCell>
                  <TableHeaderCell>{t('auditExplorer.fields.action')}</TableHeaderCell>
                  <TableHeaderCell>{t('auditExplorer.fields.aggregate')}</TableHeaderCell>
                  <TableHeaderCell>{t('auditExplorer.fields.transition')}</TableHeaderCell>
                  <TableHeaderCell>{t('auditExplorer.fields.actor')}</TableHeaderCell>
                </TableHead>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.id}>
                      <TableCell>{formatDateTime(row.occurredAt, locale)}</TableCell>
                      <TableCell><code>{row.action}</code></TableCell>
                      <TableCell>{row.aggregateType} · <code>{row.aggregateId}</code></TableCell>
                      {/* An em dash for a row that is not a transition - most audit actions are not,
                          and a blank cell reads as data the trail failed to record. */}
                      <TableCell>{row.fromState || row.toState ? `${row.fromState ?? '—'} → ${row.toState ?? '—'}` : '—'}</TableCell>
                      {/* A system actor has no label. Said in words rather than left empty, because
                          "who did this" is the first question asked of an audit row. */}
                      <TableCell>{row.actorLabel ?? t('auditExplorer.systemActor')}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              {query.hasNextPage ? (
                <Button
                  size="sm"
                  variant="secondary"
                  isLoading={query.isFetchingNextPage}
                  onClick={() => void query.fetchNextPage()}
                >
                  {t('auditExplorer.loadMore')}
                </Button>
              ) : null}
            </>
          )}
        </Card>
      )}
    </div>
  )
}
