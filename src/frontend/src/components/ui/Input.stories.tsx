// Input in its four states: resting, invalid, disabled and read-only. The last two are here because they are what
// DESIGN-SYSTEM.md §6.2 requires and what the component silently did not render until Task #21.

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
