import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchCurrencies, fetchHealth } from '../api/reference'

/** Walking-skeleton slice: renders a real reference-data read through every layer
 *  (UI -> API -> Application -> Domain -> EF Core -> PostgreSQL). docs/backlog/ROADMAP.md Phase 0. */
export function HomePage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth })
  const currencies = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })

  return (
    <div className="flex flex-col gap-6">
      <section
        className="rounded-lg border p-4"
        style={{ borderColor: 'var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}
      >
        <h2 className="mb-2 text-base font-semibold" style={{ color: 'var(--color-text-primary)' }}>
          {t('health.title')}
        </h2>
        {health.isLoading && <p style={{ color: 'var(--color-text-muted)' }}>...</p>}
        {health.isSuccess && (
          <span
            className="inline-flex rounded-full px-3 py-1 text-sm font-medium"
            style={{ color: 'var(--color-success-fg)', backgroundColor: 'var(--color-success-bg)' }}
          >
            {t('health.healthy')}
          </span>
        )}
        {health.isError && (
          <span
            className="inline-flex rounded-full px-3 py-1 text-sm font-medium"
            style={{ color: 'var(--color-danger-fg)', backgroundColor: 'var(--color-danger-bg)' }}
          >
            {t('health.unhealthy')}
          </span>
        )}
      </section>

      <section
        className="rounded-lg border p-4"
        style={{ borderColor: 'var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}
      >
        <h2 className="mb-3 text-base font-semibold" style={{ color: 'var(--color-text-primary)' }}>
          {t('reference.currencies')}
        </h2>
        <ul className="flex flex-col gap-2">
          {currencies.data?.map((c) => (
            <li key={c.id} className="flex items-center justify-between text-sm">
              <span style={{ color: 'var(--color-text-primary)' }}>{isArabic ? c.nameAr : c.nameEn}</span>
              <span className="num font-medium" style={{ color: 'var(--color-text-secondary)' }}>
                {c.code}
              </span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
