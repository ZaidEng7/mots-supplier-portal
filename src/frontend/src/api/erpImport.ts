// What importing the ministry's ERP suppliers would do.
//
// THE REPORT IS A PREVIEW AND NOTHING ELSE. There is no route here that performs the import, because there is no
// such route on the server yet. When there is, it belongs beside this one so that both sit in front of a reader
// at once - a preview whose write lives somewhere else invites somebody to run one and forget the other.
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

export class ErpPreviewError extends Error {
  constructor(
    readonly status: number,
    readonly serverDetail: string | null,
  ) {
    super(`erp_preview_failed_${status}`)
  }
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
