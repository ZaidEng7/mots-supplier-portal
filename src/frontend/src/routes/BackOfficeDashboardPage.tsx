// The back office's landing page.
//
// A holder of admin.users.manage lands on the administrator's dashboard, the permission its endpoint is gated on, so
// the page an administrator sees first is the one that says whether the platform is working. Everyone else sees the
// greeting and the permissions their session carries, as before.
//
// The permission strip was a hand-rolled surface standing in for the component that owns surfaces. It had the radius and
// the border and not the shadow, which is how a copy drifts.

import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../lib/authStore'
import {Badge, Card, PageHeading} from '../components/ui'
import { AdminDashboardPage } from './admin/AdminDashboardPage'

export function BackOfficeDashboardPage() {
  const claims = useAuthStore((s) => s.claims)

  if (claims?.permissions.includes('admin.users.manage')) return <AdminDashboardPage />

  return <Greeting />
}

function Greeting() {
  const { t } = useTranslation()
  const claims = useAuthStore((s) => s.claims)

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('dashboard.welcome', { email: claims?.email ?? '' })} />
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
