import type { Meta, StoryObj } from '@storybook/react-vite'
import { Input } from './Input'

const meta = {
  title: 'UI/Input',
  component: Input,
  args: { 'aria-label': 'Supplier reference code', placeholder: 'SUP-2026-000001' },
} satisfies Meta<typeof Input>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}
export const Invalid: Story = { args: { invalid: true, defaultValue: 'not-a-code' } }
export const Disabled: Story = { args: { disabled: true, defaultValue: 'SUP-2026-000001' } }
