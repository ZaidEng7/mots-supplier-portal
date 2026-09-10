/**
 * The hover card every chart in this product shares.
 *
 * <p>recharts pads its own tooltip 10px, which is off the 4px grid the rest of the product sits on,
 * and paints it white regardless of theme. Both are set here rather than left to the library, and
 * here rather than in each chart, because two charts drawing two different tooltips is how a design
 * system stops being one.</p>
 */
export const TOOLTIP_STYLE = {
  padding: 'var(--space-2) var(--space-3)',
  background: 'var(--color-bg-surface)',
  border: '1px solid var(--color-border)',
  borderRadius: 'var(--radius-md)',
  fontSize: 'var(--text-body-sm)',
  color: 'var(--color-text-primary)',
} as const

/**
 * The ink inside the tooltip, which `contentStyle` does not reach.
 *
 * <p>recharts writes the value row with its own inline `color: #000`, and an inline style on the child
 * beats an inherited colour from the container. On the dark theme that is black on --color-bg-surface:
 * 1.32:1. The tooltip looked right in every light-theme screenshot ever taken of this product and was
 * unreadable in the other theme.</p>
 */
export const TOOLTIP_ITEM_STYLE = { color: 'var(--color-text-primary)' } as const
export const TOOLTIP_LABEL_STYLE = { color: 'var(--color-text-secondary)' } as const
