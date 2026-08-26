import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Select } from './Select'

const options = [
  { value: 'SYP', label: 'Syrian Pound' },
  { value: 'USD', label: 'US Dollar' },
  { value: 'EUR', label: 'Euro' },
]

const meta = {
  title: 'UI/Select',
  component: Select,
  render: (args) => {
    const [value, setValue] = useState(args.value)
    return <Select {...args} value={value} onValueChange={setValue} />
  },
  args: { options, placeholder: 'Select a currency', onValueChange: () => {} },
} satisfies Meta<typeof Select>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = { args: {} }
export const Preselected: Story = { args: { value: 'USD' } }
