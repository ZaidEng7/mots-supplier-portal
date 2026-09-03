import { apiFetch } from './auth'

/** One counted bucket. `key` is the enum member name; the SPA maps it through the label catalogue. */
export interface ReportCount {
  key: string
  count: number
}

export interface CycleTimeInterval {
  key: string
  /**
   * How many RFQs the median covers. Rendered beside the median rather than hidden, because a
   * median over two RFQs and one over two hundred are different claims.
   */
  sampleSize: number
  /** Null when nothing has completed the interval - never zero, which would read as "instant". */
  medianHours: number | null
}

export interface ProcurementReport {
  rfqsByState: ReportCount[]
  cycleTimes: CycleTimeInterval[]
  awardsByState: ReportCount[]
  totalRfqs: number
  /**
   * The earliest audited transition, and so the earliest date any cycle time here can measure from.
   * Rendered on the screen: RFQs that moved before audit logging existed contribute to nothing, and
   * a report that omits them silently reads as a low count rather than as missing data.
   */
  coverageFloor: string | null
}

export interface ComplianceReport {
  suppliersByLifecycleState: ReportCount[]
  documentsByState: ReportCount[]
  totalSuppliers: number
  documentsExpiringSoon: number
  documentsExpired: number
}

export async function getProcurementReport(from?: string, to?: string): Promise<ProcurementReport> {
  const params = new URLSearchParams()
  if (from) params.set('from', new Date(from).toISOString())
  if (to) params.set('to', new Date(to).toISOString())

  const query = params.toString()
  const response = await apiFetch(`/api/v1/reports/procurement${query ? `?${query}` : ''}`)
  if (!response.ok) throw new Error(`reports.procurement ${response.status}`)
  return (await response.json()) as ProcurementReport
}

export async function getComplianceReport(): Promise<ComplianceReport> {
  const response = await apiFetch('/api/v1/reports/compliance')
  if (!response.ok) throw new Error(`reports.compliance ${response.status}`)
  return (await response.json()) as ComplianceReport
}

/**
 * Downloads a report artefact.
 *
 * <p>Fetched through `apiFetch` and handed to the browser as a blob, NOT linked with an anchor. The
 * session is a Bearer token in a header, so a plain `<a href>` would reach the endpoint
 * unauthenticated and download a 401 body as a file - a failure that looks like a successful
 * download until someone opens it.</p>
 *
 * <p>The filename comes from Content-Disposition when the server sent one, so the name in the
 * downloads folder is the server's, not a second copy of the naming rule maintained here.</p>
 */
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

  const disposition = response.headers.get('content-disposition') ?? ''
  const named = /filename=([^;]+)/i.exec(disposition)?.[1]?.trim()

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = named || `${kind}-report.${format}`
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
