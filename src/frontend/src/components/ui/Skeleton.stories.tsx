import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ReactNode } from 'react'
import { Skeleton, SkeletonGrid, SkeletonList, SkeletonTable } from './Skeleton'

/**
 * Dark stories render inside `.theme-dark` - the same class `tokens.css:117` defines the dark
 * palette on. No existing story in this folder had a dark variant, so this wrapper is the first of
 * its kind here; it is deliberately local rather than a global decorator so it does not change how
 * every other story renders.
 */
function Surface({ dark, children }: { dark?: boolean; children: ReactNode }) {
  return (
    <div
      className={dark ? 'theme-dark' : undefined}
      style={{ backgroundColor: 'var(--color-bg-surface)', padding: '1.5rem', minWidth: '32rem' }}
    >
      {children}
    </div>
  )
}

const meta = {
  title: 'UI/Skeleton',
  component: Skeleton,
} satisfies Meta<typeof Skeleton>

export default meta
type Story = StoryObj<typeof meta>

export const BarLight: Story = {
  render: () => (
    <Surface>
      <Skeleton height="1.25rem" width="18rem" />
    </Surface>
  ),
}

export const BarDark: Story = {
  render: () => (
    <Surface dark>
      <Skeleton height="1.25rem" width="18rem" />
    </Surface>
  ),
}

/** SCR-120 / SCR-400: "*Loading:* `SkeletonList` for KPI tiles + lists". */
export const ListLight: Story = {
  render: () => (
    <Surface>
      <SkeletonList label="Loading RFQs" />
    </Surface>
  ),
}

export const ListDark: Story = {
  render: () => (
    <Surface dark>
      <SkeletonList label="Loading RFQs" />
    </Surface>
  ),
}

export const ListThreeRows: Story = {
  render: () => (
    <Surface>
      <SkeletonList label="Loading invitations" rows={3} />
    </Surface>
  ),
}

/** SCR-432: "*Loading:* skeleton matrix" - frozen inline-start row headers, horizontal scroll. */
export const TableLight: Story = {
  render: () => (
    <Surface>
      <SkeletonTable label="Loading comparison matrix" />
    </Surface>
  ),
}

export const TableDark: Story = {
  render: () => (
    <Surface dark>
      <SkeletonTable label="Loading comparison matrix" />
    </Surface>
  ),
}

/** More proposal columns than fit: the frozen first column is what stays put while the rest scroll. */
export const TableManyColumns: Story = {
  render: () => (
    <Surface>
      <SkeletonTable label="Loading comparison matrix" rows={4} columns={6} />
    </Surface>
  ),
}

/** SCR-600: "*Loading:* `SkeletonGrid` for tiles + charts". */
export const GridLight: Story = {
  render: () => (
    <Surface>
      <SkeletonGrid label="Loading governance metrics" />
    </Surface>
  ),
}

export const GridDark: Story = {
  render: () => (
    <Surface dark>
      <SkeletonGrid label="Loading governance metrics" />
    </Surface>
  ),
}
