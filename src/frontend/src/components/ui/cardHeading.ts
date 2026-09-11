import { createContext, useContext } from 'react'

/**
 * The id of the heading the surrounding card draws, for anything inside that needs an accessible name.
 *
 * <p><b>What this removes.</b> A table inside a titled card carried an `sr-only` `<caption>` repeating
 * the card's own visible `<h2>`, on 26 sites - so a screen reader announced the list's name twice, and
 * the first time was the worse one: the caption ran straight into the header row and the live text read
 * "Items Items # TITLE CATEGORY QUANTITY". The Rams audit named it as one of five removable things on
 * one screen.</p>
 *
 * <p>Deleting the caption on its own would have taken the table's accessible name with it, which is why
 * the audit's own note warns against exactly that: "the card heading is a visible h2, not a table
 * caption; confirm the accessible name survives". Pointing at the heading keeps the name and announces
 * it once.</p>
 *
 * <p>Its own module rather than a second export beside the card: a context and a component in one file
 * is what the fast-refresh rule objects to, and the objection is fair - what a card publishes about
 * itself is not a piece of the card.</p>
 */
export const CardHeadingIdContext = createContext<string | undefined>(undefined)

/** Read by Table, which names itself from the card's heading rather than repeating it. */
export function useCardHeadingId(): string | undefined {
  return useContext(CardHeadingIdContext)
}
