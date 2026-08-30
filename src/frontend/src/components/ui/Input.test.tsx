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
