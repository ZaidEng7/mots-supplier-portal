// T2-33: the single path by which a domain state becomes a rendered string.
//
// Before this, {rfq.state}, {award.state}, {proposal.state}, {inv.status} and the evaluation states were
// interpolated straight into Badge children on seven screens, so an Arabic user read the raw English identifier.
// Routing every one through here means a new state gets a label in one place, and the coverage test fails if it does
// not get one at all. It falls back to the raw value when a machine has no authored labels - which is now erpSync
// alone: EPIC-16 drafted the five invitation labels §7 never tabulated, because SCR-120 renders them as chips and a
// supplier reading "Responding" in English on an Arabic page was the visible end of that gap.
//
// THE MACHINES. Five state machines render as a chip in the product today. erpSync and invitation are carried here
// too, because they reach the screen the same way, even though their labels are not fully specified.
//
// The labelled set is the machines whose complete label set exists in UX-WRITING.md §7 and is therefore transcribed
// into i18n/config.ts under status.*; the coverage test asserts every member of those four resolves in BOTH locales.
// Two are deliberately excluded and reported rather than filled in. Invitation: §7 has no invitation-status table at
// all, so Invited, Viewed, Responding, Submitted and Declined have no authored Arabic or English label anywhere in
// the docs. erpSync: §7.6 lists "Sync pending / Synced / Sync failed" as display strings rather than keyed to the
// four enum members the code has - NotRequested, Requested, Synced, Failed - and which enum member "Sync pending"
// names is a guess, so it is not made here.
//
// THE TONES. DESIGN-SYSTEM.md §6.15 - "every domain state renders as a chip pairing color + icon + label" -
// tabulates tones for a subset of states only: RFQ Published and Open, UnderEvaluation, Awarded, Cancelled; Proposal
// Submitted; and the onboarding and document rows. The states it does not name keep the tone the page that rendered
// them was already using, so this is a consolidation of existing behaviour rather than a re-colouring of the
// product, and anything still unnamed falls to neutral.
//
// The onboarding and document tones ARE the document's rather than inferred, because they are the only two machines
// §6.15 covers in full: Draft neutral, Under review info, Info requested warning, Approved success, Rejected danger,
// Suspended warning; Required neutral, Approved success, Expiring soon warning, Expired danger. PendingScan and
// ScanRejected migrated from OnboardingPage's own DOC_STATE_TONE map, which this replaces, and §6.15 has no row for
// either. `Missing` is a choice: §6.15 tabulates "Required neutral" - a required document nobody has uploaded yet is
// the resting state of the onboarding form rather than a problem - and Missing is what it BECOMES after a failed
// submit attempt, so danger matches the tone every other blocking-validation state in the product already uses. A-9's
// two terminal proposal states take warning rather than danger: neither is the supplier's fault in the way a
// rejection is, since Lapsed is a deadline they missed and Cancelled is a decision taken above them.
//
// The tone OVERRIDE exists for one real case: the EPIC-13 workspace stage tracker, where the chip's colour conveys
// PROGRESS - current, completed, upcoming - rather than the semantics of the state itself, and a completed Cancelled
// stage would otherwise turn the whole tracker red. The label still comes from §7, which is the point of routing it
// through here at all.

import { useTranslation } from 'react-i18next'
import { Badge } from './Badge'

export type StatusMachine =
  | 'onboarding' | 'document' | 'rfq' | 'proposal' | 'evaluation' | 'award'
  | 'erpSync' | 'invitation'

export const LABELLED_MACHINES = ['onboarding', 'document', 'rfq', 'proposal', 'evaluation', 'award'] as const

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

const TONES: Partial<Record<StatusMachine, Record<string, Tone>>> = {
  onboarding: {
    Draft: 'neutral', UnderReview: 'info', InfoRequested: 'warning', Approved: 'success',
    Rejected: 'danger', Suspended: 'warning', Active: 'success', Deactivated: 'danger',
  },
  document: {
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
    Lapsed: 'warning', Cancelled: 'neutral',
  },
  evaluation: { Finalized: 'success', Consolidated: 'info' },
  award: { Awarded: 'success', Rejected: 'danger' },
  erpSync: { Synced: 'success', Failed: 'danger', Requested: 'info' },
  invitation: { Declined: 'danger', Submitted: 'success' },
}

interface StatusChipProps {
  machine: StatusMachine
  value: string
  tone?: Tone
}

export function StatusChip({ machine, value, tone }: StatusChipProps) {
  const { t } = useTranslation()
  const key = `status.${machine}.${value}`
  const label = t(key)
  return <Badge tone={tone ?? TONES[machine]?.[value] ?? 'neutral'}>{label === key ? value : label}</Badge>
}
