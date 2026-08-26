import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ComponentProps } from 'react'
import { Select } from './Select'

const options = [
  { value: 'SYP', label: 'Syrian Pound' },
  { value: 'USD', label: 'US Dollar' },
  { value: 'EUR', label: 'Euro' },
]

function SelectStoryRender(args: ComponentProps<typeof Select>) {
  const [value, setValue] = useState(args.value)
  return <Select {...args} value={value} onValueChange={setValue} />
}

const meta = {
  title: 'UI/Select',
  component: Select,
  render: SelectStoryRender,
  args: { options, placeholder: 'Select a currency', onValueChange: () => {} },
} satisfies Meta<typeof Select>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = { args: {} }
export const Preselected: Story = { args: { value: 'USD' } }
