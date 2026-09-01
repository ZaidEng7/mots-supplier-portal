import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Badge, Card, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../components/ui'
import { listInvitedRfqs } from '../api/supplierRfqs'

/** FEAT-08.6/FR-INV-006: only RFQs this supplier holds a real Invitation to are ever returned -
 * the backend list endpoint is itself invitation-scoped, not filtered client-side. */
export function SupplierRfqListPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  const rfqsQuery = useQuery({ queryKey: ['supplier-rfqs'], queryFn: listInvitedRfqs })
  const rfqs = rfqsQuery.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('supplierRfq.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('supplierRfq.subtitle')}
        </p>
      </div>

      <Card title={t('supplierRfq.listTitle')}>
        {rfqs.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('supplierRfq.empty')}</p>
        ) : (
          <Table caption={t('supplierRfq.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.reference')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('supplierRfq.myStatus')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfqs.map((rfq) => (
                <TableRow key={rfq.referenceCode}>
                  <TableCell>
                    <Link to="/rfqs/$referenceCode" params={{ referenceCode: rfq.referenceCode }} style={{ color: 'var(--color-text-brand)' }}>
                      {rfq.referenceCode}
                    </Link>
                  </TableCell>
                  <TableCell>{isArabic ? rfq.titleAr : rfq.titleEn}</TableCell>
                  <TableCell><Badge tone={rfq.myInvitationStatus === 'Declined' ? 'danger' : 'info'}>{rfq.myInvitationStatus}</Badge></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
    </div>
  )
}
