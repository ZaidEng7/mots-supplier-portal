import type { Meta, StoryObj } from '@storybook/react-vite'
import { ThresholdTiles } from './ThresholdTiles'

/**
 * Three counts against the review queue's own at-risk and overdue thresholds. Not a chart, on purpose:
 * three numbers that sum to a total the reader already has are not a distribution worth drawing.
 *
 * <p>Each tile carries a glyph and a word as well as a tone, so a reader who cannot separate amber
 * from red still knows which pile is late.</p>
 */
const meta = {
  title: 'Charts/ThresholdTiles',
  component: ThresholdTiles,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof ThresholdTiles>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
    label: 'Queue ageing',
    tiles: [
      { key: 'good', count: 12, label: 'Within target', tone: 'good' },
      { key: 'warning', count: 4, label: 'At risk', tone: 'warning' },
      { key: 'critical', count: 2, label: 'Overdue', tone: 'critical' },
    ],
  },
}

/** The queue a reviewer wants to see: nothing late. */
export const AllWithinTarget: Story = {
  args: {
    label: 'Queue ageing',
    tiles: [
      { key: 'good', count: 21, label: 'Within target', tone: 'good' },
      { key: 'warning', count: 0, label: 'At risk', tone: 'warning' },
      { key: 'critical', count: 0, label: 'Overdue', tone: 'critical' },
    ],
  },
}
