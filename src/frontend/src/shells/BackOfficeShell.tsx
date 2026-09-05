import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { LanguageSwitch } from '../components/LanguageSwitch'
import { NotificationBell } from '../components/NotificationBell'
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
  // T-080: same hide-never-gate rule - every /api/v1/admin/reference route re-enforces
  // reference.manage. Its own permission rather than admin.users.manage, because the two are
  // separately grantable and a role that edits code lists need not administer accounts.
  const canManageReferenceData = useAuthStore((s) => s.claims?.permissions.includes('reference.manage') ?? false)
  // T-079/SCR-720: same hide-never-gate rule - every /api/v1/audit route re-enforces audit.read.
  const canReadAudit = useAuthStore((s) => s.claims?.permissions.includes('audit.read') ?? false)
  // governance.read is the ONLY permission ministry_viewer holds, so without this link the persona
  // had to type the URL: every other link in this bar 403s for it.
  const canViewGovernance = useAuthStore((s) => s.claims?.permissions.includes('governance.read') ?? false)
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
      {/*
        T-040: this header measured 424px against a 320px viewport in English and 377px in Arabic,
        and it is shared chrome - so every back-office route overflowed the document sideways, which
        ACCESSIBILITY.md's reflow clause forbids.

        The cause was a non-wrapping flex row holding up to eight nav links plus the bell, the
        language switch and logout. The fix is wrapping, not hiding: `flex-wrap` changes nothing at
        any width where the row already fits, so the desktop layout is byte-identical and only the
        narrow case behaves differently. A collapsed hamburger would have been a new component and a
        new interaction to test on every back-office screen.

        Horizontal padding drops to 1rem below `sm` for the same reason the supplier shell's does -
        at 320px, 48px of chrome padding is 15% of the viewport.
      */}
      <header className="flex flex-wrap items-center justify-between gap-y-3 border-b px-4 py-4 sm:px-6" style={{ borderColor: 'var(--n-700)', backgroundColor: 'var(--n-800)' }}>
        <div className="flex min-w-0 flex-wrap items-center gap-x-6 gap-y-2">
          <span className="text-lg font-semibold" style={{ color: 'var(--accent-gold-500)' }}>
            {t('appName')} · {t('nav.backOffice')}
          </span>
          <nav className="flex flex-wrap gap-x-4 gap-y-2">
            {canViewGovernance ? (
              <Link to="/back-office/ministry" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('ministry.title')}
              </Link>
            ) : null}
            {canManageStaff ? (
              <Link to="/back-office/notification-templates" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('notificationTemplates.title')}
              </Link>
            ) : null}
            {canManageReferenceData ? (
              <Link to="/back-office/reference" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('referenceAdmin.title')}
              </Link>
            ) : null}
            {canReadAudit ? (
              <Link to="/back-office/audit" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('auditExplorer.title')}
              </Link>
            ) : null}
            {canManageStaff ? (
              <Link to="/back-office/settings" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('systemSettings.title')}
              </Link>
            ) : null}
            {canManageStaff ? (
              <Link to="/back-office/admin" className="text-[length:var(--text-body-sm)]" style={{ color: '#F4F1EC' }}>
                {t('adminOverview.title')}
              </Link>
            ) : null}
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
          <NotificationBell to="/back-office/notifications" />
          <LanguageSwitch />
          <Button variant="ghost" size="sm" style={{ color: '#F4F1EC' }} onClick={handleLogout}>
            {t('nav.logout')}
          </Button>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-4 py-8 sm:px-6" style={{ backgroundColor: 'var(--color-bg-app)' }}>
        {children}
      </main>
    </div>
  )
}
