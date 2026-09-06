import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {
  Badge, Button, Card, SkeletonList, StatusChip,
  Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow,
} from '../components/ui'
import { getOwnSupplier } from '../api/supplier'
import { PROFILE_DISPLAY_FIELDS, profileDisplayValue, LEGAL_INFO_FIELDS, legalInfoValue } from './profileDisplayFields'

/**
 * SCR-121, `/profile`, `supplier_admin` and `supplier_user`, **P0** — and SCR-122 to SCR-126's
 * entry points.
 *
 * <p><b>The gap.</b> An approved supplier had no way to read their own profile. `GET /suppliers/me`
 * has always returned the whole thing, and the only component that rendered it was
 * `ReviewApplicationPage` — the REVIEWER's screen. A supplier could reach their own data only by
 * re-entering the onboarding wizard, which is a form, not a home.</p>
 *
 * <p><b>Read here, edit where editing already lives.</b> Each section links to the existing
 * onboarding editor rather than growing a second copy of it: two forms writing one aggregate is how
 * they drift, and the wizard's routes already carry the flagged-field rules that apply while a
 * reviewer has the profile open (MSP-77). This screen is the missing READ, not a second write.</p>
 *
 * <p>It reuses <c>PROFILE_DISPLAY_FIELDS</c> and <c>LEGAL_INFO_FIELDS</c> — the same lists and the
 * same null-safe readers the reviewer's screen uses — so the two views cannot disagree about what a
 * profile contains, and neither can crash on a DTO change that introduces an object where a scalar
 * was.</p>
 */
export function ProfilePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')

  const query = useQuery({ queryKey: ['supplier-profile'], queryFn: getOwnSupplier })

  if (query.isLoading) return <SkeletonList label={t('common.loading')} />
  if (query.isError || !query.data) {
    return (
      <Card title={t('profile.title')}>
        <p>{t('profile.errors.loadFailed')}</p>
        <Button size="sm" variant="ghost" onClick={() => void query.refetch()}>{t('profile.retry')}</Button>
      </Card>
    )
  }

  const supplier = query.data
  const name = (isArabic ? supplier.displayNameAr : supplier.displayNameEn) || supplier.displayNameEn

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {name}
          </h1>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            <code>{supplier.supplierCode}</code>
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusChip machine="onboarding" value={supplier.onboardingState} />
          {supplier.lifecycleState !== 'None' ? (
            <Badge tone={supplier.lifecycleState === 'Active' ? 'success' : 'warning'}>
              {t(`lifecycle.${supplier.lifecycleState}`, { defaultValue: supplier.lifecycleState })}
            </Badge>
          ) : null}
        </div>
      </div>

      {/* SCR-121's completeness view. The list comes from the server's own missingProfileFields, so
          the screen cannot disagree with the gate that will refuse a submission. */}
      {supplier.missingProfileFields.length > 0 ? (
        <Card title={t('profile.incompleteTitle')}>
          <p className="mb-2" style={{ color: 'var(--color-text-secondary)' }}>{t('profile.incompleteBody')}</p>
          <ul className="flex flex-wrap gap-2">
            {supplier.missingProfileFields.map((field) => (
              <li key={field}><Badge tone="warning">{t(`profile.fields.${field}`, { defaultValue: field })}</Badge></li>
            ))}
          </ul>
        </Card>
      ) : null}

      <Card
        title={t('profile.companyTitle')}
        action={<Link to="/onboarding"><Button size="sm" variant="ghost">{t('profile.edit')}</Button></Link>}
      >
        <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
          {PROFILE_DISPLAY_FIELDS.map((field) => (
            <div key={field}>
              <dt className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t(`profile.fields.${field}`, { defaultValue: field })}
              </dt>
              <dd style={{ color: 'var(--color-text-primary)' }}>{profileDisplayValue(supplier, field)}</dd>
            </div>
          ))}
        </dl>
      </Card>

      <Card
        title={t('profile.legalTitle')}
        action={<Link to="/onboarding"><Button size="sm" variant="ghost">{t('profile.edit')}</Button></Link>}
      >
        <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
          {LEGAL_INFO_FIELDS.map((field) => (
            <div key={field}>
              <dt className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t(`profile.fields.${field}`, { defaultValue: field })}
              </dt>
              <dd style={{ color: 'var(--color-text-primary)' }}>
                {supplier.legalInfo ? legalInfoValue(supplier.legalInfo, field) : '—'}
              </dd>
            </div>
          ))}
        </dl>
      </Card>

      <Card
        title={t('profile.contactsTitle')}
        action={<Link to="/onboarding/contacts"><Button size="sm" variant="ghost">{t('profile.manage')}</Button></Link>}
      >
        {supplier.representatives.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('profile.noContacts')}</p>
        ) : (
          <Table caption={t('profile.contactsTitle')}>
            <TableHead>
              <TableHeaderCell>{t('profile.fields.fullName')}</TableHeaderCell>
              <TableHeaderCell>{t('profile.fields.email')}</TableHeaderCell>
              <TableHeaderCell>{t('profile.fields.phone')}</TableHeaderCell>
              <TableHeaderCell>{t('profile.fields.primary')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {supplier.representatives.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>{r.fullName}</TableCell>
                  <TableCell>{r.email}</TableCell>
                  <TableCell>{r.phone ?? '—'}</TableCell>
                  <TableCell>{r.isPrimary ? <Badge tone="brand">{t('profile.fields.primary')}</Badge> : '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <Card
        title={t('profile.addressesTitle')}
        action={<Link to="/onboarding/addresses"><Button size="sm" variant="ghost">{t('profile.manage')}</Button></Link>}
      >
        {supplier.addresses.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('profile.noAddresses')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {supplier.addresses.map((a) => (
              <li key={a.id}>
                <Badge tone="neutral">{a.kind}</Badge>{' '}
                {[a.line1, a.line2, a.city, a.regionCode, a.country].filter(Boolean).join(', ')}
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card
        title={t('profile.bankingTitle')}
        action={<Link to="/onboarding/banking"><Button size="sm" variant="ghost">{t('profile.manage')}</Button></Link>}
      >
        {supplier.bankAccounts.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('profile.noBankAccounts')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {supplier.bankAccounts.map((b) => (
              <li key={b.id}>
                {b.bankName} · <code>{b.maskedAccountNumber}</code> · {b.currencyCode}
                {b.isDefault ? <> <Badge tone="brand">{t('profile.fields.default')}</Badge></> : null}
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card
        title={t('profile.offeringsTitle')}
        action={<Link to="/offerings"><Button size="sm" variant="ghost">{t('profile.manage')}</Button></Link>}
      >
        {supplier.categories.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('profile.noCategories')}</p>
        ) : (
          <ul className="flex flex-wrap gap-2">
            {supplier.categories.map((c) => <li key={c}><Badge tone="neutral">{c}</Badge></li>)}
          </ul>
        )}
      </Card>
    </div>
  )
}
