import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import {Badge, Card, PageHeading} from '../components/ui'

export function BackOfficeDashboardPage() {
  const { t } = useTranslation()
  const claims = useAuthStore((s) => s.claims)

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('dashboard.welcome', { email: claims?.email ?? '' })} />
      {/* A hand-rolled surface standing in for the component that owns surfaces. It had the radius and
          the border and not the shadow, which is how a copy drifts. */}
      <Card>
        <div className="flex flex-wrap gap-4">
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
      </Card>
    </div>
  )
}
