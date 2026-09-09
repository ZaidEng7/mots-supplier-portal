import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Select } from './Select'

const OPTIONS = [
  { value: 'company', label: 'Company' },
  { value: 'establishment', label: 'Establishment' },
]

/**
 * `Select` accepted no `disabled` prop at all until this, which is why a submitted onboarding
 * application still offered three working comboboxes — entity type, currency, and the phone's dialling
 * code — on a form whose Save buttons had been removed. A person could change them and had nothing to
 * commit them with.
 */
describe('Select disabled state', () => {
  it('refuses the control and says so to assistive technology', () => {
    render(<Select value="company" onValueChange={() => {}} options={OPTIONS} placeholder="Entity type" disabled />)
    const trigger = screen.getByLabelText('Entity type')

    expect(trigger).toBeDisabled()
    expect(trigger.style.color).toBe('var(--color-text-disabled)')
    expect(trigger.style.backgroundColor).toBe('var(--color-bg-sunken)')
    expect(trigger.className).toContain('cursor-not-allowed')
  })

  it('an enabled select is not styled as disabled', () => {
    // The control, for the same reason as Input's: an assertion that matches every trigger checks nothing.
    render(<Select value="company" onValueChange={() => {}} options={OPTIONS} placeholder="Entity type" />)
    const trigger = screen.getByLabelText('Entity type')

    expect(trigger).toBeEnabled()
    expect(trigger.style.color).toBe('var(--color-text-primary)')
    expect(trigger.className).not.toContain('cursor-not-allowed')
  })
})
