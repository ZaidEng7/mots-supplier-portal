import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Button, Card, Dialog, Field, Input, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import { nextPageParam } from '../../api/listEnvelope'
import { listRfqs, createRfq, RfqApiError } from '../../api/rfqs'

/** FEAT-07.1/FR-RFQ-001. */
export function RfqListPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [titleAr, setTitleAr] = useState('')
  const [titleEn, setTitleEn] = useState('')
  const [currencyCode, setCurrencyCode] = useState('SYP')
  const [opensAt, setOpensAt] = useState('')
  const [closesAt, setClosesAt] = useState('')

  // See SupplierRfqListPage for the reasoning: §6.1 makes RFQs cursor-default, so stopping at
  // page one would hide every RFQ past the 20th with no visible symptom.
  const rfqsQuery = useInfiniteQuery({
    queryKey: ['rfqs'],
    queryFn: ({ pageParam }) => listRfqs(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: nextPageParam,
  })
  const rfqs = rfqsQuery.data?.pages.flatMap((page) => page.data) ?? []

  const createMutation = useMutation({
    mutationFn: () => createRfq({
      titleAr, titleEn, descriptionAr: null, descriptionEn: null, currencyCode,
      publishAt: null,
      submissionOpensAt: opensAt ? new Date(opensAt).toISOString() : null,
      submissionClosesAt: closesAt ? new Date(closesAt).toISOString() : null,
      clarificationDeadlineAt: null, evaluationTargetDate: null,
    }),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['rfqs'] })
      notify({ kind: 'success', title: t('rfq.created') })
      setCreateOpen(false)
      setTitleAr(''); setTitleEn(''); setOpensAt(''); setClosesAt('')
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof RfqApiError ? err.message : t('rfq.errors.saveFailed') }),
  })

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('rfq.title')}
          </h1>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.subtitle')}
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>{t('rfq.add')}</Button>
      </div>

      <Card title={t('rfq.listTitle')}>
        {rfqs.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.empty')}</p>
        ) : (
          <Table caption={t('rfq.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.reference')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.state')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfqs.map((rfq) => (
                <TableRow key={rfq.referenceCode}>
                  <TableCell>
                    <Link to="/back-office/rfqs/$referenceCode" params={{ referenceCode: rfq.referenceCode }} style={{ color: 'var(--color-text-brand)' }}>
                      {rfq.referenceCode}
                    </Link>
                  </TableCell>
                  <TableCell>{rfq.titleEn}</TableCell>
                  <TableCell><StatusChip machine="rfq" value={rfq.state} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {rfqsQuery.hasNextPage ? (
          <Button
            variant="secondary"
            isLoading={rfqsQuery.isFetchingNextPage}
            onClick={() => rfqsQuery.fetchNextPage()}
          >
            {t('rfq.loadMore')}
          </Button>
        ) : null}
      </Card>

      <Dialog open={createOpen} onOpenChange={setCreateOpen} title={t('rfq.createTitle')}>
        <form className="flex flex-col gap-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate() }} noValidate>
          <Field label={t('rfq.fields.titleAr')} required>{(p) => <Input {...p} value={titleAr} onChange={(e) => setTitleAr(e.target.value)} />}</Field>
          <Field label={t('rfq.fields.titleEn')} required>{(p) => <Input {...p} value={titleEn} onChange={(e) => setTitleEn(e.target.value)} />}</Field>
          <Field label={t('rfq.fields.currency')} required>{(p) => <Input {...p} value={currencyCode} onChange={(e) => setCurrencyCode(e.target.value)} />}</Field>
          <Field label={t('rfq.fields.submissionOpensAt')}>{(p) => <Input {...p} type="datetime-local" value={opensAt} onChange={(e) => setOpensAt(e.target.value)} />}</Field>
          <Field label={t('rfq.fields.submissionClosesAt')}>{(p) => <Input {...p} type="datetime-local" value={closesAt} onChange={(e) => setClosesAt(e.target.value)} />}</Field>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => setCreateOpen(false)}>{t('rfq.cancel')}</Button>
            <Button type="submit" isLoading={createMutation.isPending}>{t('rfq.save')}</Button>
          </div>
        </form>
      </Dialog>
    </div>
  )
}
