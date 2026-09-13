// Select accepted no `disabled` prop at all until this, which is why a submitted onboarding application still offered
// three working comboboxes - entity type, currency, and the phone's dialling code - on a form whose Save buttons had
// been removed. A person could change them and had nothing to commit them with.
//
// So: the control is refused and says so to assistive technology, with the control beside it that an enabled select is
// not styled as disabled - for the same reason as Input's, because an assertion that matches every trigger checks
// nothing.

import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Select } from './Select'

const OPTIONS = [
  { value: 'company', label: 'Company' },
  { value: 'establishment', label: 'Establishment' },
]

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
    render(<Select value="company" onValueChange={() => {}} options={OPTIONS} placeholder="Entity type" />)
    const trigger = screen.getByLabelText('Entity type')

    expect(trigger).toBeEnabled()
    expect(trigger.style.color).toBe('var(--color-text-primary)')
    expect(trigger.className).not.toContain('cursor-not-allowed')
  })
})
