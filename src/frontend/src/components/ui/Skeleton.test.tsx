// The four skeleton shapes and the reduced-motion opt-out.
//
// A bare bar is hidden from assistive technology, because a shimmering bar carries no information to announce.
//
// SKELETONLIST announces once through a polite busy live region, per ACCESSIBILITY.md §6 and §7, and its label is
// the ONLY thing a screen reader reads there - every bar is aria-hidden. It renders the requested row count, so the
// placeholder matches the list it replaces.
//
// SKELETONTABLE renders a header row plus body rows across the frozen column and the data columns, which is
// (rows + 1 header) x (columns + 1 frozen row-header column). It pins only the first cell of each row, and with the
// LOGICAL property rather than `left`: SCR-432 asks for a "matrix (inline-start frozen row headers = criteria/line
// items; columns = proposals)", and using `left` would pin the skeleton to the wrong edge in Arabic while the real
// table pins to the right one. It scrolls horizontally rather than reflowing, as the real matrix does, and announces
// through the same busy live region contract - as does SkeletonGrid.
//
// REDUCED MOTION. The shimmer is CSS, so jsdom cannot evaluate the media query, and asserting the rule EXISTS in the
// stylesheet is the honest test: it would fail if someone deleted the opt-out or renamed the class out from under
// it, which is the regression that actually matters here. DESIGN-SYSTEM.md §6.17: "shimmering with reduced motion
// respected".

// THE REVEAL DELAY is tested with fake timers so the boundary is exact rather than a real wait that a slow machine could pass
// by accident. Each case asserts both sides of REVEAL_AFTER_MS: one millisecond short, the placeholder is in the document
// holding its space but hidden and out of the accessibility tree; at the mark, it is visible and announced. A placeholder that
// never hid would fail the first half, and one that never revealed would fail the second, so neither half can pass alone.
//
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { REVEAL_AFTER_MS, Skeleton, SkeletonGrid, SkeletonList, SkeletonTable } from './Skeleton'

describe('Skeleton', () => {
  it('is hidden from assistive tech - a shimmering bar carries no information to announce', () => {
    const { container } = render(<Skeleton />)
    const bar = container.querySelector('.msp-skeleton')

    expect(bar).not.toBeNull()
    expect(bar).toHaveAttribute('aria-hidden', 'true')
  })
})

describe('SkeletonList', () => {
  it('announces once through a polite busy live region, per ACCESSIBILITY.md §6/§7', async () => {
    render(<SkeletonList label="Loading RFQs" />)

    const region = await screen.findByRole('status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Loading RFQs')).toBeInTheDocument()
  })

  it('renders the requested row count so the placeholder matches the list it replaces', () => {
    const { container } = render(<SkeletonList label="Loading" rows={3} />)

    expect(container.querySelectorAll('.msp-skeleton')).toHaveLength(3)
  })
})

describe('SkeletonTable', () => {
  it('renders a header row plus body rows across the frozen column and the data columns', () => {
    const { container } = render(<SkeletonTable label="Loading matrix" rows={4} columns={3} />)

    expect(container.querySelectorAll('.msp-skeleton')).toHaveLength(5 * 4)
  })

  it('pins only the first cell of each row, using insetInlineStart for RTL correctness', () => {
    const { container } = render(<SkeletonTable label="Loading matrix" rows={2} columns={2} />)
    const cells = [...container.querySelectorAll<HTMLElement>('.msp-skeleton')]

    const pinned = cells.filter((c) => c.style.position === 'sticky')
    expect(pinned).toHaveLength(3) // one per row, header included
    for (const cell of pinned) {
      expect(cell.style.insetInlineStart).toBe('0px')
      expect(cell.style.left).toBe('')
    }
  })

  it('scrolls horizontally rather than reflowing, as the real matrix does', () => {
    const { container } = render(<SkeletonTable label="Loading matrix" />)

    expect(container.firstElementChild).toHaveClass('overflow-x-auto')
  })

  it('announces through the same busy live region contract', async () => {
    render(<SkeletonTable label="Loading matrix" />)

    const region = await screen.findByRole('status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).toHaveAttribute('aria-busy', 'true')
  })
})

describe('SkeletonGrid', () => {
  it('renders tiles inside the same busy live region contract', async () => {
    const { container } = render(<SkeletonGrid label="Loading metrics" items={6} />)

    const region = await screen.findByRole('status')
    expect(region).toHaveAttribute('aria-busy', 'true')
    expect(container.querySelectorAll('.msp-skeleton')).toHaveLength(6)
  })
})

describe('reduced motion', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8')

  it('declares the shimmer on .msp-skeleton', () => {
    expect(css).toContain('animation: msp-skeleton-shimmer')
  })

  it('turns the shimmer off under prefers-reduced-motion', () => {
    const scoped = css.slice(css.indexOf('@keyframes msp-skeleton-shimmer'))
    expect(scoped).toMatch(/@media \(prefers-reduced-motion: reduce\) \{\s*\.msp-skeleton \{[^}]*animation-name: none/)
  })
})

describe('the reveal delay', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  const advance = (ms: number) => act(() => { vi.advanceTimersByTime(ms) })

  it('keeps a container placeholder hidden and unannounced until a load outlasts the delay', () => {
    const { container } = render(<SkeletonList label="Loading offerings" rows={3} />)
    const region = container.querySelector<HTMLElement>('[aria-busy="true"]')

    advance(REVEAL_AFTER_MS - 1)
    expect(region).toBeInTheDocument()
    expect(region?.style.visibility).toBe('hidden')
    expect(screen.queryByRole('status')).toBeNull()

    advance(1)
    expect(region?.style.visibility).toBe('')
    expect(screen.getByRole('status')).toHaveTextContent('Loading offerings')
  })

  it('applies the same delay to the table and the grid', () => {
    const { container } = render(
      <>
        <SkeletonTable label="Loading matrix" />
        <SkeletonGrid label="Loading tiles" />
      </>,
    )
    const regions = [...container.querySelectorAll<HTMLElement>('[aria-busy="true"]')]
    expect(regions).toHaveLength(2)

    advance(REVEAL_AFTER_MS - 1)
    expect(regions.map((r) => r.style.visibility)).toEqual(['hidden', 'hidden'])

    advance(1)
    expect(regions.map((r) => r.style.visibility)).toEqual(['', ''])
  })

  it('lets bars inside a container reveal with it rather than each running a timer of their own', () => {
    const { container } = render(<SkeletonTable label="Loading matrix" rows={2} columns={2} />)
    const bars = [...container.querySelectorAll<HTMLElement>('.msp-skeleton')]

    expect(bars.length).toBeGreaterThan(0)
    expect(bars.every((bar) => bar.style.visibility === '')).toBe(true)
  })

  it('gives a bar used on its own the delay itself', () => {
    const { container } = render(<Skeleton />)
    const bar = container.querySelector<HTMLElement>('.msp-skeleton')

    advance(REVEAL_AFTER_MS - 1)
    expect(bar?.style.visibility).toBe('hidden')

    advance(1)
    expect(bar?.style.visibility).toBe('')
  })
})
