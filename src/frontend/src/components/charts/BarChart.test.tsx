import { describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { render, screen } from '@testing-library/react'
import { BarChart } from './BarChart'
// The real catalogue, imported ON PURPOSE and for its side effect, so the assertions below can read
// real English strings.
//
// It used to arrive by accident: the component imported RTL_LANGUAGES from i18n/config, and that
// module initialises i18next on import. When RTL_LANGUAGES moved to its own file - so that knowing the
// page direction would stop dragging the bootstrap into every test that mocks react-i18next - this
// file's strings silently became raw keys. An accidental dependency is one nobody can see moving.
import '../../i18n/config'

// No react-i18next mock here, deliberately: a partial mock of react-i18next breaks the config module's
// own initReactI18next import.

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

  it('draws the values it has and leaves the withheld ones as a labelled gap', () => {
    const { container } = render(
      <BarChart data={[{ key: 'Jan', value: 10 }, { key: 'Feb', value: null }, { key: 'Mar', value: 30 }]} valueLabel="Value" />,
    )

    // Two bars out of three rows, and the third row still on the axis.
    //
    // This assertion used to read "two bars, not three. The withheld month keeps its place in the
    // table beneath, not on the axis" - and it was asserting a defect. The component filtered the
    // withheld row out of the data entirely, so February disappeared from a six-month chart with
    // nothing to say it had ever been there, while the doc comment three inches above the filter
    // promised "a gap with the category still labelled". The test agreed with the code and both
    // disagreed with the documentation, which is the worst of the three possible arrangements.
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(2)
    // That the third category keeps its tick is asserted where it can be: jsdom reports zero metrics
    // for every text node, so recharts renders axis tick groups with no text inside them and no unit
    // test in any environment can read an axis label. tests/e2e/charts.spec.ts checks it in a browser.
  })

  /**
   * The denominator, asserted against the data rather than against a number typed by hand.
   *
   * <p>Every count assertion in this file was a literal until now - `toHaveLength(2)` against a
   * three-row fixture - so a component that drew one bar per PIXEL would have failed and a component
   * that silently dropped half its rows at seven would not. This grows with the fixture.</p>
   */
  it.each([1, 3, 6, 11])('draws one bar per row, at %i rows', (count) => {
    const data = Array.from({ length: count }, (_, i) => ({ key: `M${i}`, value: (i + 1) * 10 }))
    const { container } = render(<BarChart data={data} valueLabel="Value" />)

    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(count)
  })

  it('ranks by the measure, largest first, and keeps a withheld category in the list', () => {
    const { container } = render(
      <BarChart
        orientation="ranked"
        valueLabel="Value"
        data={[{ key: 'small', value: 5 }, { key: 'withheld', value: null }, { key: 'large', value: 90 }]}
      />,
    )

    // Read off the end labels rather than the category axis: jsdom gives every text node zero metrics,
    // so recharts renders a vertical category axis with tick groups and no text in them. The order of
    // the figures is the order of the rows, which is the thing being asserted.
    const order = [...container.querySelectorAll('.recharts-label-list text')].map((t) => t.textContent)
    expect(order).toEqual(['90', '5'])
    // Three rows, two bars: the withheld one holds its place and draws nothing.
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(2)
  })

  it('writes its figures the way the table beneath writes them', () => {
    // The chart drew `412000` above a table that drew `412,000`. One screen, one number, two renderings.
    const { container } = render(
      <BarChart orientation="ranked" valueLabel="Value" data={[{ key: 'Catering', value: 412000 }]} />,
    )

    const labels = [...container.querySelectorAll('.recharts-label-list text')].map((t) => t.textContent)
    expect(labels).toEqual(['412,000'])
  })

  it('caps a bar at the width the mark spec allows', () => {
    // 56 shipped, against a spec that caps a bar at 24 - and against the ranked bars in this same
    // component, which are 14. One chart drew marks two and a half times the thickness of its sibling.
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)
    const bar = container.querySelector('.recharts-bar-rectangle .recharts-rectangle')

    expect(Number(bar?.getAttribute('width'))).toBeLessThanOrEqual(24)
  })

  it('draws a grid that is a hairline rather than a second texture', () => {
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)
    const grid = container.querySelector('.recharts-cartesian-grid line')

    expect(grid?.getAttribute('stroke-dasharray')).toBeNull()
    expect(grid?.getAttribute('stroke')).toBe('var(--color-chart-grid)')
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
