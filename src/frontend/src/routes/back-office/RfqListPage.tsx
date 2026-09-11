import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import {Button, Dialog, Field, Input, ListCard, PageHeading, SegmentedControl, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast} from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import { nextPageParam } from '../../api/listEnvelope'
import { listRfqs, createRfq, RfqApiError, type RfqOwnerFilter } from '../../api/rfqs'

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
  // A-7: three views of the same list, resolved server-side. "me" as the default would hide an
  // organization's work from a manager on the screen they approve from, so the default is everything
  // and the narrowing is a deliberate click.
  const [owner, setOwner] = useState<RfqOwnerFilter | 'all'>('all')

  // See the table header below: a column of one repeated value is not information.
  const showOwner = owner === 'all'

  // See SupplierRfqListPage for the reasoning: §6.1 makes RFQs cursor-default, so stopping at
  // page one would hide every RFQ past the 20th with no visible symptom.
  const rfqsQuery = useInfiniteQuery({
    // The filter is IN the key: without it, switching views would show the previous view's cached
    // pages until the refetch landed, which on a list of tenders reads as the filter not working.
    queryKey: ['rfqs', owner],
    queryFn: ({ pageParam }) => listRfqs(pageParam, owner === 'all' ? undefined : owner),
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
          <PageHeading title={t('rfq.title')} subtitle={t('rfq.subtitle')} />
        </div>
        <Button onClick={() => setCreateOpen(true)}>{t('rfq.add')}</Button>
      </div>

      {/*
        No card title. The page heading above already says "Tenders" and this card said "Tender list" -
        two names for one list, six inches apart, which the Rams audit named as one of five removable
        things on this screen. The table takes its accessible name from the page heading instead, so
        nothing is lost to a screen reader.
      */}
      <ListCard
        query={rfqsQuery}
        isEmpty={rfqs.length === 0}
        labels={{
          loading: t('common.loading'),
          error: t('common.loadFailed'),
          empty: owner === 'all' ? t('rfq.empty') : t(`rfq.ownerFilter.empty.${owner}`),
          loadMore: t('rfq.loadMore'),
        }}
        action={
          <SegmentedControl
            legend={t('rfq.ownerFilter.label')}
            value={owner}
            onChange={setOwner}
            segments={(['all', 'me', 'unassigned'] as const).map((value) => ({
              value,
              label: t(`rfq.ownerFilter.${value}`),
            }))}
          />
        }
      >
        <Table flush caption={t('rfq.title')}>
          <TableHead>
            <TableHeaderCell>{t('rfq.fields.reference')}</TableHeaderCell>
            <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
            <TableHeaderCell>{t('rfq.fields.state')}</TableHeaderCell>
            {/*
              The owner column earns its place only when the list holds more than one owner.

              Filtered to "mine" every cell in it reads the same name, and filtered to "unassigned"
              every cell reads "Unassigned" - twenty-five identical values beside a control that had
              just been used to make them identical. Unfiltered it is the answer to "whose is this",
              which is a question a manager opens this screen with, so the column stays there.
            */}
            {showOwner ? <TableHeaderCell>{t('rfq.fields.owner')}</TableHeaderCell> : null}
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
                {/* "Unassigned" in words, not an empty cell: an unowned tender is a row somebody
                    should claim, and a blank reads as missing data rather than as an invitation. */}
                {showOwner ? <TableCell>{rfq.ownerName ?? t('rfq.unassigned')}</TableCell> : null}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </ListCard>

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
