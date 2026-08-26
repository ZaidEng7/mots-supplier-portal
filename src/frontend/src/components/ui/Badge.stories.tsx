import type { Meta, StoryObj } from '@storybook/react-vite'
import { Badge } from './Badge'

const meta = {
  title: 'UI/Badge',
  component: Badge,
  args: { children: 'Active' },
} satisfies Meta<typeof Badge>

export default meta
type Story = StoryObj<typeof meta>

export const Neutral: Story = { args: { tone: 'neutral' } }
export const Success: Story = { args: { tone: 'success', children: 'Approved' } }
export const Warning: Story = { args: { tone: 'warning', children: 'Pending review' } }
export const Danger: Story = { args: { tone: 'danger', children: 'Rejected' } }
export const Info: Story = { args: { tone: 'info', children: 'Draft' } }
export const Brand: Story = { args: { tone: 'brand', children: 'proposal.submit' } }
