import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../components/ui'
import { invalidateQuietly } from '../lib/queryClient'
import { getInvitedRfq, declineInvitation, SupplierRfqApiError } from '../api/supplierRfqs'

/** FEAT-08.4/08.6/FR-INV-004/006: the supplier's own view of an invited RFQ. A non-invited
 * supplier never reaches a rendered page here - getInvitedRfq 404s server-side and the query's
 * error state is shown instead (no client-side visibility decision is ever made). */
export function SupplierRfqDetailPage() {
  const { referenceCode } = useParams({ strict: false }) as { referenceCode: string }
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [declineReason, setDeclineReason] = useState('')

  const rfqQuery = useQuery({ queryKey: ['supplier-rfq', referenceCode], queryFn: () => getInvitedRfq(referenceCode) })

  const declineMutation = useMutation({
    mutationFn: () => declineInvitation(referenceCode, declineReason || null),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['supplier-rfq', referenceCode] })
      notify({ kind: 'success', title: t('supplierRfq.declined') })
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof SupplierRfqApiError ? err.message : t('supplierRfq.errors.declineFailed') }),
  })

  if (rfqQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }
  if (rfqQuery.isError || !rfqQuery.data) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.notFound')}</p>
  }

  const rfq = rfqQuery.data
  const canDecline = rfq.myInvitationStatus !== 'Declined' && rfq.myInvitationStatus !== 'Submitted'

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {rfq.referenceCode} — {isArabic ? rfq.titleAr : rfq.titleEn}
          </h1>
          <Badge tone={rfq.myInvitationStatus === 'Declined' ? 'danger' : 'info'}>{rfq.myInvitationStatus}</Badge>
        </div>
      </div>

      <Card title={t('rfq.fields.items')}>
        {rfq.items.length > 0 ? (
          <Table caption={t('rfq.fields.items')}>
            <TableHead>
              <TableHeaderCell>#</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.quantity')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{item.lineNo}</TableCell>
                  <TableCell>{isArabic ? item.titleAr : item.titleEn}</TableCell>
                  <TableCell>{item.quantity}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noItems')}</p>
        )}
      </Card>

      <Card title={t('rfq.fields.requirements')}>
        {rfq.requirements.length > 0 ? (
          <Table caption={t('rfq.fields.requirements')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.text')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.mandatory')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.requirements.map((req) => (
                <TableRow key={req.id}>
                  <TableCell>{isArabic ? req.textAr : req.textEn}</TableCell>
                  <TableCell>{req.isMandatory ? t('rfq.yes') : t('rfq.no')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noRequirements')}</p>
        )}
      </Card>

      {canDecline ? (
        <Card title={t('supplierRfq.declineTitle')}>
          <div className="flex gap-2">
            <Input aria-label={t('rfq.fields.reason')} placeholder={t('supplierRfq.declineReasonPlaceholder')}
              value={declineReason} onChange={(e) => setDeclineReason(e.target.value)} />
            <Button variant="ghost" isLoading={declineMutation.isPending} onClick={() => declineMutation.mutate()}>
              {t('supplierRfq.decline')}
            </Button>
          </div>
        </Card>
      ) : null}
    </div>
  )
}
