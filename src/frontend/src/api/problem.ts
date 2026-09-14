// API-ARCHITECTURE.md §7's RFC 9457 problem+json, as the SPA reads it.
//
// §7 is explicit about which members a client may branch on: clients switch on type and code, NEVER on title or
// detail, which may be localized or reworded. So `code` is what error handling compares - SCREAMING_SNAKE and
// machine-stable - and detail and title are only ever displayed. `errors`, `missingFields` and anything else the
// server carries are RFC 9457 extension members, which are permitted and are used here for ASP.NET's validation
// map and the onboarding submit's incomplete list. `errors` is §7.2's bilingual field errors, on a 422.
//
// problemMessage is the message to show a human: detail first, per §7's "human-readable explanation of this
// occurrence", then title, then a caller-supplied fallback - never `code`, which is an identifier rather than
// prose.
//
// hasCode branches on the machine-stable identifier. It compares case-insensitively against SCREAMING_SNAKE
// because the server derives some codes by upper-casing a handler's own token, and a caller writing the
// lower-case form it used to match on would otherwise fail silently - which is the failure this whole batch is
// about.
//
// errorDetail reads the server's own explanation back off an unknown throw, or null when there is not one worth
// showing a reader. Every API module here throws a typed error whose message came from problemMessage - the RFC
// 9457 detail the server wrote for a human - so a screen can show that instead of a fallback, which is what the
// read paths were doing with a string written to be a last resort. It tests a MARKER rather than
// `instanceof Error`, because a dropped connection throws a plain TypeError reading "Failed to fetch" and a bug
// in a component throws whatever it throws, neither of which is prose to put in front of a supplier. Only
// errors this application constructed from a problem document carry isProblemError, which is set from
// hasProblemProse - and that also means a bare 503 whose body held no title and no detail falls back, because
// problemMessage then returns "Request failed: 503" and that is developer text too.
//
// ProblemError is the shape every API module's error type shares: the server's message, the status, and whether
// that message is the server's own prose. Eighteen modules each declared their own class with an identical
// constructor - the same cast, the same problemMessage call, the same two assignments - differing only in the
// name and in whatever extra field that module needed. Sonar's duplication gate is what said so out loud, when
// adding one more line to each of them pushed new-code duplication to 4.7% against a 3% ceiling. The separate
// classes are kept rather than collapsed into one, because `instanceof` is how callers tell "the tender API
// refused this" from "the documents API refused this", and several add a field of their own on top -
// isConcurrencyConflict, a validation field. They now differ only in what makes them different.
//
// apiErrorMessage is the message to show when a mutation is refused. Five screens wrote it by hand,
// identically: the concurrency conflict first, because it is the one refusal a reader can act on by reloading,
// then the server's own explanation, then the caller's fallback. Each wrote it as a chain of ternaries, which
// reads as a decision procedure when it is a priority order. isConcurrencyConflict is read structurally rather
// than through a class, because each API module has its own error type and they differ only in the name.

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  code?: string
  traceId?: string
  correlationId?: string
  errors?: unknown
  [extension: string]: unknown
}

export function problemMessage(problem: ProblemDetails | null, fallback: string): string {
  return problem?.detail ?? problem?.title ?? fallback
}

export function hasCode(problem: ProblemDetails | null, code: string): boolean {
  return problem?.code?.toUpperCase() === code.toUpperCase()
}

export function hasProblemProse(problem: ProblemDetails | null): boolean {
  return problem?.detail !== undefined || problem?.title !== undefined
}

export function errorDetail(err: unknown): string | null {
  if (err === null || typeof err !== 'object') return null
  if ((err as { isProblemError?: unknown }).isProblemError !== true) return null
  const message = (err as { message?: unknown }).message
  return typeof message === 'string' && message.trim() !== '' ? message : null
}

export class ProblemError extends Error {
  readonly status: number
  readonly isProblemError: boolean

  constructor(status: number, body: unknown) {
    const problem = body as ProblemDetails | null
    super(problemMessage(problem, `Request failed: ${status}`))
    this.status = status
    this.isProblemError = hasProblemProse(problem)
  }
}

export function apiErrorMessage(err: unknown, fallback: string, concurrencyText: string): string {
  if (err !== null && typeof err === 'object' && (err as { isConcurrencyConflict?: unknown }).isConcurrencyConflict === true) {
    return concurrencyText
  }
  return errorDetail(err) ?? fallback
}
