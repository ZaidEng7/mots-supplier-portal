import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Card, SkeletonList, useToast } from '../../components/ui'
import { listRoles, updateRolePermissions, type Role, type RolesResponse } from '../../api/roles'
import { SupplierApiError } from '../../api/supplier'

/** Local, not i18next: permission strings ("supplier.edit") collide with i18next's default
 * key-separator ("."), and depth varies per permission (2-3 segments), so a flat map here is
 * simpler and safer than fighting nested translation keys for a fixed, small catalog. */
const PERMISSION_LABELS: Record<string, { ar: string; en: string }> = {
  'supplier.edit': { ar: 'تعديل ملف المورد', en: 'Edit supplier profile' },
  'supplier.submit': { ar: 'تقديم طلب المورد', en: 'Submit supplier application' },
  'supplier.approve': { ar: 'اعتماد طلب المورد', en: 'Approve supplier application' },
  'supplier.review': { ar: 'مراجعة طلب المورد', en: 'Review supplier application' },
  'supplier.reject': { ar: 'رفض طلب المورد', en: 'Reject supplier application' },
  'supplier.requestInfo': { ar: 'طلب معلومات إضافية', en: 'Request more information' },
  'supplier.document.review': { ar: 'مراجعة المستندات', en: 'Review documents' },
  'supplier.bankAccount.manage': { ar: 'إدارة الحسابات المصرفية', en: 'Manage bank accounts' },
  'supplier.user.manage': { ar: 'إدارة مستخدمي المورد', en: 'Manage supplier users' },
  'supplier.lifecycle.manage': { ar: 'إدارة دورة حياة المورد', en: 'Manage supplier lifecycle' },
  'rfq.publish': { ar: 'نشر طلب عرض السعر', en: 'Publish RFQ' },
  'proposal.submit': { ar: 'تقديم عرض', en: 'Submit proposal' },
  'evaluation.score': { ar: 'تقييم العروض', en: 'Score evaluations' },
  'award.approve': { ar: 'اعتماد الترسية', en: 'Approve award' },
  'admin.users.manage': { ar: 'إدارة المستخدمين', en: 'Manage users' },
  'audit.read': { ar: 'قراءة سجل التدقيق', en: 'Read audit log' },
  'admin.organizations.manage': { ar: 'إدارة الجهات', en: 'Manage organizations' },
  'admin.roles.manage': { ar: 'إدارة الأدوار والصلاحيات', en: 'Manage roles & permissions' },
  'offering.search': { ar: 'البحث عن الخدمات المعروضة', en: 'Search offerings' },
}

export function RolesPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const rolesQuery = useQuery({ queryKey: ['roles'], queryFn: listRoles })
  const roles = rolesQuery.data?.roles ?? []
  // Bug fix: this used to derive from roles.flatMap(r => r.permissions) - the union of what's
  // ALREADY assigned - so a permission added to the catalog but not yet granted to any role was
  // invisible here and could only ever be granted via a direct DB write. allPermissions is now
  // the backend's full Permissions.All catalog (see RolesResponse's doc comment), independent of
  // what any role currently holds.
  const allPermissions = [...(rolesQuery.data?.allPermissions ?? [])].sort((a, b) => a.localeCompare(b))

  const updateMutation = useMutation({
    mutationFn: ({ roleName, permissions }: { roleName: string; permissions: string[] }) => updateRolePermissions(roleName, permissions),
    onSuccess: (updated) => {
      queryClient.setQueryData<RolesResponse>(['roles'], (prev) =>
        prev ? { ...prev, roles: prev.roles.map((r) => (r.name === updated.name ? updated : r)) } : prev,
      )
    },
    onError: (err) => {
      const message =
        err instanceof SupplierApiError && err.message === 'would_lock_out_role_management'
          ? t('roleManagement.errors.wouldLockOutRoleManagement')
          : err instanceof SupplierApiError && err.message === 'invalid_permission'
            ? t('roleManagement.errors.invalidPermission')
            : t('roleManagement.errors.updateFailed')
      notify({ kind: 'danger', title: message })
    },
  })

  const toggle = (role: Role, permission: string) => {
    const has = role.permissions.includes(permission)
    const next = has ? role.permissions.filter((p) => p !== permission) : [...role.permissions, permission]
    updateMutation.mutate({ roleName: role.name, permissions: next })
  }

  if (rolesQuery.isLoading) {
    return <SkeletonList label={t('common.loading')} />
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
          {t('roleManagement.title')}
        </h1>
        <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
          {t('roleManagement.subtitle')}
        </p>
      </div>

      {roles.map((role) => (
        <Card key={role.name} title={t(`staff.roles.${role.name}`, { defaultValue: role.name })}>
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {allPermissions.map((permission) => {
              const checked = role.permissions.includes(permission)
              const label = PERMISSION_LABELS[permission]
              return (
                <li key={permission}>
                  <label
                    className="flex cursor-pointer items-center gap-3 rounded-[0.5rem] p-3 text-[length:var(--text-body-sm)]"
                    style={{ border: `1px solid ${checked ? 'var(--color-brand-solid)' : 'var(--color-border)'}`, backgroundColor: checked ? 'var(--color-brand-subtle)' : 'transparent' }}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={updateMutation.isPending}
                      onChange={() => toggle(role, permission)}
                    />
                    <span style={{ color: 'var(--color-text-primary)' }}>{label ? (isArabic ? label.ar : label.en) : permission}</span>
                  </label>
                </li>
              )
            })}
          </ul>
        </Card>
      ))}
    </div>
  )
}
