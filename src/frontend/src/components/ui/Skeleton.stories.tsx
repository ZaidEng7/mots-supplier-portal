// The four skeleton shapes, each in the screen that names it.
//
// SkeletonList is SCR-120 and SCR-400's "loading: SkeletonList for KPI tiles + lists". SkeletonTable is SCR-432's
// "loading: skeleton matrix", with frozen inline-start row headers and horizontal scroll, and a second story gives it
// more proposal columns than fit, because the frozen first column is what stays put while the rest scroll.
// SkeletonGrid is SCR-600's "loading: SkeletonGrid for tiles + charts".
//
// The dark stories render inside .theme-dark - the same class tokens.css defines the dark palette on. No existing
// story in this folder had a dark variant, so this wrapper is the first of its kind here, and it is deliberately
// local rather than a global decorator so it does not change how every other story renders.

import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ReactNode } from 'react'
import { Skeleton, SkeletonGrid, SkeletonList, SkeletonTable } from './Skeleton'

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

export const TableManyColumns: Story = {
  render: () => (
    <Surface>
      <SkeletonTable label="Loading comparison matrix" rows={4} columns={6} />
    </Surface>
  ),
}

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
