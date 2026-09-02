import { apiFetch } from './auth'

export interface LegalInfo {
  legalNameAr: string | null
  legalNameEn: string | null
  registrationNumber: string | null
  taxId: string | null
  supplierType: string | null
  establishedOn: string | null
}

export interface Representative {
  id: string
  fullName: string
  email: string
  phone: string | null
  position: string | null
  isPrimary: boolean
}

export interface Address {
  id: string
  kind: string
  line1: string
  line2: string | null
  city: string
  regionCode: string
  country: string
  postalCode: string | null
  latitude: number | null
  longitude: number | null
  isPrimary: boolean
}

export interface Contact {
  id: string
  fullName: string
  email: string
  phone: string | null
  role: string | null
}

export interface Branch {
  id: string
  nameAr: string
  nameEn: string
  addressId: string | null
  isActive: boolean
}

export interface BankAccount {
  id: string
  accountHolderName: string
  bankName: string
  branchName: string | null
  maskedAccountNumber: string
  swiftBic: string | null
  currencyCode: string
  isDefault: boolean
}

export interface SupplierProfile {
  referenceCode: string
  displayNameAr: string
  displayNameEn: string
  description: string | null
  website: string | null
  logoStorageKey: string | null
  supplierGroup: string | null
  onboardingState: string
  /** MSP-63: Active | Suspended | Deactivated | None. Drives which lifecycle actions staff see. */
  lifecycleState: string
  currencyCode: string | null
  legalInfo: LegalInfo | null
  primaryContactPhone: string | null
  representatives: Representative[]
  addresses: Address[]
  contacts: Contact[]
  branches: Branch[]
  bankAccounts: BankAccount[]
  categoryCodes: string[]
  missingProfileFields: string[]
  termsAcceptedVersion: string | null
  termsAcceptedAt: string | null
  rowVersion: number
}

export interface UpdateProfilePayload {
  description?: string | null
  website?: string | null
  supplierGroup?: string | null
  currencyCode?: string | null
  primaryContactPhone?: string | null
}

export interface UpdateLegalInfoPayload {
  legalNameAr: string
  legalNameEn: string
  registrationNumber?: string | null
  taxId?: string | null
  supplierType: string
  establishedOn?: string | null
}

export class SupplierApiError extends Error {
  status: number
  missingFields?: string[]
  fieldErrors?: Record<string, string[]>
  /** MSP-65: someone else saved this supplier since we read it. Callers surface a localized
   * message (NFR-USE-004) rather than the raw 409 - see `errors.concurrencyConflict`. */
  isConcurrencyConflict: boolean
  /** MSP-77: refused because the field is not in the reviewer's flagged set while InfoRequested. */
  isFieldNotFlagged: boolean

  constructor(status: number, body: unknown) {
    const b = body as { error?: string; missingFields?: string[]; errors?: Record<string, string[]> } | null
    super(b?.error ?? `Request failed: ${status}`)
    this.status = status
    this.missingFields = b?.missingFields
    this.fieldErrors = b?.errors
    this.isConcurrencyConflict = status === 409 && b?.error === 'concurrency_conflict'
    this.isFieldNotFlagged = status === 403 && b?.error === 'field_not_flagged'
  }
}

/** MSP-65: the row version we last read travels as the standard `If-Match` header, so the server
 * can reject a write built on stale data instead of silently overwriting the other editor. */
function ifMatch(rowVersion?: number): Record<string, string> {
  return rowVersion === undefined ? {} : { 'If-Match': `"${rowVersion}"` }
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function getOwnSupplier(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me')
  return parseOrThrow(res)
}

export async function updateProfile(supplierCode: string, payload: UpdateProfilePayload, rowVersion?: number): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', ...ifMatch(rowVersion) },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateLegalInfo(payload: UpdateLegalInfoPayload, rowVersion?: number): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/legal-info', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...ifMatch(rowVersion) },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function uploadLogo(file: File): Promise<SupplierProfile> {
  const form = new FormData()
  form.append('file', file)
  const res = await apiFetch('/api/v1/suppliers/me/logo', { method: 'POST', body: form })
  return parseOrThrow(res)
}

export async function getLogoDownloadUrl(): Promise<string> {
  const res = await apiFetch('/api/v1/suppliers/me/logo/download-url')
  const body = await parseOrThrow<{ url: string }>(res)
  return body.url
}

export async function acceptTerms(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/accept-terms', { method: 'POST' })
  return parseOrThrow(res)
}

export async function submitApplication(supplierCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/onboarding/submit`, { method: 'POST' })
  return parseOrThrow(res)
}

export async function resubmitApplication(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/resubmit-application', { method: 'POST' })
  return parseOrThrow(res)
}
