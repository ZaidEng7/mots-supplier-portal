// What importing the ministry's ERP suppliers would do.
//
// THE PREVIEW AND THE RUN SIT TOGETHER, because the forecast is only worth anything if the thing it forecasts is
// one click away, and a write that lived elsewhere invites somebody to read one and press the other without
// looking. They share their failure type for the same reason: the three ways either call can fail are the same
// three, and an operator should not have to learn them twice.
//
// THE STATUS CODES ARE CARRIED, NOT FLATTENED, and that is the only unusual thing in this file. Three refusals
// mean three different things to the person reading the screen: 503 is nobody configured the integration, 502 is
// the ERP refused us, and anything else is ours. A single "preview_unavailable" would tell an administrator to
// go and look at logs for a problem the server already described precisely - and the most likely one, a
// credential without rights on a record type, is fixed by asking the other team rather than by reading anything
// here.
//
// THE DETAIL FROM THE SERVER IS KEPT for the same reason: "403 PermissionError" names which side to fix. It is
// the ERP's own words and it is shown as such rather than translated, because a translated approximation of
// another system's error is a worse thing to forward to that system's owner.

import { apiFetch } from './auth'

export type ErpImportAction = 'Create' | 'Update' | 'Refuse'

export interface ErpImportPreviewRow {
  externalId: string
  name: string | null
  action: ErpImportAction
  notes: string[]
  matchedReferenceCode: string | null
}

export interface ErpImportPreviewReport {
  erpSupplierCount: number
  wouldCreate: number
  wouldUpdate: number
  refused: number
  rows: ErpImportPreviewRow[]
}

// The fields are declared and assigned rather than written as constructor parameter properties, because this
// project compiles with erasableSyntaxOnly: parameter properties emit real assignments, so TypeScript refuses
// them. It is the same class either way, spelled the way this build allows.
export class ErpPreviewError extends Error {
  readonly status: number

  readonly serverDetail: string | null

  constructor(status: number, serverDetail: string | null) {
    super(`erp_preview_failed_${status}`)
    this.status = status
    this.serverDetail = serverDetail
  }
}

export type ErpImportOutcome = 'Created' | 'Updated' | 'Refused' | 'Failed'

export interface ErpImportResultRow {
  externalId: string
  name: string | null
  outcome: ErpImportOutcome
  referenceCode: string | null
  notes: string[]
}

export interface ErpImportRunReport {
  erpSupplierCount: number
  created: number
  updated: number
  refused: number
  failed: number
  rows: ErpImportResultRow[]
}

export async function runErpImport(): Promise<ErpImportRunReport> {
  const response = await apiFetch('/api/v1/admin/erp-import/run', { method: 'POST' })

  if (!response.ok) {
    throw new ErpPreviewError(response.status, await detailOf(response))
  }

  return (await response.json()) as ErpImportRunReport
}

export async function previewErpImport(): Promise<ErpImportPreviewReport> {
  const response = await apiFetch('/api/v1/admin/erp-import/preview', { method: 'POST' })

  if (!response.ok) {
    throw new ErpPreviewError(response.status, await detailOf(response))
  }

  return (await response.json()) as ErpImportPreviewReport
}

async function detailOf(response: Response): Promise<string | null> {
  try {
    const problem = (await response.json()) as { detail?: string }
    return problem.detail ?? null
  } catch {
    return null
  }
}
