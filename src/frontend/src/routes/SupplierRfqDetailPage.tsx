import { formatNumber } from '../lib/datetime'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDateTime } from '../lib/datetime'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Input, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../components/ui'
import { invalidateQuietly } from '../lib/queryClient'
import { getInvitedRfq, declineInvitation, postClarification, SupplierRfqApiError } from '../api/supplierRfqs'
import { getRfqAttachmentDownloadUrl } from '../api/rfqs'

/** FEAT-08.4/08.6/FR-INV-004/006: the supplier's own view of an invited RFQ. A non-invited
 * supplier never reaches a rendered page here - getInvitedRfq 404s server-side and the query's
 * error state is shown instead (no client-side visibility decision is ever made). */
export function SupplierRfqDetailPage() {
  const { referenceCode } = useParams({ strict: false }) as { referenceCode: string }
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [declineReason, setDeclineReason] = useState('')
  const [question, setQuestion] = useState('')

  const rfqQuery = useQuery({ queryKey: ['supplier-rfq', referenceCode], queryFn: () => getInvitedRfq(referenceCode) })

  const declineMutation = useMutation({
    mutationFn: () => declineInvitation(referenceCode, declineReason || null),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['supplier-rfq', referenceCode] })
      notify({ kind: 'success', title: t('supplierRfq.declined') })
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof SupplierRfqApiError ? err.message : t('supplierRfq.errors.declineFailed') }),
  })

  const downloadMutation = useMutation({
    mutationFn: (attachmentId: string) => getRfqAttachmentDownloadUrl(referenceCode, attachmentId),
    onSuccess: (url) => window.open(url, '_blank', 'noopener,noreferrer'),
    onError: () => notify({ kind: 'danger', title: t('supplierRfq.attachments.downloadFailed') }),
  })

  const askMutation = useMutation({
    mutationFn: () => postClarification(referenceCode, question),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['supplier-rfq', referenceCode] })
      notify({ kind: 'success', title: t('supplierRfq.clarifications.asked') })
      setQuestion('')
    },
    onError: (err) => notify({ kind: 'danger', title: err instanceof SupplierRfqApiError ? err.message : t('supplierRfq.clarifications.errors.askFailed') }),
  })

  if (rfqQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }
  if (rfqQuery.isError || !rfqQuery.data) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.notFound')}</p>
  }

  const rfq = rfqQuery.data
  const canDecline = rfq.invitationStatus !== 'Declined' && rfq.invitationStatus !== 'Submitted'
  const canAsk = rfq.state === 'Published' || rfq.state === 'SubmissionOpen'

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {rfq.rfqCode} — {isArabic ? rfq.titleAr : rfq.titleEn}
          </h1>
          <StatusChip machine="invitation" value={rfq.invitationStatus} />
        </div>
        {rfq.invitationStatus !== 'Declined' ? (
          <Link to="/rfqs/$referenceCode/proposal" params={{ referenceCode }}
            className="rounded-md px-3 py-1.5 text-[length:var(--text-body-sm)] font-[var(--fw-medium)]"
            style={{ backgroundColor: 'var(--color-brand-solid)', color: 'var(--color-text-inverse)' }}>
            {t('proposal.goToMyProposal')}
          </Link>
        ) : null}
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
                  <TableCell>{formatNumber(item.quantity, locale, 0)}</TableCell>
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

      {/*
        SCR-142. The payload has carried `attachments` since EPIC-08 and nothing rendered them, so an
        invited supplier could read the RFQ and never reach the tender documents it depends on - the
        one thing they need before pricing anything. Found by the per-screen sweep (batch 9 phase 12a).

        Download goes through the same `download-url` exchange the buyer uses: the URL is short-lived
        and issued per request (D-16), so it is fetched on the click rather than rendered into the page.
      */}
      {/* A-6: the supplier is told WHY their deadline moved, on the screen where the deadline is. */}
      {rfq.submissionDeadlineChangeReason ? (
        <Card title={t('supplierRfq.deadlineChanged.title')}>
          <p>{rfq.submissionDeadlineChangeReason}</p>
          {rfq.submissionDeadlineChangedAt ? (
            <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
              {formatDateTime(rfq.submissionDeadlineChangedAt, locale)}
            </p>
          ) : null}
        </Card>
      ) : null}

      <Card title={t('supplierRfq.attachments.title')}>
        {rfq.attachments.length > 0 ? (
          <ul className="flex flex-col gap-2">
            {rfq.attachments.map((attachment) => (
              <li key={attachment.id} className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex min-w-0 flex-col">
                  <span className="truncate">{attachment.originalFileName}</span>
                  {attachment.caption ? (
                    <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                      {attachment.caption}
                    </span>
                  ) : null}
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  isLoading={downloadMutation.isPending && downloadMutation.variables === attachment.id}
                  onClick={() => downloadMutation.mutate(attachment.id)}
                >
                  {t('supplierRfq.attachments.download')}
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.attachments.none')}</p>
        )}
      </Card>

      <Card title={t('supplierRfq.clarifications.title')}>
        {rfq.clarifications.length > 0 ? (
          <ul className="flex flex-col gap-3">
            {rfq.clarifications.map((c) => (
              <li key={c.id} className="border-b pb-3 last:border-b-0" style={{ borderColor: 'var(--color-border)' }}>
                <div className="flex items-center justify-between gap-2">
                  <p className="font-[var(--fw-medium)]">{c.question}</p>
                  {c.isMine ? <Badge tone="info">{t('supplierRfq.clarifications.mine')}</Badge> : null}
                </div>
                <p style={{ color: 'var(--color-text-secondary)' }}>
                  {c.answer ?? t('supplierRfq.clarifications.awaitingAnswer')}
                </p>
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.clarifications.none')}</p>
        )}
        {canAsk ? (
          <form className="mt-4 flex gap-2" onSubmit={(e) => { e.preventDefault(); askMutation.mutate() }}>
            <Input aria-label={t('supplierRfq.clarifications.askPlaceholder')} placeholder={t('supplierRfq.clarifications.askPlaceholder')}
              value={question} onChange={(e) => setQuestion(e.target.value)} />
            <Button type="submit" size="sm" isLoading={askMutation.isPending} disabled={!question}>
              {t('supplierRfq.clarifications.ask')}
            </Button>
          </form>
        ) : null}
      </Card>

      {rfq.addenda.length > 0 ? (
        <Card title={t('rfq.addenda.title')}>
          <ul className="flex flex-col gap-2">
            {rfq.addenda.map((a) => (
              <li key={a.id}>
                <p className="font-[var(--fw-medium)]">{isArabic ? a.titleAr : a.titleEn}</p>
                <p style={{ color: 'var(--color-text-secondary)' }}>{isArabic ? a.descriptionAr : a.descriptionEn}</p>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

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
