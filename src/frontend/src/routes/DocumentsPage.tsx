import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Card, Field, Input, SkeletonList, StatusChip,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast,
} from '../components/ui'
import { formatDate, formatDateTime } from '../lib/datetime'
import { getOwnSupplier } from '../api/supplier'
import {
  listOwnDocuments, uploadDocument, getDocumentDownloadUrl, getDocumentHistory,
  DocumentApiError, type DocumentTypeStatus,
} from '../api/documents'

/** A document type is "needing attention" when its latest version is missing, rejected, expiring or
 * expired. Derived from the state the daily job already maintains rather than recomputing expiry —
 * two places deciding "is this expiring" would eventually disagree, and this would be the one nobody
 * checks. */
function needsAttention(row: DocumentTypeStatus): boolean {
  const state = row.latestDocument?.state
  if (!row.latestDocument) return row.isRequired
  return state === 'Rejected' || state === 'Expired' || state === 'ExpiringSoon' || state === 'ScanRejected'
}

/**
 * SCR-130 documents centre (**P0**), SCR-131 upload/replace, SCR-132 detail and history, SCR-133
 * expiring alerts — `/documents`, `supplier_admin` and `supplier_user`.
 *
 * <p><b>The gap.</b> Documents were reachable only through the onboarding wizard's upload step. An
 * approved supplier with an expiring certificate had no page that said so, and no way to see why a
 * document had been rejected two versions ago.</p>
 *
 * <p>SCR-133 is a FILTER on this page rather than a second route: "needs attention" is a view of the
 * same list, and a separate screen would be a second place for the definition of "expiring" to
 * live.</p>
 */
export function DocumentsPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [attentionOnly, setAttentionOnly] = useState(false)
  const [expanded, setExpanded] = useState<string | null>(null)
  const [expiry, setExpiry] = useState<Record<string, string>>({})

  const profile = useQuery({ queryKey: ['supplier-profile'], queryFn: getOwnSupplier })
  const supplierCode = profile.data?.supplierCode

  const documents = useQuery({
    queryKey: ['own-documents', supplierCode],
    queryFn: () => listOwnDocuments(supplierCode!),
    enabled: !!supplierCode,
  })

  const history = useQuery({
    queryKey: ['document-history', supplierCode, expanded],
    queryFn: () => getDocumentHistory(supplierCode!, expanded!),
    enabled: !!supplierCode && expanded !== null,
  })

  const uploadMutation = useMutation({
    mutationFn: ({ typeId, file, expiryDate }: { typeId: string; file: File; expiryDate?: string }) =>
      uploadDocument(supplierCode!, typeId, file, undefined, expiryDate),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['own-documents'] })
      void queryClient.invalidateQueries({ queryKey: ['document-history'] })
      notify({ kind: 'success', title: t('documents.uploaded') })
    },
    onError: (raised) => notify({
      kind: 'danger',
      title: raised instanceof DocumentApiError ? raised.message : t('documents.errors.uploadFailed'),
    }),
  })

  const download = async (documentId: string) => {
    try {
      // Fetched, not linked: the URL needs the Authorization header, so a plain anchor would arrive
      // unauthenticated. Same treatment the audit export already gets.
      const url = await getDocumentDownloadUrl(documentId)
      window.open(url, '_blank', 'noopener')
    } catch {
      notify({ kind: 'danger', title: t('documents.errors.downloadFailed') })
    }
  }

  if (profile.isLoading || documents.isLoading) return <SkeletonList label={t('common.loading')} />
  if (documents.isError || !documents.data) {
    return (
      <Card title={t('documents.title')}>
        <p>{t('documents.errors.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void documents.refetch()}>{t('documents.retry')}</Button>
      </Card>
    )
  }

  const all = documents.data
  const attention = all.filter(needsAttention)
  const rows = attentionOnly ? attention : all

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('documents.title')}
        </h1>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('documents.subtitle')}</p>
      </div>

      {/* SCR-133. Shown only when there IS something to attend to - a permanent "0 need attention"
          banner is chrome nobody reads. */}
      {attention.length > 0 ? (
        <Card title={t('documents.attentionTitle')}>
          <p className="mb-2" style={{ color: 'var(--color-text-secondary)' }}>
            {t('documents.attentionBody', { count: attention.length })}
          </p>
          <Button
            size="sm"
            variant={attentionOnly ? 'primary' : 'secondary'}
            aria-pressed={attentionOnly}
            onClick={() => setAttentionOnly((v) => !v)}
          >
            {attentionOnly ? t('documents.showAll') : t('documents.showAttention')}
          </Button>
        </Card>
      ) : null}

      <Card title={t('documents.listTitle')}>
        <Table caption={t('documents.listTitle')}>
          <TableHead>
            <TableHeaderCell>{t('documents.fields.type')}</TableHeaderCell>
            <TableHeaderCell>{t('documents.fields.required')}</TableHeaderCell>
            <TableHeaderCell>{t('documents.fields.state')}</TableHeaderCell>
            <TableHeaderCell>{t('documents.fields.expiry')}</TableHeaderCell>
            <TableHeaderCell>{t('documents.fields.actions')}</TableHeaderCell>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.documentTypeId}>
                <TableCell>{isArabic ? row.nameAr : row.nameEn}</TableCell>
                <TableCell>
                  {row.isRequired ? <Badge tone="warning">{t('documents.required')}</Badge> : <Badge tone="neutral">{t('documents.optional')}</Badge>}
                </TableCell>
                <TableCell>
                  {row.latestDocument
                    ? <StatusChip machine="document" value={row.latestDocument.state} />
                    : <Badge tone="neutral">{t('documents.notUploaded')}</Badge>}
                </TableCell>
                <TableCell>
                  {row.latestDocument?.expiryDate ? formatDate(row.latestDocument.expiryDate, locale) : '—'}
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-end gap-2">
                    {row.expiryTracked ? (
                      <Field label={t('documents.fields.expiry')}>
                        {(p) => (
                          <Input
                            {...p}
                            type="date"
                            value={expiry[row.documentTypeId] ?? ''}
                            onChange={(e) => setExpiry((prev) => ({ ...prev, [row.documentTypeId]: e.target.value }))}
                          />
                        )}
                      </Field>
                    ) : null}
                    <label className="inline-flex">
                      <span className="sr-only">
                        {row.latestDocument ? t('documents.replace') : t('documents.upload')} — {isArabic ? row.nameAr : row.nameEn}
                      </span>
                      <input
                        type="file"
                        aria-label={`${row.latestDocument ? t('documents.replace') : t('documents.upload')} — ${isArabic ? row.nameAr : row.nameEn}`}
                        onChange={(e) => {
                          const file = e.target.files?.[0]
                          if (!file) return
                          uploadMutation.mutate({
                            typeId: row.documentTypeId,
                            file,
                            expiryDate: expiry[row.documentTypeId] || undefined,
                          })
                          e.target.value = ''
                        }}
                      />
                    </label>
                    {row.latestDocument ? (
                      <Button size="sm" variant="ghost" onClick={() => void download(row.latestDocument!.documentId)}>
                        {t('documents.download')}
                      </Button>
                    ) : null}
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => setExpanded((v) => (v === row.code ? null : row.code))}
                    >
                      {expanded === row.code ? t('documents.hideHistory') : t('documents.history')}
                    </Button>
                  </div>

                  {/* SCR-132's rejection reason, on the row it belongs to. */}
                  {row.latestDocument?.rejectReason ? (
                    <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-danger-fg)' }}>
                      {row.latestDocument.rejectReason}
                    </p>
                  ) : null}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      {/* SCR-132. A panel rather than a route, for the same reason SCR-431 is: a supplier checking
          why a document was rejected is comparing it to the current one. */}
      {expanded ? (
        <Card
          title={t('documents.historyTitle')}
          action={<Button size="sm" variant="ghost" onClick={() => setExpanded(null)}>{t('documents.close')}</Button>}
        >
          {history.isLoading ? (
            <SkeletonList label={t('common.loading')} rows={3} />
          ) : history.isError || !history.data ? (
            <p>{t('documents.errors.historyFailed')}</p>
          ) : history.data.length === 0 ? (
            // A real answer, and a different one from "no such type".
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('documents.noHistory')}</p>
          ) : (
            <Table caption={t('documents.historyTitle')}>
              <TableHead>
                <TableHeaderCell>{t('documents.fields.version')}</TableHeaderCell>
                <TableHeaderCell>{t('documents.fields.state')}</TableHeaderCell>
                <TableHeaderCell>{t('documents.fields.fileName')}</TableHeaderCell>
                <TableHeaderCell>{t('documents.fields.uploadedAt')}</TableHeaderCell>
                <TableHeaderCell>{t('documents.fields.reason')}</TableHeaderCell>
              </TableHead>
              <TableBody>
                {history.data.map((v) => (
                  <TableRow key={v.documentId}>
                    <TableCell>{v.version}</TableCell>
                    <TableCell><StatusChip machine="document" value={v.state} /></TableCell>
                    <TableCell>{v.originalFileName}</TableCell>
                    <TableCell>{formatDateTime(v.uploadedAt, locale)}</TableCell>
                    <TableCell>{v.rejectReason ?? '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </Card>
      ) : null}
    </div>
  )
}
