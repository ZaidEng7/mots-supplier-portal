// Three groups: the invalid border, the disabled and read-only states, and the hover state.
//
// THE INVALID BORDER is Task #21's. Its colour - --color-danger-solid, fixed for dark-mode AA contrast in that task -
// is set in three places, the base style, onFocus and onBlur, and only the base style had any test exercising it
// before this. Blur specifically, because that is the state a field is actually shown in most of the time it is
// marked invalid; focus is transient. The resting border moved into a custom property so a stylesheet rule can raise
// it on hover, and the behaviour is the same.
//
// THE DISABLED AND READ-ONLY STATES. DESIGN-SYSTEM.md §6.2 requires both, and this component paints its own
// background and colour inline, which beats the browser's own disabled rendering - so `disabled` was set on every
// field of a submitted onboarding application and changed nothing anybody could see. A disabled field must LOOK
// disabled rather than merely behave so, with the control beside it that an enabled field is not styled as disabled:
// if the assertions matched every input they would pass forever. Read-only drops the border rather than dimming the
// value, per §6.2, and the value stays the primary colour, because read-only means "not editable here" rather than
// "inactive". A disabled field takes no focus ring, and a read-only one returns to no border after blur.
//
// THE HOVER STATE is the one DESIGN-SYSTEM.md has always required and the component did not have: a resting border
// that visibly strengthens when a pointer is over an editable field.
//
// Why those tests read a stylesheet instead of hovering. jsdom applies no author CSS, so userEvent.hover would
// dispatch a pointer event and then assert nothing - the rule that does the work lives in index.css, outside the
// component. What a unit test CAN prove is that the two halves still meet: the element carries the hook the rule
// selects on, its border reads through the custom property the rule sets, and the rule itself is still present and
// still guarded. Break either half and hover silently stops working in the browser with every other test in this file
// green, which is exactly what happened before the property indirection existed. A literal borderColor would win over
// the rule's custom property and the hover would never be seen, which is the failure that asserts against. And the
// rule must not fire on a field that cannot be edited, or on a touch device where :hover sticks after a tap.

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Input } from './Input'

describe('Input invalid state', () => {
  it('keeps the invalid border color after a blur', async () => {
    render(<Input invalid aria-label="probe" defaultValue="bad value" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()

    expect(input.style.getPropertyValue('--input-border')).toBe('var(--color-danger-solid)')
  })

  it('restores the normal border color on blur when not invalid', async () => {
    render(<Input invalid={false} aria-label="probe" defaultValue="fine" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()

    expect(input.style.getPropertyValue('--input-border')).toBe('var(--color-border-input)')
  })
})

describe('Input disabled and read-only states', () => {
  it('a disabled field looks disabled, not merely behaves so', () => {
    render(<Input disabled aria-label="probe" defaultValue="locked" />)
    const input = screen.getByLabelText('probe')

    expect(input).toBeDisabled()
    expect(input.style.color).toBe('var(--color-text-disabled)')
    expect(input.style.backgroundColor).toBe('var(--color-bg-sunken)')
    expect(input.className).toContain('cursor-not-allowed')
  })

  it('an enabled field is not styled as disabled', () => {
    render(<Input aria-label="probe" defaultValue="editable" />)
    const input = screen.getByLabelText('probe')

    expect(input).toBeEnabled()
    expect(input.style.color).toBe('var(--color-text-primary)')
    expect(input.style.backgroundColor).toBe('var(--color-bg-surface)')
    expect(input.className).not.toContain('cursor-not-allowed')
  })

  it('read-only drops the border rather than dimming the value, per §6.2', () => {
    render(<Input readOnly aria-label="probe" defaultValue="a value you may read" />)
    const input = screen.getByLabelText('probe')

    expect(input).toHaveAttribute('readonly')
    expect(input.style.getPropertyValue('--input-border')).toBe('transparent')
    expect(input.style.color).toBe('var(--color-text-primary)')
  })

  it('a disabled field takes no focus ring, and a read-only one returns to no border after blur', async () => {
    const { rerender } = render(<Input readOnly aria-label="probe" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()
    expect(input.style.getPropertyValue('--input-border')).toBe('transparent')

    rerender(<Input disabled aria-label="probe" />)
    expect(input.style.boxShadow).not.toBe('var(--focus-ring)')
  })
})


describe('Input hover state', () => {
  const indexCss = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8')

  it('exposes the class and the custom property the stylesheet rule needs', () => {
    render(<Input aria-label="probe" />)
    const input = screen.getByLabelText('probe')

    expect(input.className).toContain('msp-input')
    expect(input.style.border).toBe('1px solid var(--input-border)')
    expect(input.style.getPropertyValue('--input-border')).toBe('var(--color-border-input)')
  })

  it('the rule exists, raises the border, and is guarded on all three states', () => {
    const rule = indexCss.match(/\.msp-input[^{]*\{[^}]*\}/)?.[0]
    expect(rule, 'no .msp-input hover rule in index.css').toBeDefined()

    expect(rule).toContain('--input-border: var(--color-border-strong)')
    expect(rule).toContain(':not(:disabled)')
    expect(rule).toContain(':not([readonly])')
    expect(indexCss).toContain('@media (hover: hover) and (pointer: fine)')
  })
})
