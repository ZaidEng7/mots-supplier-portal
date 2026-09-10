import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { Stepper } from './Stepper'
import { FactList } from './FactList'

const LABELS = { done: 'Done', current: 'Current', todo: 'Not started' } as const

/**
 * The rail's answer to "how far along is this", which the tender workspace used to give as a wrapping
 * row of status chips - every stage the same size, every stage equally loud, and the one the tender is
 * actually at findable only by comparing tones.
 */
describe('Stepper says where a thing stands', () => {
  const steps = [
    { key: 'Draft', label: 'Draft', state: 'done' as const },
    { key: 'Published', label: 'Published', state: 'current' as const },
    { key: 'Awarded', label: 'Awarded', state: 'todo' as const },
  ]

  it('marks exactly one step as the current one', () => {
    render(<Stepper steps={steps} label="Lifecycle stages" stateLabels={LABELS} />)

    const list = screen.getByLabelText('Lifecycle stages')
    const current = within(list).getAllByRole('listitem', { current: 'step' })

    expect(current).toHaveLength(1)
    expect(current[0]).toHaveTextContent('Published')
  })

  /**
   * The denominator. `aria-current` on every row, or on none, would satisfy a test that only asked
   * whether the current row can be found - and both are exactly the failure this component exists to
   * fix, which is a tracker where nothing stands out.
   */
  it('marks the other steps as neither current nor equal', () => {
    render(<Stepper steps={steps} label="Lifecycle stages" stateLabels={LABELS} />)

    const rows = screen.getAllByRole('listitem')
    expect(rows).toHaveLength(3)
    expect(rows.filter((r) => r.getAttribute('aria-current') === 'step')).toHaveLength(1)
  })

  /**
   * Not colour alone. The marks differ in fill as well as hue, but a mark is decorative markup; the
   * state has to reach a screen reader as a word, and this is that word.
   */
  it('names each state in text, not only in a colour', () => {
    render(<Stepper steps={steps} label="Lifecycle stages" stateLabels={LABELS} />)

    const list = screen.getByLabelText('Lifecycle stages')
    expect(within(list).getByText('Done:')).toBeInTheDocument()
    expect(within(list).getByText('Current:')).toBeInTheDocument()
    expect(within(list).getByText('Not started:')).toBeInTheDocument()
  })

  it('takes its state words from the caller, so they are translated', () => {
    render(
      <Stepper
        steps={steps}
        label="مراحل دورة الحياة"
        stateLabels={{ done: 'مكتملة', current: 'الحالية', todo: 'لم تبدأ' }}
      />,
    )

    expect(screen.getByText('الحالية:')).toBeInTheDocument()
    expect(screen.queryByText('Current:')).toBeNull()
  })
})

/**
 * The counts a reader wants before deciding whether to read the page. Every one of these was already
 * on the tender workspace and every one had to be found by scrolling to its card and counting rows.
 */
describe('FactList pairs a label with its number', () => {
  const facts = [
    { key: 'invited', label: 'Invited', value: '7' },
    { key: 'bids', label: 'Bids received', value: '4' },
  ]

  it('renders each fact as a term and its description', () => {
    const { container } = render(<FactList facts={facts} />)

    expect(container.querySelectorAll('dt')).toHaveLength(2)
    expect(container.querySelectorAll('dd')).toHaveLength(2)
    expect(screen.getByText('Invited')).toBeInTheDocument()
    expect(screen.getByText('7')).toBeInTheDocument()
  })

  /**
   * The pairing is the whole point: a screen reader announces a `dd` with the `dt` before it, so a
   * list that rendered the labels and the values as two separate runs would read as four unrelated
   * strings. This asserts they alternate.
   */
  it('keeps every value immediately after its own label', () => {
    const { container } = render(<FactList facts={facts} />)

    const cells = [...container.querySelectorAll('dt, dd')].map((el) => `${el.tagName}:${el.textContent}`)
    expect(cells).toEqual(['DT:Invited', 'DD:7', 'DT:Bids received', 'DD:4'])
  })
})
