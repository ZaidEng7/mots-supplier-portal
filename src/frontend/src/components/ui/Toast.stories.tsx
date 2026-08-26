import type { Meta, StoryObj } from '@storybook/react-vite'
import { ToastProvider, useToast } from './Toast'
import { Button } from './Button'

function Demo() {
  const { notify } = useToast()
  return (
    <Button
      onClick={() =>
        notify({ kind: 'success', title: 'Supplier approved', description: 'SUP-2026-000001 is now active.' })
      }
    >
      Trigger toast
    </Button>
  )
}

const meta = {
  title: 'UI/Toast',
  component: ToastProvider,
  args: { children: null },
  render: () => (
    <ToastProvider>
      <Demo />
    </ToastProvider>
  ),
} satisfies Meta<typeof ToastProvider>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = { args: {} }
