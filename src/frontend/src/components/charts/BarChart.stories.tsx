// The bar chart in both orientations, in both languages, and with a withheld figure.
//
// These stories exist because nothing in this product could show a bar chart. The three ministry award charts are the
// only consumers, and the demonstration database holds zero awards, so every one of them renders "no figures available
// to chart" - the ranked orientation in particular had never been seen by anybody, in either language, since the day it
// was written. They also put the charts inside the axe sweep over the built Storybook, which had no chart in it.
//
// The columns story is a series read along time: the data's own order is the meaning, so it is never sorted. The
// ranked story is the comparison of unordered things, sorted, with the figure written at the end of its own bar - the
// category names are prose, which is the reason that form exists, because they do not fit under a column. And the
// withheld story is a gap with its category still labelled, never a bar of height zero, which would assert that
// nothing was awarded: D-57 withholds commercial values outside a demonstration environment, so it is the shape a real
// Ministry reader sees most.

import type { Meta, StoryObj } from '@storybook/react-vite'
import { ArabicStory } from './rtlStoryDecorator'
import { BarChart } from './BarChart'

const meta = {
  title: 'Charts/BarChart',
  component: BarChart,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof BarChart>

export default meta
type Story = StoryObj<typeof meta>

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

export const SingleBar: Story = {
  args: { valueLabel: 'Awarded value', data: [{ key: '2026-09', value: 42_000 }] },
}

export const NothingToPlot: Story = {
  args: {
    valueLabel: 'Awarded value',
    data: [{ key: '2026-08', value: null }, { key: '2026-09', value: null }],
  },
}

export const RankedInArabic: Story = {
  args: RankedByCategory.args,
  decorators: [(Story) => <ArabicStory><Story /></ArabicStory>],
}

export const ByMonthInArabic: Story = {
  args: ByMonth.args,
  decorators: [(Story) => <ArabicStory><Story /></ArabicStory>],
}
