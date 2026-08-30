import { describe, expect, it } from 'vitest'
import { hex } from 'wcag-contrast'

/**
 * Task #22/NFR-A11Y-002: "visible focus indicator" isn't satisfied by an indicator existing - it
 * has to actually be visible, which for a color-based ring means it clears the 3:1 UI-component
 * minimum (WCAG 2.4.7 non-text contrast) against the surface it appears on. Neither theme's focus
 * ring was checked before this: task #21 covered the DARK theme's token pairs, but the focus ring
 * itself (--focus-ring, tokens.css) wasn't one of the pairs in that audit's denominator - it's a
 * box-shadow value, not a text/border color token, so it fell outside that sweep's scope. This
 * closes that gap for both themes, not just the one task #21 already covered.
 */
describe('focus ring contrast', () => {
  it('light theme: brand-500 ring clears 3:1 against white bg-surface', () => {
    expect(hex('#1F8069', '#FFFFFF')).toBeGreaterThanOrEqual(3)
  })

  it('dark theme: brand-300 ring clears 3:1 against the dark bg-surface', () => {
    expect(hex('#6FBAA8', '#26241F')).toBeGreaterThanOrEqual(3)
  })
})
