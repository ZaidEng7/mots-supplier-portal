import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Skeleton, SkeletonGrid, SkeletonList, SkeletonTable } from './Skeleton'

describe('Skeleton', () => {
  it('is hidden from assistive tech - a shimmering bar carries no information to announce', () => {
    const { container } = render(<Skeleton />)
    const bar = container.querySelector('.msp-skeleton')

    expect(bar).not.toBeNull()
    expect(bar).toHaveAttribute('aria-hidden', 'true')
  })
})

describe('SkeletonList', () => {
  it('announces once through a polite busy live region, per ACCESSIBILITY.md §6/§7', () => {
    render(<SkeletonList label="Loading RFQs" />)

    const region = screen.getByRole('status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).toHaveAttribute('aria-busy', 'true')
    // The label is the ONLY thing a screen reader reads here; every bar is aria-hidden.
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

    // (rows + 1 header) x (columns + 1 frozen row-header column)
    expect(container.querySelectorAll('.msp-skeleton')).toHaveLength(5 * 4)
  })

  /**
   * SCR-432: "Matrix (`inline-start` frozen row headers = criteria/line items; columns =
   * proposals)". The frozen column must pin with the LOGICAL property, not `left`, or the skeleton
   * pins to the wrong edge in Arabic while the real table pins to the right one.
   */
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

  it('announces through the same busy live region contract', () => {
    render(<SkeletonTable label="Loading matrix" />)

    const region = screen.getByRole('status')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).toHaveAttribute('aria-busy', 'true')
  })
})

describe('SkeletonGrid', () => {
  it('renders tiles inside the same busy live region contract', () => {
    const { container } = render(<SkeletonGrid label="Loading metrics" items={6} />)

    const region = screen.getByRole('status')
    expect(region).toHaveAttribute('aria-busy', 'true')
    expect(container.querySelectorAll('.msp-skeleton')).toHaveLength(6)
  })
})

/**
 * The shimmer is CSS, so jsdom cannot evaluate the media query - asserting the rule EXISTS in the
 * stylesheet is the honest test. It would fail if someone deleted the opt-out or renamed the class
 * out from under it, which is the regression that actually matters here.
 *
 * DESIGN-SYSTEM.md §6.17: "shimmering with reduced motion respected".
 */
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
