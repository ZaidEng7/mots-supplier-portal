// SCR-045: what the chrome banner is allowed to say.
//
// Row-scoped server-side: a supplier is told only about their own award's sync, buyer staff only about their own
// organization's, and "no ERP configured at all" only reaches callers who could act on it. The client asks one
// question and the server decides what this caller may know.

import { apiFetch } from './auth'

export interface SystemStatus {
  erpDegraded: boolean
  erpNotConfigured: boolean
}

export async function getSystemStatus(): Promise<SystemStatus> {
  const res = await apiFetch('/api/v1/system/status')
  if (!res.ok) throw new Error(`system status: ${res.status}`)
  return (await res.json()) as SystemStatus
}
