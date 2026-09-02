import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { LanguageSwitch } from '../components/LanguageSwitch'
import { Button } from '../components/ui'
import { useAuthStore } from '../lib/authStore'
import { logout as apiLogout } from '../api/auth'

interface Props {
  children: ReactNode
}

/** Back-office (MOTS staff) app shell: dark sidebar-style top bar, visually distinct from the
 * supplier shell so staff and suppliers are never mistaken for the same surface. */
export function BackOfficeShell({ children }: Props) {
  const { t } = useTranslation()
  const clearSession = useAuthStore((s) => s.clearSession)
  // FR-IAM-010: hide, never gate - the API re-enforces admin.organizations.manage on every
  // Organization endpoint regardless of what this link's visibility does.
  const canManageOrganizations = useAuthStore((s) => s.claims?.permissions.includes('admin.organizations.manage') ?? false)
  // Task #28: same hide-never-gate rule - StaffEndpoints re-enforces admin.users.manage
  // (Permissions.AdminUsersManage) on the actual invite endpoint regardless of this link.
  const canManageStaff = useAuthStore((s) => s.claims?.permissions.includes('admin.users.manage') ?? false)
  const canManageRoles = useAuthStore((s) => s.claims?.permissions.includes('admin.roles.manage') ?? false)
  // FEAT-06.3: same hide-never-gate rule - the /api/v1/offerings/search endpoint re-enforces
  // offering.search regardless of what this link's visibility does.
  const canSearchOfferings = useAuthStore((s) => s.claims?.permissions.includes('offering.search') ?? false)
  // EPIC-07: same hide-never-gate rule - RfqEndpoints re-enforces rfq.read/rfq.edit/etc on
  // every actual RFQ endpoint regardless of what this link's visibility does.
  //
  // Gated on rfq.read, not rfq.create: procurement_manager approves RFQs but does not author them,
  // so keying the link on the authoring permission hid the section from the one role whose job is
  // to open it. Same defect as the endpoints' own gate, on the navigation side.
  const canViewRfqs = useAuthStore((s) => s.claims?.permissions.includes('rfq.read') ?? false)
  const canManageEvaluationTemplates = useAuthStore((s) => s.claims?.permissions.includes('evaluation.template.manage') ?? false)

  const handleLogout = async () => {
    await apiLogout()
    clearSession()
    window.location.href = '/login'
  }

  return (
    <div className="flex min-h-screen flex-col" style={{ backgroundColor: 'var(--n-900)' }}>
      <header className="flex items-center justify-between border-b px-6 py-4" style={{ borderColor: 'var(--n-700)', backgroundColor: 'var(--n-800)' }}>
        <div className="flex items-center gap-6">
          <span className="text-lg font-semibold" style={{ color: 'var(--accent-gold-500)' }}>
            {t('appName')} · {t('nav.backOffice')}
          </span>
          <nav className="flex gap-4">
            <Link to="/back-office/dashboard" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
              {t('nav.dashboard')}
            </Link>
            <Link to="/back-office/review" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
              {t('review.title')}
            </Link>
            {canManageOrganizations ? (
              <Link to="/back-office/organizations" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('organizations.title')}
              </Link>
            ) : null}
            {canManageStaff ? (
              <Link to="/back-office/staff" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('staff.title')}
              </Link>
            ) : null}
            {canManageRoles ? (
              <Link to="/back-office/roles" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('roleManagement.title')}
              </Link>
            ) : null}
            {canSearchOfferings ? (
              <Link to="/back-office/offerings" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('offeringSearch.title')}
              </Link>
            ) : null}
            {canViewRfqs ? (
              <Link to="/back-office/rfqs" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('rfq.title')}
              </Link>
            ) : null}
            {canManageEvaluationTemplates ? (
              <Link to="/back-office/evaluation-templates" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('evaluationTemplates.title')}
              </Link>
            ) : null}
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          <Button variant="ghost" size="sm" style={{ color: '#F4F1EC' }} onClick={handleLogout}>
            {t('nav.logout')}
          </Button>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-6 py-8" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        {children}
      </main>
    </div>
  )
}
