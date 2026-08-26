import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import { Badge } from '../components/ui'

export function SupplierDashboardPage() {
  const { t } = useTranslation()
  const claims = useAuthStore((s) => s.claims)

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('dashboard.welcome', { email: claims?.email ?? '' })}
        </h1>
      </div>
      <div
        className="flex flex-wrap gap-4 rounded-[0.75rem] p-6"
        style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
      >
        <div>
          <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
            {t('dashboard.supplierId')}
          </p>
          <p className="num text-[length:var(--text-body)]" style={{ color: 'var(--color-text-primary)' }}>
            {claims?.supplierId ?? '—'}
          </p>
        </div>
        <div>
          <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-muted)' }}>
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
