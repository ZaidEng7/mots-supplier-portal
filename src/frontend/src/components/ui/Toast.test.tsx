import { afterEach, describe, expect, it } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import i18n from '../../i18n/config'
import { ToastProvider, useToast } from './Toast'

/**
 * The toast can be swiped towards the edge it sits on, in both languages.
 *
 * <p>No react-i18next mock here, deliberately: the whole point of the assertion is that the real
 * language decides the direction, and a mock would decide it instead.</p>
 *
 * <p><b>Why this is worth a test.</b> The viewport is pinned with a logical `end-4`, so the toast sits
 * on the right in English and on the LEFT in Arabic - but the swipe direction was the literal "right"
 * for both. In Arabic that asked a person to drag the toast away from the edge it was resting against,
 * across the screen, and the enter animation this change adds would have travelled in from the opposite
 * side to the one it lands on. The visible placement flips and the gesture did not, which is the kind of
 * RTL defect that survives because nobody swipes a toast in a screenshot.</p>
 */
function Emit() {
  const { notify } = useToast()
  return <button onClick={() => notify({ title: 'Saved', kind: 'success' })}>emit</button>
}

afterEach(async () => {
  await act(async () => {
    await i18n.changeLanguage('en')
  })
})

describe('the toast is swiped towards the edge it sits on', () => {
  it.each([
    ['en', 'right'],
    ['ar', 'left'],
  ])('in %s it swipes %s', async (language, direction) => {
    await act(async () => {
      await i18n.changeLanguage(language)
    })
    render(
      <ToastProvider>
        <Emit />
      </ToastProvider>,
    )

    await act(async () => {
      screen.getByRole('button', { name: 'emit' }).click()
    })

    // Radix puts the provider's swipeDirection on the root element, which is how this is observable at
    // all without synthesising a pointer drag.
    expect(screen.getByText('Saved').closest('[data-swipe-direction]')).toHaveAttribute(
      'data-swipe-direction',
      direction,
    )
  })
})
