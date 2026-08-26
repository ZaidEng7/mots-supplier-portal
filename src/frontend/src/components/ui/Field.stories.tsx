import type { Meta, StoryObj } from '@storybook/react-vite'
import { Field } from './Field'
import { Input } from './Input'

const meta = {
  title: 'UI/Field',
  component: Field,
} satisfies Meta<typeof Field>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
    label: 'Email',
    required: true,
    children: (inputProps) => <Input type="email" {...inputProps} />,
  },
}

export const WithHint: Story = {
  args: {
    label: 'Registration number',
    hint: 'As printed on the commercial registry certificate',
    children: (inputProps) => <Input {...inputProps} />,
  },
}

export const WithError: Story = {
  args: {
    label: 'Password',
    required: true,
    error: 'Password must be at least 10 characters',
    children: (inputProps) => <Input type="password" {...inputProps} />,
  },
}
