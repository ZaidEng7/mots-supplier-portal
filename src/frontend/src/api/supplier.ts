import { apiFetch } from './auth'

export interface SupplierProfile {
  referenceCode: string
  displayNameAr: string
  displayNameEn: string
  onboardingState: string
  registrationNumber: string | null
  taxId: string | null
  addressLine: string | null
  city: string | null
  country: string | null
  currencyCode: string | null
  primaryContactPhone: string | null
  missingProfileFields: string[]
  termsAcceptedVersion: string | null
  termsAcceptedAt: string | null
}

export interface UpdateProfilePayload {
  registrationNumber?: string | null
  taxId?: string | null
  addressLine?: string | null
  city?: string | null
  country?: string | null
  currencyCode?: string | null
  primaryContactPhone?: string | null
}

export class SupplierApiError extends Error {
  status: number
  missingFields?: string[]

  constructor(status: number, body: unknown) {
    const b = body as { error?: string; missingFields?: string[] } | null
    super(b?.error ?? `Request failed: ${status}`)
    this.status = status
    this.missingFields = b?.missingFields
  }
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

export async function updateProfile(payload: UpdateProfilePayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/profile', {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function acceptTerms(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/accept-terms', { method: 'POST' })
  return parseOrThrow(res)
}

export async function submitApplication(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/submit-application', { method: 'POST' })
  return parseOrThrow(res)
}

export async function resubmitApplication(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/resubmit-application', { method: 'POST' })
  return parseOrThrow(res)
}
