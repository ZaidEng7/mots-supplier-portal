import type { ReactNode } from 'react'

/**
 * What the reader is expected to do, said in a sentence, with the control that does it inside the
 * same box.
 *
 * <p>The tender workspace used to answer this with a column of badges naming permitted transitions,
 * and the buttons that performed them lived somewhere else on the page. A badge is a label, not an
 * instruction, and a reader who saw "Publish" in green still had to go looking for the way to publish.</p>
 *
 * <p>It is tinted rather than bordered like the other cards, because it is the one thing on the rail
 * that asks for something. Every other card there reports.</p>
 */
export function NextActionCard({ title, children, action }: Readonly<{
  title: string
  children: ReactNode
  /** The control that performs it. Omitted when the state genuinely asks nothing of this reader. */
  action?: ReactNode
}>) {
  return (
    <section
      aria-labelledby="next-action-title"
      className="rounded-[var(--radius-lg)] p-4"
      style={{ backgroundColor: 'var(--color-brand-subtle)', border: '1px solid var(--color-brand-solid)' }}
    >
      <h2
        id="next-action-title"
        className="mb-3 text-[length:var(--text-caption)] font-[var(--fw-semibold)] uppercase tracking-wide"
        style={{ color: 'var(--color-text-brand)' }}
      >
        {title}
      </h2>
      <div className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-primary)' }}>
        {children}
      </div>
      {action ? <div className="mt-3 flex flex-col gap-2">{action}</div> : null}
    </section>
  )
}
