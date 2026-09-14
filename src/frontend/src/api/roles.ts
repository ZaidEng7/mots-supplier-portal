// SCR-703: the roles and what each one grants.
//
// allPermissions is the backend's canonical Permissions.All catalogue rather than something derived from what
// roles happen to already hold - see ManageRolesContracts.cs's RolesResponse. That was an FR-ADM-002 bug fix:
// without it, a permission added to the catalogue but not yet granted to any role had no way to ever be granted
// through this screen.

import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Role {
  name: string
  permissions: string[]
}

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
