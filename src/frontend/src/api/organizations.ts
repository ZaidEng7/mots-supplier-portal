import { problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'

export type OrganizationType = 'Hotel' | 'MotBody' | 'Ministry'

export interface OrgUnit {
  id: string
  organizationId: string
  parentOrgUnitId: string | null
  name: string
}

export interface Organization {
  id: string
  legalNameAr: string
  legalNameEn: string
  organizationType: OrganizationType
  contactEmail: string | null
  contactPhone: string | null
  isActive: boolean
  orgUnits: OrgUnit[]
}

export interface SupplierOrgLink {
  id: string
  supplierId: string
  supplierReferenceCode: string
  organizationId: string
  createdAt: string
}

export class OrganizationApiError extends Error {
  status: number
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.status = status
  }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new OrganizationApiError(res.status, body)
  return body as T
}

export async function listOrganizations(): Promise<Organization[]> {
  const res = await apiFetch('/api/v1/organizations')
  return parseOrThrow(res)
}

export async function createOrganization(payload: {
  legalNameAr: string
  legalNameEn: string
  organizationType: OrganizationType
  contactEmail?: string | null
  contactPhone?: string | null
}): Promise<Organization> {
  const res = await apiFetch('/api/v1/organizations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function addOrgUnit(organizationId: string, name: string, parentOrgUnitId?: string | null): Promise<Organization> {
  const res = await apiFetch(`/api/v1/organizations/${organizationId}/org-units`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, parentOrgUnitId: parentOrgUnitId ?? null }),
  })
  return parseOrThrow(res)
}

export async function removeOrgUnit(organizationId: string, orgUnitId: string): Promise<Organization> {
  const res = await apiFetch(`/api/v1/organizations/${organizationId}/org-units/${orgUnitId}`, { method: 'DELETE' })
  return parseOrThrow(res)
}

export async function listSupplierOrgLinks(supplierReferenceCode: string): Promise<SupplierOrgLink[]> {
  const res = await apiFetch(`/api/v1/organizations/supplier-links/${supplierReferenceCode}`)
  return parseOrThrow(res)
}

export async function createSupplierOrgLink(supplierReferenceCode: string, organizationId: string): Promise<SupplierOrgLink> {
  const res = await apiFetch(`/api/v1/organizations/supplier-links/${supplierReferenceCode}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ organizationId }),
  })
  return parseOrThrow(res)
}

export async function removeSupplierOrgLink(linkId: string): Promise<void> {
  const res = await apiFetch(`/api/v1/organizations/supplier-links/${linkId}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 204) {
    const text = await res.text()
    throw new OrganizationApiError(res.status, text ? JSON.parse(text) : null)
  }
}
