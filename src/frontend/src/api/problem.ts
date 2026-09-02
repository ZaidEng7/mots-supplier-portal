/**
 * API-ARCHITECTURE.md §7's RFC 9457 problem+json, as the SPA reads it.
 *
 * <p>§7 is explicit about which members a client may branch on: *"clients switch on `type`/`code`,
 * **never** on `title`/`detail` (those may be localized/reworded)"*. So `code` is what error
 * handling compares, and `detail`/`title` are only ever displayed.</p>
 *
 * <p>`errors`, `missingFields` and anything else the server carries are RFC 9457 extension members —
 * permitted, and used here for ASP.NET's validation map and the onboarding submit's incomplete
 * list.</p>
 */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  /** SCREAMING_SNAKE, machine-stable. This is what to branch on. */
  code?: string
  traceId?: string
  correlationId?: string
  /** §7.2's bilingual field errors, on a 422. */
  errors?: unknown
  [extension: string]: unknown
}

/**
 * The message to show a human. `detail` first (§7's "human-readable explanation of this
 * occurrence"), then `title`, then a caller-supplied fallback — never `code`, which is an
 * identifier and not prose.
 */
export function problemMessage(problem: ProblemDetails | null, fallback: string): string {
  return problem?.detail ?? problem?.title ?? fallback
}

/**
 * Branch on the machine-stable identifier, per §7.
 *
 * <p>Compared case-insensitively against SCREAMING_SNAKE because the server derives some codes by
 * upper-casing a handler's own token — a caller writing the lower-case form it used to match on
 * would otherwise fail silently, which is the failure this whole batch is about.</p>
 */
export function hasCode(problem: ProblemDetails | null, code: string): boolean {
  return problem?.code?.toUpperCase() === code.toUpperCase()
}
