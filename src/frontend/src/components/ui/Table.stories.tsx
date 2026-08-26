import type { Meta, StoryObj } from '@storybook/react-vite'
import { Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell } from './Table'
import { Badge } from './Badge'

const suppliers = [
  { code: 'SUP-2026-000001', name: 'Test Co', status: 'Active' },
  { code: 'SUP-2026-000002', name: 'Levant Textiles', status: 'Pending review' },
]

const meta = {
  title: 'UI/Table',
  component: Table,
  args: { children: null },
  render: () => (
    <Table caption="Suppliers">
      <TableHead>
        <TableHeaderCell>Reference</TableHeaderCell>
        <TableHeaderCell>Name</TableHeaderCell>
        <TableHeaderCell>Status</TableHeaderCell>
      </TableHead>
      <TableBody>
        {suppliers.map((s) => (
          <TableRow key={s.code}>
            <TableCell>{s.code}</TableCell>
            <TableCell>{s.name}</TableCell>
            <TableCell>
              <Badge tone={s.status === 'Active' ? 'success' : 'warning'}>{s.status}</Badge>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  ),
} satisfies Meta<typeof Table>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = { args: {} }
