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

/**
 * What one row of a tooltip says: the figure, then what the figure is.
 *
 * <p>A named function rather than an arrow written inline three times, because the rule it carries is
 * the one this pass was about - a figure on a chart is written the way the table beneath writes it, in
 * whichever locale is running - and a rule worth stating is worth testing. Inline, it was three
 * closures nothing could reach: recharts calls them from inside its own hover machinery, which needs
 * real element geometry, and jsdom reports none.</p>
 *
 * <p>recharts hands the value back as `unknown` because a tooltip can sit on any series; anything that
 * is not a number is passed through as its own text rather than coerced into `NaN`.</p>
 */
export function tooltipRow(
  write: (value: number) => string,
  label: string,
): (value: unknown) => [string, string] {
  return (value) => {
    if (typeof value === 'number') return [write(value), label]
    // Anything else is passed through only when it is already text. An object stringified here would
    // put "[object Object]" on screen, which is worse than the value it stands for.
    return [typeof value === 'string' ? value : '', label]
  }
}

/**
 * The same row for a chart whose segments have different names - a stacked pair, where what the figure
 * IS depends on which segment the pointer is over.
 *
 * <p>Separate from <see cref="tooltipRow"/> for the reason that one exists at all: written inline it is
 * a closure recharts only calls from its hover machinery, which jsdom cannot drive, so the rule that
 * decides which of two words appears beside a number could not be checked anywhere.</p>
 */
export function segmentTooltipRow(
  write: (value: number) => string,
  labelFor: (segment: unknown) => string,
): (value: unknown, segment: unknown) => [string, string] {
  return (value, segment) => tooltipRow(write, labelFor(segment))(value)
}
