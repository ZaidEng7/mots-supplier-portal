// FEAT-19.1 and 19.2's two reports, and the export behind them.
//
// A counted bucket's key is the enum member name, which the SPA maps through the label catalogue.
//
// A cycle-time interval carries how many RFQs its median covers, rendered beside the median rather than hidden,
// because a median over two RFQs and one over two hundred are different claims. The median is null when nothing
// has completed the interval - never zero, which would read as "instant". The report also carries the earliest
// audited transition, and so the earliest date any cycle time here can measure from; it is rendered on the
// screen, because RFQs that moved before audit logging existed contribute to nothing and a report that omits them
// silently reads as a low count rather than as missing data.
//
// getProcurementReport returns null on a 404, because a 404 is not a failure here, it is an answer: this report is
// scoped to the caller's buying body (BRULE-029), and two personas deliberately have none - the bootstrap
// administrator and ministry_viewer. For them the handler returns null and the endpoint 404s, correctly. It used
// to reach the screen as a thrown error, so the card offered "The report could not be loaded" and a Try again
// that could never succeed, on every visit, for those two accounts. Returning null says the honest thing instead
// and the screen explains it.
//
// downloadReport fetches through apiFetch and hands the result to the browser as a blob, NOT as an anchor link.
// The session is a Bearer token in a header, so a plain <a href> would reach the endpoint unauthenticated and
// download a 401 body as a file - a failure that looks like a successful download until someone opens it. The
// filename comes from Content-Disposition when the server sent one, so the name in the downloads folder is the
// server's rather than a second copy of the naming rule maintained here.
//
// downloadSupplierRegistry and downloadMinistrySupplierFeed are the same mechanism pointed at different routes,
// which is why the blob handling is one function all three call rather than three copies. It is not a report: it is the registry itself, one row per
// supplier, and it is gated on supplier.registry.export rather than report.read - the screen only offers it to
// an account that holds that, because offering a download which can only answer 403 is the defect this same
// screen was just fixed for.

import { apiFetch } from './auth'

export interface ReportCount {
  key: string
  count: number
}

export interface CycleTimeInterval {
  key: string
  sampleSize: number
  medianHours: number | null
}

export interface ProcurementReport {
  rfqsByState: ReportCount[]
  cycleTimes: CycleTimeInterval[]
  awardsByState: ReportCount[]
  totalRfqs: number
  coverageFloor: string | null
}

export interface ComplianceReport {
  suppliersByLifecycleState: ReportCount[]
  documentsByState: ReportCount[]
  totalSuppliers: number
  documentsExpiringSoon: number
  documentsExpired: number
}

export async function getProcurementReport(from?: string, to?: string): Promise<ProcurementReport | null> {
  const params = new URLSearchParams()
  if (from) params.set('from', new Date(from).toISOString())
  if (to) params.set('to', new Date(to).toISOString())

  const query = params.toString()
  const response = await apiFetch(`/api/v1/reports/procurement${query ? `?${query}` : ''}`)
  if (response.status === 404) return null
  if (!response.ok) throw new Error(`reports.procurement ${response.status}`)
  return (await response.json()) as ProcurementReport
}

export async function getComplianceReport(): Promise<ComplianceReport> {
  const response = await apiFetch('/api/v1/reports/compliance')
  if (!response.ok) throw new Error(`reports.compliance ${response.status}`)
  return (await response.json()) as ComplianceReport
}

export async function downloadReport(
  kind: 'procurement' | 'compliance',
  format: 'pdf' | 'csv',
  from?: string,
  to?: string,
): Promise<void> {
  const params = new URLSearchParams({ format })
  if (kind === 'procurement' && from) params.set('from', new Date(from).toISOString())
  if (kind === 'procurement' && to) params.set('to', new Date(to).toISOString())

  const response = await apiFetch(`/api/v1/reports/${kind}/export?${params.toString()}`)
  if (!response.ok) throw new Error(`reports.${kind}.export ${response.status}`)

  await saveAsFile(response, `${kind}-report.${format}`)
}

export async function downloadSupplierRegistry(): Promise<void> {
  const response = await apiFetch('/api/v1/suppliers/export')
  if (!response.ok) throw new Error(`suppliers.export ${response.status}`)

  await saveAsFile(response, 'mots-suppliers.csv')
}

export async function downloadMinistrySupplierFeed(): Promise<void> {
  const response = await apiFetch('/api/v1/feeds/suppliers')
  if (!response.ok) throw new Error(`feeds.suppliers ${response.status}`)

  await saveAsFile(response, 'mots-feed-suppliers.csv')
}

export async function downloadMinistryRfqFeed(): Promise<void> {
  const response = await apiFetch('/api/v1/feeds/rfqs')
  if (!response.ok) throw new Error(`feeds.rfqs ${response.status}`)

  await saveAsFile(response, 'mots-feed-rfqs.csv')
}

async function saveAsFile(response: Response, fallbackName: string): Promise<void> {
  const disposition = response.headers.get('content-disposition') ?? ''
  const named = /filename=([^;]+)/i.exec(disposition)?.[1]?.trim()

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = named || fallbackName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
