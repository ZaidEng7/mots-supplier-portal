import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, LabelList, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { RTL_LANGUAGES } from '../../i18n/config'

export interface CoverageDatum {
  key: string
  /** The name a reader sees on the axis. */
  label: string
  /** The pool: everyone approved for this category. */
  total: number
  /** Of those, the ones who can act today. Never greater than `total`. */
  covered: number
}

/**
 * How much of a whole is currently usable: approved suppliers against the ones who can actually bid.
 *
 * <p><b>Why two shades of one hue rather than two colours.</b> These are not two categories, they are a
 * part and its whole, and a sequential pair says that where a categorical pair would say they are
 * unrelated things standing side by side. The `dataviz` pass also found the brand family too low in
 * chroma to carry categorical identity at all, so a second hue would have had to come from outside the
 * token layer.</p>
 *
 * <p><b>Stacked rather than overlaid.</b> The segments are "can trade" and "the rest of the pool", which
 * sum to the pool - so the bar's full length is a real quantity rather than two bars drawn on top of one
 * another and hoping the reader infers the difference. A gap in the surface colour separates them,
 * because two fills of the same hue meeting at an edge read as one.</p>
 *
 * <p><b>The number is written at the end of the bar</b>, as "12 of 14", because the question this chart
 * answers is a comparison of two figures and reading both off a shared axis is work the label can do.</p>
 */
export function CoverageChart({ data, height }: {
  data: CoverageDatum[]
  /** Optional: a coverage chart is a list, so its height is however many rows it has. */
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)

  if (data.length === 0) {
    return (
      <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('charts.nothingToPlot')}
      </p>
    )
  }

  const rows = [...data]
    .sort((a, b) => b.total - a.total)
    .map((row) => ({
      ...row,
      remainder: Math.max(row.total - row.covered, 0),
      readout: t('charts.coverageReadout', { covered: row.covered, total: row.total }),
    }))

  return (
    <div>
      {/*
        A legend, because there are two series. It is markup rather than recharts' own, so it survives
        the `aria-hidden` on the drawing below - identity must never be carried by colour alone, and the
        chart itself is hidden from a screen reader by design.
      */}
      <ul className="mb-3 flex list-none flex-wrap gap-4 p-0 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        <li className="flex items-center gap-2">
          <span aria-hidden="true" className="h-3 w-3 rounded-[var(--radius-sm)]" style={{ backgroundColor: 'var(--color-brand-solid)' }} />
          {t('charts.coverageCanTrade')}
        </li>
        <li className="flex items-center gap-2">
          <span aria-hidden="true" className="h-3 w-3 rounded-[var(--radius-sm)]" style={{ backgroundColor: 'var(--color-accent-line)' }} />
          {t('charts.coverageApproved')}
        </li>
      </ul>

      {/* Hidden from assistive technology for the same reason every chart in this product is: the table
          beneath carries the same figures with headers, and announcing both announces it twice. */}
      <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height ?? Math.max(72, rows.length * 34 + 16) }}>
        <ResponsiveContainer width="100%" height="100%">
          <RechartsBarChart
            data={rows}
            layout="vertical"
            accessibilityLayer={false}
            margin={{ top: 4, right: isRtl ? 8 : 64, bottom: 4, left: isRtl ? 64 : 8 }}
          >
            <XAxis type="number" hide reversed={isRtl} />
            <YAxis
              type="category"
              dataKey="label"
              orientation={isRtl ? 'right' : 'left'}
              tick={{ fill: 'var(--color-text-secondary)', fontSize: 11 }}
              tickLine={false}
              axisLine={false}
              width={128}
            />
            <Bar dataKey="covered" stackId="pool" fill="var(--color-brand-solid)" barSize={14} isAnimationActive={false} />
            <Bar
              dataKey="remainder"
              stackId="pool"
              fill="var(--color-accent-line)"
              barSize={14}
              isAnimationActive={false}
              radius={isRtl ? [4, 0, 0, 4] : [0, 4, 4, 0]}
              // The 2px separator between two fills of one hue. Drawn in the surface colour rather than
              // as a margin, because a stacked segment has no margin to give.
              stroke="var(--color-bg-surface)"
              strokeWidth={2}
            >
              <LabelList
                dataKey="readout"
                position={isRtl ? 'left' : 'right'}
                fill="var(--color-text-primary)"
                fontSize={11}
              />
            </Bar>
          </RechartsBarChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
