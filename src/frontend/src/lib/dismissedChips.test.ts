// SCR-120's dismissible action chips, and where a dismissal is remembered.
//
// A dismissal is remembered for this session, and dismissing one chip leaves the others alone - the control that a dismissal is
// per-chip rather than a switch that silences the strip.
//
// The last test is the property that makes per-session the right choice: a chip dismissed while a document is still expiring
// comes BACK. Dismissing it forever would silence the one warning that keeps a supplier compliant, and nothing would ever
// raise it again.

import { afterEach, describe, expect, it } from 'vitest'
import { clearDismissed, dismiss, isDismissed } from './dismissedChips'

describe('dismissedChips', () => {
  afterEach(() => clearDismissed())

  it('remembers a dismissal for this session', () => {
    expect(isDismissed('expiringDocuments')).toBe(false)

    dismiss('expiringDocuments')

    expect(isDismissed('expiringDocuments')).toBe(true)
  })

  it('dismissing one chip leaves the others alone', () => {
    dismiss('expiringDocuments')

    expect(isDismissed('awardOffers')).toBe(false)
  })

  it('a new session starts with nothing dismissed', () => {
    dismiss('expiringDocuments')
    sessionStorage.clear()

    expect(isDismissed('expiringDocuments')).toBe(false)
  })
})
