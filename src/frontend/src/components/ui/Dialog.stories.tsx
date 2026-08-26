import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Dialog } from './Dialog'
import { Button } from './Button'

const meta = {
  title: 'UI/Dialog',
  component: Dialog,
  render: (args) => {
    const [open, setOpen] = useState(true)
    return <Dialog {...args} open={open} onOpenChange={setOpen} />
  },
  args: {
    open: true,
    onOpenChange: () => {},
    title: 'Reject supplier application',
    description: 'This action cannot be undone.',
    children: (
      <div className="flex justify-end gap-2">
        <Button variant="secondary">Cancel</Button>
        <Button variant="danger">Reject</Button>
      </div>
    ),
  },
} satisfies Meta<typeof Dialog>

export default meta
type Story = StoryObj<typeof meta>

export const Open: Story = { args: {} }
