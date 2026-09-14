// The status-label coverage test, and the chip's own rendering.
//
// THE ENUM MEMBERS are listed here as data, because the TypeScript union types they mirror are erased at runtime.
// This list IS the denominator: if a new state is added to api/*.ts and not added here, the reviewer sees an untested
// state; if it is added here without a UX-WRITING §7 label being transcribed into i18n/config.ts, the coverage test
// fails. That second half is the regression this test exists for - T2-33's acceptance: "must fail if a new state is
// added later without a label".
//
// The supplier machine carries two code enums, because UX-WRITING §7.1 groups onboarding and lifecycle in one table:
// SupplierOnboardingState's nine members plus the three rendered SupplierLifecycleState ones. They are sourced from
// the backend enums in Domain/Suppliers rather than from a frontend union, because onboardingState and lifecycleState
// are typed as bare strings in api/*.ts. The document machine lists the DocumentState members §7.2 actually labels.
// A-9's two terminal proposal states are in there too: both terminal, and both needing a label, because a supplier
// list showing a raw enum name is the failure this coverage test exists to catch.
//
// THREE LISTS SIT BESIDE IT, and each exists to keep a distinction visible.
//
// UNLABELLED is enum members that exist in code but have NO §7 row. Labels are not authored for these, because the
// documentation is the single source - §7: "these are the single source for chip text" - so inventing one here would
// put an unreviewed Arabic string in front of a user. They are asserted to have NO label, deliberately, so the test
// fails in BOTH directions: adding a state without a label fails the coverage test, and authoring a label for one of
// these fails the other one, forcing it to be moved into the members list rather than silently diverging from the
// doc. The list is small and explicit so it cannot rot unnoticed.
//
// The second list is members that DO have a label in code but NO §7 row behind it. These were authored before this
// batch, in OnboardingPage's own onboarding.docState namespace, and were carried over verbatim when that namespace
// was folded into status.document - migrated, not written here. Deleting them would regress the chip to raw English,
// so they are kept and reported as a documentation gap, asserted present so the migration cannot be silently undone,
// and listed separately from the members so nobody mistakes them for transcription. `Missing` joins them for the same
// reason and by the same rule: a label the code authors because the product needs one, with no §7 row behind it. It
// is not a DocumentState member either - it is the display state a required-but-absent document takes on after a
// failed submit attempt, defined by the product owner, and reported as a documentation gap.
//
// The third is labels that ARE transcribed from §7 but are not members of any code enum. `Required` is §7.2's first
// row and SCR-106 lists it first in that screen's StatusBadge set - "Required / Uploaded / UnderReview / Approved /
// Rejected" - but DocumentState has no Required member, because a required document type with nothing uploaded has no
// document and therefore no state. It is listed separately so the distinction between "transcribed from the doc" and
// "a state the enum has" stays visible.
//
// The coverage suite then asserts, per machine and per locale: every member has a label; no label exists for a state
// the enums do not have, which catches a stale or invented entry; the pre-existing labels with no §7 row survive the
// migration; the §7-transcribed labels with no enum member resolve; and the reported documentation gaps STAY
// unlabelled until the documentation gains a row - if someone authors one locally, that fails and tells them to move
// it into the members list, which is the moment to check the doc actually says so.
//
// THE RENDERING tests are that the chip renders the §7 label rather than the raw enum value, in English and in
// Arabic, and that it falls back to the raw value for a machine with no authored labels. Invitation statuses have no
// §7 table, reported as a documentation gap, and the chip must degrade to the raw value rather than render an empty
// pill or a missing-key string - so that test documents the fallback as intended behaviour, and it is not mistaken
// for a bug later.

import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import i18n from '../../i18n/config'
import { StatusChip, LABELLED_MACHINES } from './StatusChip'
import type { StatusMachine } from './StatusChip'

const MEMBERS: Record<(typeof LABELLED_MACHINES)[number], readonly string[]> = {
  onboarding: [
    'Draft', 'EmailVerified', 'ProfileInProgress', 'Submitted', 'UnderReview', 'InfoRequested',
    'Resubmitted', 'Approved', 'Rejected', 'Active', 'Suspended', 'Deactivated',
  ],
  document: ['Uploaded', 'UnderReview', 'Approved', 'Rejected', 'ExpiringSoon', 'Expired'],
  rfq: [
    'Draft', 'InternalReview', 'Approved', 'Published', 'SubmissionOpen', 'SubmissionClosed',
    'UnderEvaluation', 'Clarification', 'Shortlisting', 'Recommendation', 'AwardApproval',
    'Awarded', 'Completed', 'Cancelled',
  ],
  proposal: [
    'Draft', 'Submitted', 'Withdrawn', 'UnderReview', 'ClarificationRequested', 'Revised',
    'Shortlisted', 'NotSelected', 'AwardOffered', 'Awarded', 'Declined',
    'Lapsed', 'Cancelled',
  ],
  evaluation: ['NotStarted', 'Assigned', 'InProgress', 'EvaluatorSubmitted', 'Consolidated', 'Finalized'],
  award: ['Recommended', 'PendingApproval', 'Approved', 'Rejected', 'Awarded'],
}

const UNLABELLED_PENDING_DOCS: Partial<Record<(typeof LABELLED_MACHINES)[number], readonly string[]>> = {
  onboarding: ['None'], // SupplierLifecycleState.None - no §7.1 row, renders the raw fallback
}

const CARRIED_OVER_WITHOUT_DOC_ROW: Partial<Record<(typeof LABELLED_MACHINES)[number], readonly string[]>> = {
  document: ['PendingScan', 'ScanRejected', 'Missing'],
}

const TRANSCRIBED_NOT_ENUM_MEMBERS: Partial<Record<(typeof LABELLED_MACHINES)[number], readonly string[]>> = {
  document: ['Required'],
}

const resources = i18n.options.resources as Record<string, { translation: Record<string, unknown> }>

function labelFor(locale: string, machine: string, member: string): unknown {
  const status = resources[locale].translation.status as Record<string, Record<string, string>>
  return status?.[machine]?.[member]
}

describe('status label coverage (UX-WRITING.md §7)', () => {
  for (const machine of LABELLED_MACHINES) {
    for (const locale of ['ar', 'en'] as const) {
      it(`${machine}: every member has an ${locale} label`, () => {
        const missing = MEMBERS[machine].filter((m) => {
          const v = labelFor(locale, machine, m)
          return typeof v !== 'string' || v.length === 0
        })

        expect(missing, `${machine} members with no ${locale} label: ${missing.join(', ')}`).toEqual([])
      })
    }
  }

  it('has no label for a state the enums do not have - catches a stale or invented entry', () => {
    for (const machine of LABELLED_MACHINES) {
      const en = resources.en.translation.status as Record<string, Record<string, string>>
      const known = [
        ...MEMBERS[machine],
        ...(CARRIED_OVER_WITHOUT_DOC_ROW[machine] ?? []),
        ...(TRANSCRIBED_NOT_ENUM_MEMBERS[machine] ?? []),
      ]
      const extra = Object.keys(en[machine]).filter((k) => !known.includes(k))
      expect(extra, `${machine} has labels for unknown states: ${extra.join(', ')}`).toEqual([])
    }
  })

  for (const [machine, members] of Object.entries(CARRIED_OVER_WITHOUT_DOC_ROW)) {
    for (const locale of ['ar', 'en'] as const) {
      it(`${machine}: pre-existing labels with no §7 row survive the migration in ${locale}`, () => {
        const lost = members.filter((m) => typeof labelFor(locale, machine, m) !== 'string')

        expect(
          lost,
          `${machine} lost ${locale} labels for ${lost.join(', ')} - these were migrated from ` +
          'onboarding.docState and removing them regresses the chip to raw English',
        ).toEqual([])
      })
    }
  }

  for (const [machine, members] of Object.entries(TRANSCRIBED_NOT_ENUM_MEMBERS)) {
    for (const locale of ['ar', 'en'] as const) {
      it(`${machine}: §7-transcribed labels with no enum member resolve in ${locale}`, () => {
        const missing = members.filter((m) => typeof labelFor(locale, machine, m) !== 'string')

        expect(missing, `${machine} has no ${locale} label for ${missing.join(', ')}`).toEqual([])
      })
    }
  }

  for (const [machine, members] of Object.entries(UNLABELLED_PENDING_DOCS)) {
    for (const locale of ['ar', 'en'] as const) {
      it(`${machine}: reported documentation gaps stay unlabelled in ${locale}`, () => {
        const authored = members.filter((m) => typeof labelFor(locale, machine, m) === 'string')

        expect(
          authored,
          `${machine} now has ${locale} labels for ${authored.join(', ')} - if UX-WRITING §7 added ` +
          'a row, move these into MEMBERS; if not, they were invented and must be removed',
        ).toEqual([])
      })
    }
  }
})

function renderChip(machine: StatusMachine, value: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <StatusChip machine={machine} value={value} />
    </I18nextProvider>,
  )
}

describe('StatusChip rendering', () => {
  it('renders the §7 label, not the raw enum value', async () => {
    await i18n.changeLanguage('en')
    renderChip('rfq', 'SubmissionOpen')

    expect(screen.getByText('Open for submissions')).toBeInTheDocument()
    expect(screen.queryByText('SubmissionOpen')).not.toBeInTheDocument()
  })

  it('renders the Arabic label under the ar locale', async () => {
    await i18n.changeLanguage('ar')
    renderChip('rfq', 'SubmissionOpen')

    expect(screen.getByText('مفتوح للتقديم')).toBeInTheDocument()
    await i18n.changeLanguage('en')
  })

  it('falls back to the raw value for a machine with no authored labels', async () => {
    await i18n.changeLanguage('en')
    renderChip('invitation', 'Responding')

    expect(screen.getByText('Responding')).toBeInTheDocument()
  })
})
