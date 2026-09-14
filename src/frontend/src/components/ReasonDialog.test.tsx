// MSP-63: the mandatory-reason dialog shared by reject, suspend, reactivate and deactivate.
//
// The behaviour worth testing is not that it renders - it is that it CANNOT be submitted without a reason. BRULE-096
// makes the reason mandatory and the reason becomes the audit record, so a dialog that lets an empty or whitespace one
// through writes a record that explains nothing.
//
// The component only uses t() for labels, so returning the key keeps these assertions about behaviour rather than about
// translation strings, which are asserted in i18n's own right elsewhere.
//
// Confirmation is disabled until a reason is entered, and whitespace is still refused - the failure that guards against
// is that "   " is truthy and non-empty, and an audit record whose stated justification is three spaces is worse than
// one with none, because it looks answered. A real reason submits. A warning is shown when one is supplied and not when
// it is not.
//
// The last test starts the dialog empty on each mount, so a previous action cannot pre-fill the next. That was the
// defect found by opening the page rather than by any test: the dialog kept its reason across opens, so Deactivate
// pre-filled with the Suspend reason. The page fixes it by keying the dialog on the action, and this asserts the
// component's half - a fresh mount is empty.

import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReasonDialog } from './ReasonDialog'

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}))

describe('ReasonDialog', () => {
  const props = {
    open: true,
    onOpenChange: () => {},
    isLoading: false,
    title: 'review.suspend',
    confirmLabel: 'review.suspend',
    variant: 'danger' as const,
  }

  it('disables confirmation until a reason is entered', () => {
    render(<ReasonDialog {...props} onSubmit={() => {}} />)

    expect(screen.getByRole('button', { name: 'review.suspend' })).toBeDisabled()
  })

  it('still refuses whitespace, which would satisfy a naive length check', async () => {
    render(<ReasonDialog {...props} onSubmit={() => {}} />)

    await userEvent.type(screen.getByRole('textbox'), '   ')

    expect(screen.getByRole('button', { name: 'review.suspend' })).toBeDisabled()
  })

  it('submits the reason once a real one is entered', async () => {
    const onSubmit = vi.fn()
    render(<ReasonDialog {...props} onSubmit={onSubmit} />)

    await userEvent.type(screen.getByRole('textbox'), 'Sanctions screening hit')
    await userEvent.click(screen.getByRole('button', { name: 'review.suspend' }))

    expect(onSubmit).toHaveBeenCalledWith('Sanctions screening hit')
  })

  it('shows a warning when one is supplied, and none when it is not', () => {
    const { unmount } = render(
      <ReasonDialog {...props} onSubmit={() => {}} warning="review.deactivateWarning" />,
    )
    expect(screen.getByText('review.deactivateWarning')).toBeInTheDocument()
    unmount()

    render(<ReasonDialog {...props} onSubmit={() => {}} />)
    expect(screen.queryByText('review.deactivateWarning')).not.toBeInTheDocument()
  })

  it('starts empty on each mount, so a previous action cannot pre-fill the next', async () => {
    const first = render(<ReasonDialog {...props} onSubmit={() => {}} />)
    await userEvent.type(screen.getByRole('textbox'), 'Reason for the first action')
    first.unmount()

    render(<ReasonDialog {...props} onSubmit={() => {}} />)

    expect(screen.getByRole('textbox')).toHaveValue('')
  })
})
