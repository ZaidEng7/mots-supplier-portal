import { afterEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import i18n from '../../i18n/config'
import { ArabicStory } from './rtlStoryDecorator'

/**
 * The decorator that lets a chart be looked at in Arabic.
 *
 * <p>Tested rather than excluded from coverage. It would have been easy to claim it as a Storybook
 * fixture and waive it - the file next door is a `.stories.tsx` and those are waived by name - but the
 * claim would have been weaker than the test: this thing changes global i18n state and puts it back,
 * and "puts it back" is exactly the kind of promise that quietly stops being true. A story that leaked
 * Arabic into the next story would make every screenshot after it wrong, and nothing would say so.</p>
 */
afterEach(async () => {
  await i18n.changeLanguage('en')
  document.documentElement.setAttribute('dir', 'ltr')
})

describe('the Arabic story decorator', () => {
  it('renders its children in Arabic, right to left', async () => {
    render(<ArabicStory><p>محتوى</p></ArabicStory>)

    await waitFor(() => expect(screen.getByText('محتوى')).toBeInTheDocument())
    expect(i18n.language).toBe('ar')
    expect(document.documentElement.getAttribute('dir')).toBe('rtl')
    expect(screen.getByText('محتوى').closest('[dir="rtl"]')).not.toBeNull()
  })

  it('holds the children back until the catalogue has actually switched', async () => {
    // recharts measures on first render and does not re-measure on a language change, so a chart drawn
    // in English and relabelled in Arabic keeps English geometry - and hides the very defect the
    // decorator exists to show.
    await i18n.changeLanguage('en')
    const { container } = render(<ArabicStory><p>محتوى</p></ArabicStory>)

    expect(container.textContent).toBe('')
    await waitFor(() => expect(screen.getByText('محتوى')).toBeInTheDocument())
  })

  it('puts the language and the direction back when it unmounts', async () => {
    await i18n.changeLanguage('en')
    const { unmount } = render(<ArabicStory><p>محتوى</p></ArabicStory>)
    await waitFor(() => expect(i18n.language).toBe('ar'))

    unmount()

    await waitFor(() => expect(i18n.language).toBe('en'))
    expect(document.documentElement.getAttribute('dir')).toBe('ltr')
  })
})
