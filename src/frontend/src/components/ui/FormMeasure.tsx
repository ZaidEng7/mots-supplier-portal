import type { ReactNode } from 'react'

/**
 * The width a form is allowed to be.
 *
 * <p><b>The defect this closes.</b> Every screen filled whatever width the window gave it. On a 1440
 * monitor that put the supplier's legal-name input at 540 pixels for a value of a few words, and set
 * each label 800 pixels away from the field on the other side of the row. A form is read down a column
 * and filled one field at a time, so past a certain width every extra pixel is distance between a
 * label and the thing it labels.</p>
 *
 * <p>1080 is the approved template's own figure for this archetype - it caps the page at 1440 and
 * overrides the form to 1080 - and it is roughly two comfortable columns of fields side by side, which
 * is what these forms are.</p>
 *
 * <p>A component rather than a class repeated on five screens, so the number is stated once and the
 * reason for it sits beside the number.</p>
 */
export function FormMeasure({ children }: Readonly<{ children: ReactNode }>) {
  return <div className="flex w-full max-w-[1080px] flex-col gap-6">{children}</div>
}
