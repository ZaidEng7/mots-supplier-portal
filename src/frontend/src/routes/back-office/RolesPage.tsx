// SCR-703: the roles and what each one grants.
//
// THE PERMISSION LABELS are local rather than i18next keys: permission strings like "supplier.edit" collide with i18next's
// default key separator, and depth varies per permission at two or three segments, so a flat map here is simpler and safer
// than fighting nested translation keys for a fixed, small catalogue. An unlabelled permission renders as its RAW string,
// deliberately: an administrator granting one should still see which it is.
//
// THE CHECKLIST comes from the backend's full Permissions.All catalogue. It used to derive from the union of what roles
// ALREADY held, so a permission added to the catalogue but not yet granted to any role was invisible here and could only ever
// be granted via a direct DB write. See RolesResponse for the field that fixed it.
//
// The server names its two refusals with machine-stable codes, so the mapping is a table.
//
// A failed fetch is not an empty result: without that branch the screen renders its empty state and tells the reader there
// is nothing here.

import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {Card, PageHeading, QueryError, SkeletonList, useToast} from '../../components/ui'
import { listRoles, updateRolePermissions, type Role, type RolesResponse } from '../../api/roles'
import { SupplierApiError } from '../../api/supplier'

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

const ROLE_ERROR_KEYS: Record<string, string> = {
  would_lock_out_role_management: 'roleManagement.errors.wouldLockOutRoleManagement',
  invalid_permission: 'roleManagement.errors.invalidPermission',
}

function permissionLabel(label: { ar: string; en: string } | undefined, isArabic: boolean, permission: string): string {
  if (!label) return permission
  return isArabic ? label.ar : label.en
}

export function RolesPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const queryClient = useQueryClient()
  const { notify } = useToast()
  const rolesQuery = useQuery({ queryKey: ['roles'], queryFn: listRoles })
  const roles = rolesQuery.data?.roles ?? []
  const allPermissions = [...(rolesQuery.data?.allPermissions ?? [])].sort((a, b) => a.localeCompare(b))

  const updateMutation = useMutation({
    mutationFn: ({ roleName, permissions }: { roleName: string; permissions: string[] }) => updateRolePermissions(roleName, permissions),
    onSuccess: (updated) => {
      queryClient.setQueryData<RolesResponse>(['roles'], (prev) =>
        prev ? { ...prev, roles: prev.roles.map((r) => (r.name === updated.name ? updated : r)) } : prev,
      )
    },
    onError: (err) => {
      const code = err instanceof SupplierApiError ? err.message : undefined
      const messageKey = ROLE_ERROR_KEYS[code ?? ''] ?? 'roleManagement.errors.updateFailed'
      const message = t(messageKey)
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
  if (rolesQuery.isError) return <QueryError error={rolesQuery.error} onRetry={() => void rolesQuery.refetch()} />


  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('roleManagement.title')} subtitle={t('roleManagement.subtitle')} />
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
                    className="flex cursor-pointer items-center gap-3 rounded-[var(--radius-md)] p-3 text-[length:var(--text-body-sm)]"
                    style={{ border: `1px solid ${checked ? 'var(--color-brand-solid)' : 'var(--color-border)'}`, backgroundColor: checked ? 'var(--color-brand-subtle)' : 'transparent' }}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={updateMutation.isPending}
                      onChange={() => toggle(role, permission)}
                    />
                    <span style={{ color: 'var(--color-text-primary)' }}>{permissionLabel(label, isArabic, permission)}</span>
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
