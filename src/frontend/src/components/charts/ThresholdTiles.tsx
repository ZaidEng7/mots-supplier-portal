export type ThresholdTone = 'good' | 'warning' | 'critical'

export interface ThresholdTile {
  key: string
  count: number
  label: string
  tone: ThresholdTone
}

/**
 * How a queue is doing against its own thresholds, as three counts.
 *
 * <p><b>Why counts and not a bar.</b> Three numbers that sum to a total the reader already has are not a
 * distribution worth drawing; the question is "how many are late", and a chart makes that a measuring
 * exercise. The comp reaches the same answer.</p>
 *
 * <p><b>Not colour alone.</b> Each tile carries a glyph and a word as well as a tone, because "at risk"
 * and "overdue" are the two states somebody acts on and a reader who cannot separate amber from red
 * would otherwise have to open every row to find out which is which.</p>
 *
 * <p><b>The thresholds are the queue's own.</b> These counts come from the hours a queue treats as
 * at-risk and overdue, never from a share of the total - a percentile would call a fifth of the queue
 * late on a day when nothing is late.</p>
 */
export function ThresholdTiles({ tiles, label }: Readonly<{ tiles: readonly ThresholdTile[]; label: string }>) {
  return (
    <ul aria-label={label} className="m-0 grid list-none grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3 p-0">
      {tiles.map((tile) => {
        const colour = toneColour(tile.tone)
        return (
          <li
            key={tile.key}
            className="rounded-[var(--radius-lg)] p-4"
            style={{ backgroundColor: 'var(--color-bg-surface)', border: `1px solid ${colour}` }}
          >
            <p className="text-[length:var(--text-h3)] font-[var(--fw-semibold)] tabular-nums" style={{ color: colour }}>
              {tile.count}
            </p>
            <p className="mt-1 flex items-center gap-1 text-[length:var(--text-body-sm)]" style={{ color: colour }}>
              <span aria-hidden="true">{toneGlyph(tile.tone)}</span>
              {tile.label}
            </p>
          </li>
        )
      })}
    </ul>
  )
}

function toneColour(tone: ThresholdTone): string {
  if (tone === 'critical') return 'var(--color-danger-fg)'
  if (tone === 'warning') return 'var(--color-warning-fg)'
  return 'var(--color-success-fg)'
}

/** A shape, so the three tiles differ without their colours. */
function toneGlyph(tone: ThresholdTone): string {
  if (tone === 'critical') return '✕'
  if (tone === 'warning') return '!'
  return '✓'
}
