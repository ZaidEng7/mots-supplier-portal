import { useTranslation } from 'react-i18next'
import { Badge } from './Badge'

/**
 * The five state machines that render as a chip in the product today. `erpSync` and `invitation`
 * are carried here too because they reach the screen the same way, even though their labels are
 * not fully specified (see `LABELLED_MACHINES` below).
 */
export type StatusMachine =
  | 'onboarding' | 'document' | 'rfq' | 'proposal' | 'evaluation' | 'award'
  | 'erpSync' | 'invitation'

/**
 * Machines whose complete label set exists in UX-WRITING.md §7 and is therefore transcribed into
 * `i18n/config.ts` under `status.*`. The coverage test asserts every member of these four resolves
 * in BOTH locales.
 *
 * <p>Deliberately excluded, and reported rather than filled in:</p>
 * <ul>
 *   <li><b>invitation</b> - §7 has no invitation-status table at all. Invited / Viewed /
 *       Responding / Submitted / Declined have no authored AR or EN label anywhere in the docs.</li>
 *   <li><b>erpSync</b> - §7.6 lists "Sync pending / Synced / Sync failed" as display strings, not
 *       keyed to the four enum members the code has (NotRequested / Requested / Synced / Failed).
 *       Which enum member "Sync pending" names is a guess, so it is not made here.</li>
 * </ul>
 */
export const LABELLED_MACHINES = ['onboarding', 'document', 'rfq', 'proposal', 'evaluation', 'award'] as const

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

/**
 * Tone per state. DESIGN-SYSTEM.md §6.15 ("every domain state renders as a chip pairing
 * color + icon + label") tabulates tones for a subset of states only - RFQ Published/Open,
 * UnderEvaluation, Awarded, Cancelled; Proposal Submitted; and the onboarding/document rows. The
 * states it does not name keep the tone the page that rendered them was already using, so this
 * change is a consolidation of existing behaviour rather than a re-colouring of the product.
 * Anything still unnamed falls to `neutral`.
 */
const TONES: Partial<Record<StatusMachine, Record<string, Tone>>> = {
  // §6.15 tabulates onboarding and document tones explicitly - the only two machines it covers in
  // full - so these are the doc's, not inferred: Draft neutral, Under review info, Info requested
  // warning, Approved success, Rejected danger, Suspended warning; Required neutral, Approved
  // success, Expiring soon warning, Expired danger.
  onboarding: {
    Draft: 'neutral', UnderReview: 'info', InfoRequested: 'warning', Approved: 'success',
    Rejected: 'danger', Suspended: 'warning', Active: 'success', Deactivated: 'danger',
  },
  // PendingScan/ScanRejected tones migrated from OnboardingPage's own DOC_STATE_TONE map, which
  // this replaces; §6.15 does not tabulate them (it has no row for either state).
  document: {
    // §6.15 tabulates "Required neutral" - a required document nobody has uploaded yet is the
    // resting state of the onboarding form, not a problem. `Missing` is what it BECOMES after a
    // failed submit attempt, and §6.15 has no row for it: danger is a choice, matching the tone
    // every other blocking-validation state in the product already uses.
    Required: 'neutral', Missing: 'danger',
    PendingScan: 'info', Uploaded: 'success', UnderReview: 'info', Approved: 'success',
    Rejected: 'danger', ScanRejected: 'danger', ExpiringSoon: 'warning', Expired: 'danger',
  },
  rfq: {
    Published: 'success', SubmissionOpen: 'success',
    UnderEvaluation: 'info', Awarded: 'brand', Completed: 'success', Cancelled: 'danger',
  },
  proposal: {
    Submitted: 'info', Awarded: 'brand', Withdrawn: 'danger',
    Declined: 'danger', NotSelected: 'warning',
  },
  evaluation: { Finalized: 'success', Consolidated: 'info' },
  award: { Awarded: 'success', Rejected: 'danger' },
  erpSync: { Synced: 'success', Failed: 'danger', Requested: 'info' },
  invitation: { Declined: 'danger', Submitted: 'success' },
}

interface StatusChipProps {
  machine: StatusMachine
  /** The raw enum value as the API returns it, e.g. `"SubmissionOpen"`. */
  value: string
  /**
   * Overrides the state's own tone. Exists for one real case: the EPIC-13 workspace stage tracker,
   * where the chip's colour conveys PROGRESS (current / completed / upcoming), not the semantics of
   * the state itself - a completed `Cancelled` stage would otherwise turn the whole tracker red.
   * The label still comes from §7, which is the point of routing it through here at all.
   */
  tone?: Tone
}

/**
 * T2-33: the single path by which a domain state becomes a rendered string.
 *
 * <p>Before this, `{rfq.state}`, `{award.state}`, `{proposal.state}`, `{inv.status}` and the
 * evaluation states were interpolated straight into `Badge` children on seven screens, so an
 * Arabic user read the raw English identifier. Routing every one through here means a new state
 * gets a label in one place, and the coverage test fails if it does not get one at all.</p>
 *
 * <p>Falls back to the raw value when a machine has no authored labels (invitation, erpSync) -
 * visibly unchanged from today's behaviour, deliberately not papered over with an invented
 * translation.</p>
 */
export function StatusChip({ machine, value, tone }: StatusChipProps) {
  const { t } = useTranslation()
  const key = `status.${machine}.${value}`
  const label = t(key)
  return <Badge tone={tone ?? TONES[machine]?.[value] ?? 'neutral'}>{label === key ? value : label}</Badge>
}
