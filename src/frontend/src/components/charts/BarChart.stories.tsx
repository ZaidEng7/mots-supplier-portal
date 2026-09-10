import type { Meta, StoryObj } from '@storybook/react-vite'
import { ArabicStory } from './rtlStoryDecorator'
import { BarChart } from './BarChart'

/**
 * <p>These stories exist because nothing in this product could show a bar chart. The three ministry
 * award charts are the only consumers, and the demonstration database holds zero awards, so every one
 * of them renders "no figures available to chart" - the ranked orientation in particular had never
 * been seen by anybody, in either language, since the day it was written.</p>
 *
 * <p>They also put the charts inside the axe sweep over the built Storybook, which had no chart in it.</p>
 */
const meta = {
  title: 'Charts/BarChart',
  component: BarChart,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof BarChart>

export default meta
type Story = StoryObj<typeof meta>

/** A series read along time: the data's own order is the meaning, so it is never sorted. */
export const ByMonth: Story = {
  args: {
    valueLabel: 'Awarded value',
    data: [
      { key: '2026-04', value: 184_000 },
      { key: '2026-05', value: 96_500 },
      { key: '2026-06', value: 271_300 },
      { key: '2026-07', value: 142_800 },
      { key: '2026-08', value: 318_900 },
      { key: '2026-09', value: 87_200 },
    ],
  },
}

/**
 * The comparison of unordered things, sorted, with the figure written at the end of its own bar. The
 * category names are prose, which is the reason this form exists: they do not fit under a column.
 */
export const RankedByCategory: Story = {
  args: {
    orientation: 'ranked',
    valueLabel: 'Awarded value',
    data: [
      { key: 'Catering & Hospitality', value: 412_000 },
      { key: 'Accommodation & Hotels', value: 268_500 },
      { key: 'Transport & Logistics', value: 191_000 },
      { key: 'Maintenance & Technical', value: 96_400 },
      { key: 'Events & Conferences', value: 41_900 },
      { key: 'Tour operations', value: 18_600 },
    ],
  },
}

/**
 * A withheld figure is a gap with its category still labelled - never a bar of height zero, which
 * would assert that nothing was awarded. D-57 withholds commercial values outside a demonstration
 * environment, so this is the shape a real Ministry reader sees most.
 */
export const SomeValuesWithheld: Story = {
  args: {
    valueLabel: 'Awarded value',
    data: [
      { key: '2026-04', value: 184_000 },
      { key: '2026-05', value: null },
      { key: '2026-06', value: 271_300 },
      { key: '2026-07', value: null },
      { key: '2026-08', value: 318_900 },
    ],
  },
}

/** One award in a month is one data point, not a full scale. */
export const SingleBar: Story = {
  args: { valueLabel: 'Awarded value', data: [{ key: '2026-09', value: 42_000 }] },
}

/** Every figure withheld: the chart says so rather than drawing an empty axis. */
export const NothingToPlot: Story = {
  args: {
    valueLabel: 'Awarded value',
    data: [{ key: '2026-08', value: null }, { key: '2026-09', value: null }],
  },
}

/**
 * The same ranked chart in Arabic. Bars grow from the right, the category names sit on the right, and
 * the figure is written at the left end of its own bar - the end the value reaches.
 */
export const RankedInArabic: Story = {
  args: RankedByCategory.args,
  decorators: [(Story) => <ArabicStory><Story /></ArabicStory>],
}

/** The time series in Arabic: the months run right to left, because that is the reading order. */
export const ByMonthInArabic: Story = {
  args: ByMonth.args,
  decorators: [(Story) => <ArabicStory><Story /></ArabicStory>],
}
