// The supplier's own profile: the read, the profile and legal-info writes, the logo, terms, and submitting or
// resubmitting the application. Every other supplier-child module in this folder throws SupplierApiError and
// returns this profile.
//
// THE WIRE NAMES, all settled by R-9 in §12.2 and all of them wrong in this file until T-110: supplierCode, not
// referenceCode; defaultCurrency, not currencyCode; categories, not categoryCodes. The client type said
// referenceCode for three batches and the field simply arrived as undefined. The lifecycle state is MSP-63's -
// Active, Suspended, Deactivated or None - and drives which lifecycle actions staff see.
//
// SUPPLIERAPIERROR carries three flags. The concurrency one is MSP-65's: someone else saved this supplier since we
// read it, and callers surface a localized message per NFR-USE-004 rather than the raw 412 - see
// errors.concurrencyConflict. §8.1 moved it from a 409 { error: "concurrency_conflict" } to a 412 ETAG_MISMATCH,
// because a lost update is a failed precondition and 409 now means only what §7.1 says it means. The flagged-field
// one is MSP-77's: refused because the field is not in the reviewer's flagged set while InfoRequested. And §7's
// machine-stable code is carried so a caller can branch on it rather than on the human message, which is what §7
// tells clients to do and what hasCode exists for; it was added in batch 10, because T-077's two refusals - own
// account, last administrator - are things an administrator has to understand, and a caller matching on `detail`
// would break the day the wording changed.
//
// ONE FUNCTION WAS REMOVED IN BATCH 12, and this is the reason rather than an apology. It built If-Match: "4" from
// the numeric rowVersion in the body. That matched the server until batch 11 changed the ETag to carry the build
// alongside the row - the real header is now "AAAABA.1bdf128d", a base64 row version and a build hash - so a
// hand-built numeric tag could never match again, and every write through it answered 412. Worse, it was not
// merely wrong, it OVERRODE the right answer: apiFetch already attaches the stored ETag and an explicit If-Match
// always wins, so that call site's guess beat the value the server had actually issued. Deleting it restores the
// design apiFetch describes - attached centrally, from what the server sent, never reconstructed at a call site.
// Found by walking onboarding as a supplier: choosing a currency, pressing Save, and watching the value come back
// empty after a reload.
//
// EVERY PROFILE RESPONSE IS PARSED THROUGH ONE FUNCTION, which files its ETag under BOTH spellings of this one
// resource. Every write here answers with the whole profile and a fresh ETag, and apiFetch already re-files that
// under the prefix the precondition came from - which is /suppliers/me, because that is the path these writes use.
// The PATCH does not: it is addressed by supplier code. So a legal-info save followed by a profile save refreshed
// one prefix and asserted against the other, and the second save answered 412 on a page where nothing else had
// touched the record.
//
// Both, not one. Filing only under the code path left the other direction open, and the walkthrough fell into it:
// save the company details on step 1, go to step 3, add an address, and the save was refused with "This resource
// changed after you loaded it" on a record nobody else had touched. The PATCH had filed version 7 under
// /suppliers/{code}, while /suppliers/me/addresses and /suppliers/me/branches walk up to /suppliers/me - still
// holding version 6 from the page's own read. Two keys for one aggregate, refreshed one at a time. A full page
// reload cleared it, which is exactly why it survived: every fix attempt began with a reload. Routing every
// profile-returning call through one parser keeps the two paths carrying the same version.
//
// The two spellings are declared to be one resource once, in that parser, because it is the only place that can
// know it: `me` resolves to a code the caller does not learn until the body arrives. After that the store keeps
// them in step on its own, including for writes that return a document or a branch rather than a profile and never
// reach the parser.
//
// getOwnSupplier files the read's ETag under the SUPPLIER-CODE path as well as the one it was read from. Same
// shape as D-47, one aggregate later: a supplier reads itself at /suppliers/me and is written at
// /suppliers/{code}, and the ETag store walks a path upward and never sideways, so the write could never find the
// version the read had issued and the server correctly refused it with 428.

import { ProblemError, hasCode, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import { aliasETagPaths, rememberETag } from './etags'

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
  supplierCode: string
  displayNameAr: string
  displayNameEn: string
  description: string | null
  website: string | null
  logoStorageKey: string | null
  supplierGroup: string | null
  onboardingState: string
  lifecycleState: string
  defaultCurrency: string | null
  legalInfo: LegalInfo | null
  primaryContactPhone: string | null
  representatives: Representative[]
  addresses: Address[]
  contacts: Contact[]
  branches: Branch[]
  bankAccounts: BankAccount[]
  categories: string[]
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

export class SupplierApiError extends ProblemError {
  missingFields?: string[]
  fieldErrors?: Record<string, string[]>
  isConcurrencyConflict: boolean
  isFieldNotFlagged: boolean
  code?: string

  constructor(status: number, body: unknown) {
    super(status, body)
    const b = body as ProblemDetails | null
    this.missingFields = b?.missingFields as string[] | undefined
    this.fieldErrors = b?.errors as Record<string, string[]> | undefined
    this.isConcurrencyConflict = status === 412 && hasCode(body as ProblemDetails | null, 'ETAG_MISMATCH')
    this.isFieldNotFlagged = status === 403 && hasCode(body as ProblemDetails | null, 'FIELD_NOT_FLAGGED')
    this.code = b?.code
  }
}


async function profileFrom(res: Response): Promise<SupplierProfile> {
  const etag = res.headers.get('ETag')
  const profile = await parseOrThrow<SupplierProfile>(res)
  aliasETagPaths(`/api/v1/suppliers/${profile.supplierCode}`, '/api/v1/suppliers/me')
  if (etag) rememberETag(`/api/v1/suppliers/${profile.supplierCode}`, etag)
  return profile
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function getOwnSupplier(): Promise<SupplierProfile> {
  return profileFrom(await apiFetch('/api/v1/suppliers/me'))
}

export async function updateProfile(supplierCode: string, payload: UpdateProfilePayload): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return profileFrom(res)
}

export async function updateLegalInfo(payload: UpdateLegalInfoPayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/legal-info', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return profileFrom(res)
}

export async function uploadLogo(file: File): Promise<SupplierProfile> {
  const form = new FormData()
  form.append('file', file)
  const res = await apiFetch('/api/v1/suppliers/me/logo', { method: 'POST', body: form })
  return profileFrom(res)
}

export async function getLogoDownloadUrl(): Promise<string> {
  const res = await apiFetch('/api/v1/suppliers/me/logo/download-url')
  const body = await parseOrThrow<{ url: string }>(res)
  return body.url
}

export async function acceptTerms(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/accept-terms', { method: 'POST' })
  return profileFrom(res)
}

export async function submitApplication(supplierCode: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/${supplierCode}/onboarding/submit`, { method: 'POST' })
  return profileFrom(res)
}

export async function resubmitApplication(): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/resubmit-application', { method: 'POST' })
  return profileFrom(res)
}
