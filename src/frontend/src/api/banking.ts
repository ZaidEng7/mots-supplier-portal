import { apiFetch } from './auth'
import type { SupplierProfile } from './supplier'
import { SupplierApiError } from './supplier'

export interface AddBankAccountPayload {
  accountHolderName: string
  bankName: string
  branchName?: string | null
  accountNumber: string
  swiftBic?: string | null
  currencyCode: string
}

/** accountNumber omitted (or empty) leaves the existing encrypted value untouched server-side. */
export interface UpdateBankAccountPayload {
  accountHolderName: string
  bankName: string
  branchName?: string | null
  accountNumber?: string | null
  swiftBic?: string | null
  currencyCode: string
}

async function parseOrThrow<T>(res: Response): Promise<T> {
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new SupplierApiError(res.status, body)
  return body as T
}

export async function addBankAccount(payload: AddBankAccountPayload): Promise<SupplierProfile> {
  const res = await apiFetch('/api/v1/suppliers/me/bank-accounts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function updateBankAccount(id: string, payload: UpdateBankAccountPayload): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/bank-accounts/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return parseOrThrow(res)
}

export async function removeBankAccount(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/bank-accounts/${id}`, { method: 'DELETE' })
  return parseOrThrow(res)
}

export async function setDefaultBankAccount(id: string): Promise<SupplierProfile> {
  const res = await apiFetch(`/api/v1/suppliers/me/bank-accounts/${id}/set-default`, { method: 'POST' })
  return parseOrThrow(res)
}

/** Server-audited reveal (BRULE-014/090/091). Caller must not persist/cache the returned value —
 * display it transiently only (see BankingPage's auto-hide-after-timeout usage). */
export async function revealBankAccount(id: string): Promise<string> {
  const res = await apiFetch(`/api/v1/suppliers/me/bank-accounts/${id}/reveal`, { method: 'POST' })
  const body = await parseOrThrow<{ accountNumber: string }>(res)
  return body.accountNumber
}
