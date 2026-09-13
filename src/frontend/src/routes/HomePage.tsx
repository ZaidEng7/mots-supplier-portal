// The landing page. It began as the walking-skeleton slice: a real reference-data read through every layer - UI to API
// to Application to Domain to EF Core to PostgreSQL - per ROADMAP.md Phase 0.
//
// It imported no shared component at all, having been written before there was a component layer, and it kept its own
// hand-rolled surfaces and headings long after one existed. Both sections are Cards now, and the page has a name.

import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchCurrencies, fetchHealth } from '../api/reference'
import { Card, PageHeading } from '../components/ui'

export function HomePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth })
  const currencies = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('appName')} />
      <Card title={t('health.title')}>
        {health.isLoading && <p style={{ color: 'var(--color-text-secondary)' }}>...</p>}
        {health.isSuccess && (
          <span
            className="inline-flex rounded-full px-3 py-1 text-[length:var(--text-body)] font-[var(--fw-medium)]"
            style={{ color: 'var(--color-success-fg)', backgroundColor: 'var(--color-success-bg)' }}
          >
            {t('health.healthy')}
          </span>
        )}
        {health.isError && (
          <span
            className="inline-flex rounded-full px-3 py-1 text-[length:var(--text-body)] font-[var(--fw-medium)]"
            style={{ color: 'var(--color-danger-fg)', backgroundColor: 'var(--color-danger-bg)' }}
          >
            {t('health.unhealthy')}
          </span>
        )}
      </Card>

      <Card title={t('reference.currencies')}>
        <ul className="flex flex-col gap-2">
          {currencies.data?.map((c) => (
            <li key={c.id} className="flex items-center justify-between text-[length:var(--text-body)]">
              <span style={{ color: 'var(--color-text-primary)' }}>{isArabic ? c.nameAr : c.nameEn}</span>
              <span className="num font-[var(--fw-medium)]" style={{ color: 'var(--color-text-secondary)' }}>
                {c.code}
              </span>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
