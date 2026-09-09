import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import {Badge, PageHeading} from '../components/ui'

export function BackOfficeDashboardPage() {
  const { t } = useTranslation()
  const claims = useAuthStore((s) => s.claims)

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('dashboard.welcome', { email: claims?.email ?? '' })} />
      <div
        className="flex flex-wrap gap-4 rounded-[var(--radius-lg)] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <div>
          <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('dashboard.permission')}
          </p>
          <div className="flex flex-wrap gap-1">
            {claims && claims.permissions.length > 0 ? (
              claims.permissions.map((p) => (
                <Badge key={p} tone="brand">
                  {p}
                </Badge>
              ))
            ) : (
              <Badge tone="neutral">—</Badge>
            )}
          </div>
        </div>
      </div>
      <p style={{ color: 'var(--color-text-secondary)' }}>{t('dashboard.placeholder')}</p>
    </div>
  )
}
