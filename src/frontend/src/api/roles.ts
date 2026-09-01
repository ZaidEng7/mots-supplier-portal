import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Role {
  name: string
  permissions: string[]
}

/** FR-ADM-002 bug fix: allPermissions is the backend's canonical Permissions.All catalog, not
 * derived from what roles happen to already hold - see ManageRolesContracts.cs's RolesResponse
 * doc comment. Without this, a permission added to the catalog but not yet granted to any role
 * had no way to ever be granted through this UI. */
export interface RolesResponse {
  roles: Role[]
  allPermissions: string[]
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function listRoles(): Promise<RolesResponse> {
  const res = await apiFetch('/api/v1/admin/roles')
  return parseOrThrow(res)
}

export async function updateRolePermissions(roleName: string, permissions: string[]): Promise<Role> {
  const res = await apiFetch(`/api/v1/admin/roles/${encodeURIComponent(roleName)}/permissions`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ permissions }),
  })
  return parseOrThrow(res)
}
