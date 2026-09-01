import { apiFetch } from './auth'
import { SupplierApiError } from './supplier'

export interface Role {
  name: string
  permissions: string[]
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function listRoles(): Promise<Role[]> {
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
