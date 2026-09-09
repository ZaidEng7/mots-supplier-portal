import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Input } from './Input'

/**
 * Task #21: the invalid-state border color (--color-danger-solid, fixed for dark-mode AA
 * contrast this task) is set in three places - the base style, onFocus, and onBlur - and only
 * the base style had any test exercising it before this. Blur specifically, since that's the
 * state a field is actually shown in most of the time it's marked invalid (focus is transient).
 */
describe('Input invalid state', () => {
  it('keeps the invalid border color after a blur', async () => {
    render(<Input invalid aria-label="probe" defaultValue="bad value" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()

    expect(input.style.borderColor).toBe('var(--color-danger-solid)')
  })

  it('restores the normal border color on blur when not invalid', async () => {
    render(<Input invalid={false} aria-label="probe" defaultValue="fine" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()

    expect(input.style.borderColor).toBe('var(--color-border-input)')
  })
})

/**
 * DESIGN-SYSTEM.md §6.2 requires a disabled and a read-only state, and this component paints its own
 * background and colour inline — which beats the browser's own disabled rendering. So `disabled` was set
 * on every field of a submitted onboarding application and changed nothing anybody could see.
 */
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
    // The control: if the assertions above matched every input, they would pass forever.
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
    expect(input.style.border).toBe('1px solid transparent')
    // The value is still the primary colour: read-only means "not editable here", not "inactive".
    expect(input.style.color).toBe('var(--color-text-primary)')
  })

  it('a disabled field takes no focus ring, and a read-only one returns to no border after blur', async () => {
    const { rerender } = render(<Input readOnly aria-label="probe" />)
    const input = screen.getByLabelText('probe')

    await userEvent.click(input)
    await userEvent.tab()
    expect(input.style.borderColor).toBe('transparent')

    rerender(<Input disabled aria-label="probe" />)
    expect(input.style.boxShadow).not.toBe('var(--focus-ring)')
  })
})

