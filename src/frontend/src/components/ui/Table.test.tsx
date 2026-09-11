import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { Card } from './Card'
import { Table, TableBody, TableCell, TableRow } from './Table'

/**
 * A table is named once.
 *
 * <p><b>The defect.</b> Every table inside a titled card carried an `sr-only` `<caption>` repeating
 * the card's own visible `<h2>` - 26 sites. A screen reader therefore announced the list's name twice,
 * and the first time was the worse one: the caption ran straight into the header row, so the live text
 * read "Items Items # TITLE CATEGORY QUANTITY". The Rams audit counted it among five removable things
 * on one screen.</p>
 *
 * <p><b>Why the obvious fix was wrong.</b> Deleting the caption takes the table's accessible name with
 * it, and the audit's own note warns against exactly that: "the card heading is a visible h2, not a
 * table caption; confirm the accessible name survives". So the table points at the heading instead -
 * one name, said once, still there.</p>
 */
const rows = (
  <TableBody>
    <TableRow><TableCell>a row</TableCell></TableRow>
  </TableBody>
)

describe('a table names itself once', () => {
  it('takes its name from the card heading above it, rather than repeating it', () => {
    const { container } = render(
      <Card title="Tenders"><Table flush caption="Tenders">{rows}</Table></Card>,
    )

    const table = container.querySelector('table')!
    const heading = container.querySelector('h2')!

    expect(table.getAttribute('aria-labelledby')).toBe(heading.id)
    expect(table.querySelector('caption'), 'the caption would be the second announcement').toBeNull()
  })

  it('keeps its caption when no card heading names it', () => {
    // A bare table on a page, or one in a card with no title. Deleting the caption here would leave
    // the table with no accessible name at all, which is the failure the audit warned about.
    const { container } = render(<Table caption="Documents">{rows}</Table>)

    const table = container.querySelector('table')!
    expect(table.querySelector('caption')?.textContent).toBe('Documents')
    expect(table.getAttribute('aria-labelledby')).toBeNull()
  })

  it('keeps its caption inside an untitled card, which names nothing', () => {
    const { container } = render(<Card><Table caption="Documents">{rows}</Table></Card>)

    expect(container.querySelector('caption')?.textContent).toBe('Documents')
  })

  it('is never named twice', () => {
    // The rule stated directly, over all three arrangements above. Whichever way a table is named, it
    // must not be named both ways at once.
    for (const tree of [
      <Card key="titled" title="Tenders"><Table caption="Tenders">{rows}</Table></Card>,
      <Card key="untitled"><Table caption="Documents">{rows}</Table></Card>,
      <Table key="bare" caption="Documents">{rows}</Table>,
    ]) {
      const { container, unmount } = render(tree)
      const table = container.querySelector('table')!

      const byHeading = table.getAttribute('aria-labelledby') !== null
      const byCaption = table.querySelector('caption') !== null

      expect(byHeading || byCaption, 'a table must have an accessible name').toBe(true)
      expect(byHeading && byCaption, 'and must not have two').toBe(false)
      unmount()
    }
  })
})
