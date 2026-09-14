// SCR-908 and API-ARCHITECTURE.md's ops row: GET /api/v1/meta, carrying the build's version and commit.
//
// The commit is null when the build carries no source revision - a local `dotnet run`, for instance. The
// maintenance notice is SCR-044's, and is null unless an operator has configured one; its two dates are both
// optional and both free text as configured, displayed and never compared against a clock here, because the
// operator who wrote the notice is the one who knows when it applies.
//
// getMeta uses plain fetch rather than apiFetch. The endpoint is anonymous and the about screen has to render
// for someone who cannot sign in, so going through apiFetch would attach a token it does not need and, worse,
// could raise SCR-040's expiry overlay over a public page.

import { API_BASE_URL } from './auth'

export interface Meta {
  version: string | null
  commit: string | null
  maintenance: MaintenanceNotice | null
}

export interface MaintenanceNotice {
  messageAr: string | null
  messageEn: string | null
  from: string | null
  to: string | null
}

export async function getMeta(): Promise<Meta> {
  const res = await fetch(`${API_BASE_URL}/api/v1/meta`)
  if (!res.ok) throw new Error(`meta ${res.status}`)
  return (await res.json()) as Meta
}
