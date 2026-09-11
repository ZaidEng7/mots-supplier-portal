import { useId, type ReactNode } from 'react'
import { CardHeadingIdContext } from './cardHeading'

interface CardProps {
  title?: string
  action?: ReactNode
  /**
   * Hold the content edge to edge rather than padding it.
   *
   * <p>For the one thing that brings its own edges: a table. Padded, a table draws its own frame a few
   * pixels inside the card's, which reads as a box in a box and is the one place this product's list
   * screens visibly departed from the approved template. Everything else keeps the padding, because
   * everything else is content rather than a surface.</p>
   */
  flush?: boolean
  children: ReactNode
}

/** Section container per DESIGN-SYSTEM §6.7 - the surface/border/radius/padding pattern every
 * onboarding section already repeats inline, extracted so new screens don't hand-roll it again. */
export function Card({ title, action, flush = false, children }: CardProps) {
  const headingId = useId()

  return (
    <div
      className="overflow-hidden rounded-[var(--radius-lg)]"
      // §6.7: surface, 1px border, --radius-lg, --shadow-sm. The border does the separating; the
      // shadow is the faintest step on the scale, not a lift.
      style={{
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-sm)',
      }}
    >
      {/*
        A band rather than a heading floating above the content, which is the approved template's card.
        The rule under it is what separates the card's own name from what the card holds - before this
        the two sat in one padded box and a reader had to take the first line on faith as a title.
      */}
      {title || action ? (
        <div
          className="flex items-center justify-between gap-3 px-4 py-3.5"
          style={{ borderBlockEnd: '1px solid var(--color-border)' }}
        >
          {title ? (
            <h2 id={headingId} className="text-[length:var(--text-body)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
              {title}
            </h2>
          ) : (
            <span />
          )}
          {action}
        </div>
      ) : null}
      <div className={flush ? '' : 'p-4'}>
        <CardHeadingIdContext.Provider value={title ? headingId : undefined}>{children}</CardHeadingIdContext.Provider>
      </div>
    </div>
  )
}
