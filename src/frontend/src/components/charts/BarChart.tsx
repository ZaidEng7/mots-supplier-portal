import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { RTL_LANGUAGES } from '../../i18n/config'

export interface BarDatum {
  /** The category this bar names — a month, a category, a buying body. */
  key: string
  /** The measure. `null` means withheld by disclosure policy, which is not the same as zero. */
  value: number | null
}

/**
 * A single-measure bar chart, in one hue.
 *
 * <p><b>Why one hue and not a palette.</b> The `dataviz` pass validated the product's own tokens against
 * its colour formula and the answer was unambiguous: the brand teal family sits at chroma 0.053–0.094
 * against a ~0.10 floor, so it cannot carry categorical identity — adjacent series would be
 * indistinguishable to a colour-blind reader and barely distinguishable to anyone else. Rather than
 * introduce a second palette and break the token contract, these charts carry identity in the axis label
 * and the table beneath, and use colour for magnitude only. That is the same answer the comp reached.</p>
 *
 * <p><b>The table is not a fallback.</b> Every chart here sits above the table it draws, which is the
 * accessibility requirement and also the honest ordering: the numbers are the record and the chart is a
 * reading of them.</p>
 *
 * <p><b>Withheld is not zero.</b> A `null` value is a commercial figure D-57 withholds, and it is drawn
 * as a gap with the category still labelled — never as a bar of height zero, which would assert that
 * nothing was awarded.</p>
 */
export function BarChart({ data, valueLabel, height = 220 }: {
  data: BarDatum[]
  /** What the measure is, for the tooltip and the axis. */
  valueLabel: string
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)
  const drawable = data.filter((d) => d.value !== null)

  if (drawable.length === 0) {
    return (
      <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('charts.nothingToPlot')}
      </p>
    )
  }

  return (
    // `aria-hidden`, deliberately. Recharts emits an SVG of paths and tick labels that a screen reader
    // reads as a stream of unrelated numbers; the table directly beneath carries the same data with
    // headers, which is the accessible presentation. Announcing both would be announcing it twice, worse
    // the first time.
    <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height }}>
      <ResponsiveContainer width="100%" height="100%">
        {/* `accessibilityLayer={false}` because recharts otherwise emits <svg role="application"
            tabindex="0">, which is focusable - and a focusable element inside an aria-hidden
            wrapper is a keyboard trap for a screen-reader user: they can tab into something their
            reader will not describe. axe's aria-hidden-focus rule caught it. The table beneath is
            the accessible presentation, so the chart should be inert in every sense, not half. */}
        <RechartsBarChart data={drawable} accessibilityLayer={false} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="var(--color-border)" strokeDasharray="2 4" vertical={false} />
          <XAxis
            dataKey="key"
            reversed={isRtl}
            tick={{ fill: 'var(--color-text-secondary)', fontSize: 11 }}
            tickLine={false}
            axisLine={{ stroke: 'var(--color-border)' }}
          />
          <YAxis
            orientation={isRtl ? 'right' : 'left'}
            tick={{ fill: 'var(--color-text-secondary)', fontSize: 11 }}
            tickLine={false}
            axisLine={false}
            width={56}
          />
          <Tooltip
            cursor={{ fill: 'var(--color-bg-sunken)' }}
            contentStyle={{
              // recharts' default tooltip pads itself 10px, which is off the 4px grid the rest of the
              // product sits on. Set here rather than left to the library.
              padding: 'var(--space-2) var(--space-3)',
              background: 'var(--color-bg-surface)',
              border: '1px solid var(--color-border)',
              borderRadius: 'var(--radius-md)',
              fontSize: 'var(--text-body-sm)',
              color: 'var(--color-text-primary)',
            }}
            formatter={(value) => [String(value), valueLabel]}
          />
          {/* `maxBarSize` because a category count is not a width. A month with one award drew a bar
              spanning the whole card, which reads as a full scale rather than as one data point -
              and sparse data is the normal case early in a tender year, not a fixture artefact. */}
          <Bar dataKey="value" radius={[4, 4, 0, 0]} maxBarSize={56} isAnimationActive={false}>
            {drawable.map((d) => (
              <Cell key={d.key} fill="var(--color-brand-solid)" />
            ))}
          </Bar>
        </RechartsBarChart>
      </ResponsiveContainer>
    </div>
  )
}
