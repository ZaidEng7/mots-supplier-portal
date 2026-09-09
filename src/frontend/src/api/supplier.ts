import { hasCode, hasProblemProse, problemMessage, type ProblemDetails } from './problem'
import { apiFetch } from './auth'
import { rememberETag } from './etags'

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
  /** R-9 renamed this on the wire (§12.2). The client type said `referenceCode` for three batches
   * and the field simply arrived as undefined — see `supplierCode`, `defaultCurrency` and
   * `categories` below, all three of which were wrong here until T-110. */
  supplierCode: string
  displayNameAr: string
  displayNameEn: string
  description: string | null
  website: string | null
  logoStorageKey: string | null
  supplierGroup: string | null
  onboardingState: string
  /** MSP-63: Active | Suspended | Deactivated | None. Drives which lifecycle actions staff see. */
  lifecycleState: string
  /** R-9: `defaultCurrency` on the wire, not `currencyCode`. */
  defaultCurrency: string | null
  legalInfo: LegalInfo | null
  primaryContactPhone: string | null
  representatives: Representative[]
  addresses: Address[]
  contacts: Contact[]
  branches: Branch[]
  bankAccounts: BankAccount[]
  /** R-9: `categories` on the wire, not `categoryCodes`. */
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

export class SupplierApiError extends Error {
  status: number
  missingFields?: string[]
  fieldErrors?: Record<string, string[]>
  /** MSP-65: someone else saved this supplier since we read it. Callers surface a localized
   * message (NFR-USE-004) rather than the raw 412 - see `errors.concurrencyConflict`. §8.1 moved
   * this from a 409 { error: "concurrency_conflict" } to a 412 ETAG_MISMATCH: a lost update is a
   * failed precondition, and 409 now means only what §7.1 says it means. */
  isConcurrencyConflict: boolean
  /** MSP-77: refused because the field is not in the reviewer's flagged set while InfoRequested. */
  isFieldNotFlagged: boolean
  /** §7's machine-stable code, carried so a caller can branch on it rather than on the human message -
   * which is what §7 tells clients to do, and what `hasCode` exists for. Added in batch 10: T-077's two
   * refusals (own account, last administrator) are things an administrator has to understand, and a
   * caller matching on `detail` would break the day the wording changed. */
  code?: string

  /** Read by `errorDetail`: this message is the server's own prose, not a bug's. False when the
   * problem document carried no `title` and no `detail`, because `problemMessage` then falls back to
   * "Request failed: <status>", which is developer text and must not reach a reader. */
  isProblemError: boolean
  constructor(status: number, body: unknown) {
    const b = body as ProblemDetails | null
    super(problemMessage(b, `Request failed: ${status}`))
    this.isProblemError = hasProblemProse(b)
    this.status = status
    this.missingFields = b?.missingFields as string[] | undefined
    this.fieldErrors = b?.errors as Record<string, string[]> | undefined
    this.isConcurrencyConflict = status === 412 && hasCode(b, 'ETAG_MISMATCH')
    this.isFieldNotFlagged = status === 403 && hasCode(b, 'FIELD_NOT_FLAGGED')
    this.code = b?.code
  }
}

/**
 * REMOVED in batch 12, and this comment is the reason rather than an apology.
 *
 * This built `If-Match: "4"` from the numeric rowVersion in the body. That matched the server until
 * batch 11 changed the ETag to carry the build alongside the row - the real header is now
 * `"AAAABA.1bdf128d"`, base64 row version and a build hash - so a hand-built numeric tag could never
 * match again, and every write through it answered 412.
 *
 * Worse, it was not merely wrong, it OVERRODE the right answer: apiFetch already attaches the stored
 * ETag and its own comment says "an explicit If-Match always wins", so this call site's guess beat the
 * value the server had actually issued. Deleting it restores the design that comment describes -
 * attached centrally, from what the server sent, never reconstructed at a call site.
 *
 * Found by walking onboarding as a supplier: choosing a currency, pressing Save, and watching the
 * value come back empty after a reload.
 */

/**
 * Parses a supplier response and files its ETag under the SUPPLIER-CODE path.
 *
 * Every write here answers with the whole profile and a fresh ETag, and `apiFetch` already re-files
 * that under the prefix the precondition came from - which is `/suppliers/me`, because that is the
 * path these writes use. The PATCH does not: it is addressed by supplier code. So a legal-info save
 * followed by a profile save refreshed one prefix and asserted against the other, and the second save
 * answered 412 on a page where nothing else had touched the record.
 *
 * Routing every profile-returning call through here keeps the two paths carrying the same version.
 */
async function profileFrom(res: Response): Promise<SupplierProfile> {
  const etag = res.headers.get('ETag')
  const profile = await parseOrThrow<SupplierProfile>(res)
  if (etag) rememberETag(`/api/v1/suppliers/${profile.supplierCode}`, etag)
  return profile
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

/**
 * Files the read's ETag under the SUPPLIER-CODE path as well as the one it was read from.
 *
 * Same shape as D-47, one aggregate later: a supplier reads itself at `/suppliers/me` and is written
 * at `/suppliers/{code}`. The ETag store walks a path upward and never sideways, so the write could
 * never find the version the read had issued and the server correctly refused it with 428.
 *
 * Declared here because this function is the only thing that knows the two paths name one resource -
 * `me` resolves to a supplier code the caller does not otherwise learn until the body arrives.
 */
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
