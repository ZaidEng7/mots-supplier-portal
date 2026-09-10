import type { Meta, StoryObj } from '@storybook/react-vite'
import { ArabicStory } from './rtlStoryDecorator'
import { CoverageChart } from './CoverageChart'

/**
 * <p>The part-and-whole chart on the Ministry's category screen: how much of the approved pool can
 * actually trade today. The two segments are one hue at two steps, because they are a part and its
 * whole rather than two unrelated things.</p>
 *
 * <p>The muted step is the reason these stories exist. It used to be --color-accent-line, which
 * measures 1.68:1 on a white surface, so the segment standing for "approved but cannot trade" - the
 * whole point of the chart - was invisible, and no story existed in which anybody would have seen it.</p>
 */
const meta = {
  title: 'Charts/CoverageChart',
  component: CoverageChart,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof CoverageChart>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
    data: [
      { key: 'catering', label: 'Catering & Hospitality', total: 18, covered: 14 },
      { key: 'transport', label: 'Transport & Logistics', total: 11, covered: 11 },
      { key: 'accommodation', label: 'Accommodation & Hotels', total: 4, covered: 3 },
      { key: 'events', label: 'Events & Conferences', total: 3, covered: 3 },
      { key: 'maintenance', label: 'Maintenance & Technical', total: 2, covered: 0 },
    ],
  },
}

/**
 * The row this screen is opened to find: a category whose whole approved pool is suspended. It reads
 * as a bar that is entirely the muted step, which only works if the muted step can be seen.
 */
export const NoneCanTrade: Story = {
  args: { data: [{ key: 'maintenance', label: 'Maintenance & Technical', total: 6, covered: 0 }] },
}

/** The other end: nothing suspended, so there is no second segment at all. */
export const FullyCovered: Story = {
  args: { data: [{ key: 'transport', label: 'Transport & Logistics', total: 11, covered: 11 }] },
}

/** The same coverage chart in Arabic, where every part of its geometry flips. */
export const InArabic: Story = {
  args: Default.args,
  decorators: [(Story) => <ArabicStory><Story /></ArabicStory>],
}
