import { describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { render, screen } from '@testing-library/react'
import { BarChart } from './BarChart'

// No react-i18next mock here, deliberately: BarChart reads RTL_LANGUAGES from the i18n module, and a
// partial mock of react-i18next breaks that module's own initReactI18next import. The real catalogue is
// loaded in tests, so assertions use the real English string.

// Recharts measures its container, and jsdom reports every element as 0x0, so ResponsiveContainer
// renders nothing without this. Stubbing the measurement is the standard way to test recharts under
// jsdom; the assertions below are about which data reaches the chart, not about pixels.
vi.mock('recharts', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('recharts')
  // The real ResponsiveContainer clones its child with measured width/height. jsdom reports every
  // element as 0x0, so it hands down zeros and recharts draws nothing. This clones with real numbers,
  // which is the only reason a bar exists to assert on.
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) =>
      cloneElement(children, { width: 600, height: 220 }),
  }
})

describe('BarChart', () => {
  it('says so, rather than drawing an empty axis, when every value is withheld', () => {
    // D-57 withholds commercial values outside a demonstration environment. A chart of three nulls must
    // not render as three bars of height zero: that asserts nothing was awarded, which is a different
    // and false claim.
    render(<BarChart data={[{ key: 'Jan', value: null }, { key: 'Feb', value: null }]} valueLabel="Value" />)

    expect(screen.getByText('No figures available to chart. The table below carries what there is.')).toBeInTheDocument()
  })

  it('draws the values it has and leaves out the ones it does not', () => {
    const { container } = render(
      <BarChart data={[{ key: 'Jan', value: 10 }, { key: 'Feb', value: null }, { key: 'Mar', value: 30 }]} valueLabel="Value" />,
    )

    // Two bars, not three. The withheld month keeps its place in the table beneath, not on the axis.
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(2)
  })

  it('is hidden from assistive technology, because the table beneath is the accessible copy', () => {
    // Not an oversight. Recharts emits an SVG that a screen reader announces as a stream of unrelated
    // numbers; every chart in this product sits directly above the table it draws.
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)

    expect(container.querySelector('[aria-hidden="true"]')).toBeInTheDocument()
  })

  it('is inert to the keyboard as well as to a screen reader', () => {
    // Half-hidden is worse than not hidden. Recharts emits <svg role="application" tabindex="0"> by
    // default, so a keyboard user could tab into an element inside an aria-hidden wrapper and reach
    // something their screen reader will not describe. axe's aria-hidden-focus rule caught this on the
    // ministry analytics route; this is the unit-level guard so it cannot come back.
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)

    // tabindex="-1" is fine and recharts uses it: it removes an element from the tab order rather than
    // adding it. What must not exist is a tabbable one, which is what axe's rule is about.
    const tabbable = [...container.querySelectorAll('[tabindex]')]
      .filter((el) => Number(el.getAttribute('tabindex')) >= 0)

    expect(tabbable).toEqual([])
    expect(container.querySelector('[role="application"]')).toBeNull()
  })

  it('carries no colour of its own', () => {
    // The dataviz pass found the brand teal family sits below the chroma floor for categorical identity,
    // so these charts use one hue for magnitude and carry identity in the label and the table. A literal
    // colour here would also fail tokenConformance - this asserts the reason, not just the rule.
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }, { key: 'Feb', value: 20 }]} valueLabel="Value" />)
    const fills = [...container.querySelectorAll('.recharts-bar-rectangle path')].map((p) => p.getAttribute('fill'))
    expect(new Set(fills).size).toBe(1)
  })

  /**
   * Ranked is for a comparison of unordered things, and its whole claim is that the biggest is first.
   * A mode that drew the data in its given order would look identical on already-sorted fixtures, which
   * is exactly how a sorting bug survives.
   */
  it('puts the biggest first when ranked, whatever order it was given', () => {
    const { container } = render(
      <BarChart
        orientation="ranked"
        valueLabel="Awards"
        data={[
          { key: 'small', value: 3 },
          { key: 'largest', value: 90 },
          { key: 'middle', value: 40 },
        ]}
      />,
    )

    const ticks = [...container.querySelectorAll('.recharts-cartesian-axis-tick-value')].map((el) => el.textContent)
    expect(ticks).toEqual(['largest', 'middle', 'small'])

    // And the value is written at the end of its own bar rather than read off a scale, which is the
    // other half of what ranked means.
    const labels = [...container.querySelectorAll('.recharts-label')].map((el) => el.textContent)
    expect(labels).toEqual(['90', '40', '3'])
  })

  /**
   * The denominator for the mode itself: columns must NOT reorder. Months are read along time, and a
   * chart that sorted them would be drawing a different fact from the one the table beneath carries.
   */
  it('leaves a column chart in the order it was given', () => {
    const { container } = render(
      <BarChart
        valueLabel="Awards"
        data={[
          { key: 'Jan', value: 3 },
          { key: 'Feb', value: 90 },
          { key: 'Mar', value: 40 },
        ]}
      />,
    )

    // Both axes emit ticks, so this keeps only the ones that are month names - the numeric scale on the
    // other axis is not what this asserts.
    const months = [...container.querySelectorAll('.recharts-cartesian-axis-tick-value')]
      .map((el) => el.textContent)
      .filter((text) => ['Jan', 'Feb', 'Mar'].includes(text ?? ''))
    expect(months).toEqual(['Jan', 'Feb', 'Mar'])
  })
})
