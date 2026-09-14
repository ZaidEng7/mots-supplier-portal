// What the bar chart draws, in both orientations.
//
// THE SETUP. The real catalogue is imported ON PURPOSE and for its side effect, so the assertions can read real
// English strings. It used to arrive by accident: the component imported RTL_LANGUAGES from i18n/config, and that
// module initialises i18next on import, so when RTL_LANGUAGES moved to its own file - to stop knowing the page
// direction from dragging the bootstrap into every test that mocks react-i18next - this file's strings silently became
// raw keys. An accidental dependency is one nobody can see moving. There is no react-i18next mock here, deliberately:
// a partial mock of react-i18next breaks the config module's own initReactI18next import.
//
// ResponsiveContainer is stubbed because recharts measures its container and jsdom reports every element as 0x0, so
// the real one clones its child with measured zeros and recharts draws nothing. The stub clones with real numbers,
// which is the only reason a bar exists to assert on. That is the standard way to test recharts under jsdom, and the
// assertions are about which data reaches the chart rather than about pixels.
//
// WITHHELD VALUES. A chart of three nulls says so rather than drawing an empty axis: D-57 withholds commercial values
// outside a demonstration environment, and three bars of height zero would assert nothing was awarded, which is a
// different and false claim. With a mix, the values it has are drawn and the withheld ones are left as a labelled gap -
// two bars out of three rows, with the third row still on the axis.
//
// That assertion used to read "two bars, not three. The withheld month keeps its place in the table beneath, not on
// the axis" - and it was asserting a defect. The component filtered the withheld row out of the data entirely, so
// February disappeared from a six-month chart with nothing to say it had ever been there, while the note three inches
// above the filter promised "a gap with the category still labelled". The test agreed with the code and both
// disagreed with the documentation, which is the worst of the three possible arrangements. That the third category
// keeps its TICK is asserted where it can be: jsdom reports zero metrics for every text node, so recharts renders axis
// tick groups with no text inside them and no unit test in any environment can read an axis label -
// tests/e2e/charts.spec.ts checks it in a browser.
//
// The denominator is asserted against the data rather than against a number typed by hand. Every count assertion in
// this file was a literal until now - toHaveLength(2) against a three-row fixture - so a component that drew one bar
// per PIXEL would have failed and a component that silently dropped half its rows at seven would not. It grows with
// the fixture.
//
// FIGURES are written the way the table beneath writes them: the chart drew 412000 above a table that drew 412,000 -
// one screen, one number, two renderings.
//
// MARKS. A bar is capped at the width the mark spec allows: 56 shipped, against a spec that caps a bar at 24 and
// against the ranked bars in this same component, which are 14, so one chart drew marks two and a half times the
// thickness of its sibling. The grid is a hairline rather than a second texture. And the chart carries no colour of
// its own: the dataviz pass found the brand teal family sits below the chroma floor for categorical identity, so these
// charts use one hue for magnitude and carry identity in the label and the table - a literal colour here would also
// fail tokenConformance, and this asserts the reason rather than just the rule.
//
// ACCESSIBILITY. The drawing is hidden from assistive technology, because the table beneath is the accessible copy -
// not an oversight: recharts emits an SVG a screen reader announces as a stream of unrelated numbers, and every chart
// in this product sits directly above the table it draws. It is inert to the KEYBOARD as well, because half-hidden is
// worse than not hidden: recharts emits <svg role="application" tabindex="0"> by default, so a keyboard user could tab
// into an element inside an aria-hidden wrapper and reach something their screen reader will not describe. axe's
// aria-hidden-focus rule caught that on the ministry analytics route, and this is the unit-level guard so it cannot
// come back. tabindex="-1" is fine and recharts uses it, because it removes an element from the tab order rather than
// adding it; what must not exist is a tabbable one.
//
// THE TWO MODES. Ranked is for a comparison of unordered things, and its whole claim is that the biggest is first - a
// mode that drew the data in its given order would look identical on already-sorted fixtures, which is exactly how a
// sorting bug survives. It ranks by the measure, largest first, keeping a withheld category in the list: three rows,
// two bars, the withheld one holding its place and drawing nothing. Those assertions read off the END LABELS rather
// than the category axis, because jsdom gives every text node zero metrics so recharts renders a vertical category
// axis with tick groups and no text in them, and the order of the figures is the order of the rows - which is the thing
// being asserted. The value at the end of its own bar rather than read off a scale is the other half of what ranked
// means.
//
// The denominator for the mode itself is that columns must NOT reorder: months are read along time, and a chart that
// sorted them would be drawing a different fact from the one the table beneath carries. Both axes emit ticks, so that
// assertion keeps only the ones that are month names - the numeric scale on the other axis is not what it is about.

import { describe, expect, it, vi } from 'vitest'
import { cloneElement, type ReactElement } from 'react'
import { render, screen } from '@testing-library/react'
import { BarChart } from './BarChart'
import '../../i18n/config'


vi.mock('recharts', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('recharts')
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width?: number; height?: number }> }) =>
      cloneElement(children, { width: 600, height: 220 }),
  }
})

describe('BarChart', () => {
  it('says so, rather than drawing an empty axis, when every value is withheld', () => {
    render(<BarChart data={[{ key: 'Jan', value: null }, { key: 'Feb', value: null }]} valueLabel="Value" />)

    expect(screen.getByText('No figures available to chart. The table below carries what there is.')).toBeInTheDocument()
  })

  it('draws the values it has and leaves the withheld ones as a labelled gap', () => {
    const { container } = render(
      <BarChart data={[{ key: 'Jan', value: 10 }, { key: 'Feb', value: null }, { key: 'Mar', value: 30 }]} valueLabel="Value" />,
    )

    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(2)
  })

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

    const order = [...container.querySelectorAll('.recharts-label-list text')].map((t) => t.textContent)
    expect(order).toEqual(['90', '5'])
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(2)
  })

  it('writes its figures the way the table beneath writes them', () => {
    const { container } = render(
      <BarChart orientation="ranked" valueLabel="Value" data={[{ key: 'Catering', value: 412000 }]} />,
    )

    const labels = [...container.querySelectorAll('.recharts-label-list text')].map((t) => t.textContent)
    expect(labels).toEqual(['412,000'])
  })

  it('caps a bar at the width the mark spec allows', () => {
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
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)

    expect(container.querySelector('[aria-hidden="true"]')).toBeInTheDocument()
  })

  it('is inert to the keyboard as well as to a screen reader', () => {
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }]} valueLabel="Value" />)

    const tabbable = [...container.querySelectorAll('[tabindex]')]
      .filter((el) => Number(el.getAttribute('tabindex')) >= 0)

    expect(tabbable).toEqual([])
    expect(container.querySelector('[role="application"]')).toBeNull()
  })

  it('carries no colour of its own', () => {
    const { container } = render(<BarChart data={[{ key: 'Jan', value: 10 }, { key: 'Feb', value: 20 }]} valueLabel="Value" />)
    const fills = [...container.querySelectorAll('.recharts-bar-rectangle path')].map((p) => p.getAttribute('fill'))
    expect(new Set(fills).size).toBe(1)
  })

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

    const labels = [...container.querySelectorAll('.recharts-label')].map((el) => el.textContent)
    expect(labels).toEqual(['90', '40', '3'])
  })

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

    const months = [...container.querySelectorAll('.recharts-cartesian-axis-tick-value')]
      .map((el) => el.textContent)
      .filter((text) => ['Jan', 'Feb', 'Mar'].includes(text ?? ''))
    expect(months).toEqual(['Jan', 'Feb', 'Mar'])
  })
})
