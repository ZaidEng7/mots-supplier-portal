import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, CartesianGrid, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
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
export function BarChart({ data, valueLabel, orientation = 'columns', height }: {
  data: BarDatum[]
  /** What the measure is, for the tooltip and the axis. */
  valueLabel: string
  /**
   * `'columns'` for a series read along time - a month beside a month - where the order is the data's
   * own and reversing it would lie about it.
   *
   * `'ranked'` for a comparison of unordered things, where the question is which is biggest: bars run
   * along the reading direction, sorted, with the value written at the end of each. A category name is
   * prose and does not fit under a column; ranked puts it on the axis where it has room, and puts the
   * number where the eye already is instead of making the reader walk back to a scale.
   */
  orientation?: 'columns' | 'ranked'
  /**
   * Optional. A column chart has a fixed height because its bars share one baseline; a ranked chart is a
   * list, and its height is however many rows it has. Six categories in a 220px box left a bar floating
   * in the middle of an empty card, which reads as missing data rather than as one row.
   */
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)
  const withValues = data.filter((d) => d.value !== null)
  const drawable = orientation === 'ranked'
    ? [...withValues].sort((a, b) => (b.value ?? 0) - (a.value ?? 0))
    : withValues

  if (drawable.length === 0) {
    return (
      <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('charts.nothingToPlot')}
      </p>
    )
  }

  if (orientation === 'ranked') {
    const rankedHeight = height ?? Math.max(72, drawable.length * 34 + 16)
    return (
      // `aria-hidden` for the same reason as the column chart below: the table beneath is the accessible
      // copy, and announcing both would announce it twice, worse the first time.
      <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: rankedHeight }}>
        <ResponsiveContainer width="100%" height="100%">
          <RechartsBarChart
            data={drawable}
            layout="vertical"
            accessibilityLayer={false}
            // Room at the inline end for the value written there. Without it the widest number is
            // clipped by the container, which is the one number a reader most wants.
            margin={{ top: 4, right: isRtl ? 8 : 48, bottom: 4, left: isRtl ? 48 : 8 }}
          >
            <XAxis type="number" hide reversed={isRtl} />
            <YAxis
              type="category"
              dataKey="key"
              orientation={isRtl ? 'right' : 'left'}
              tick={{ fill: 'var(--color-text-secondary)', fontSize: 11 }}
              tickLine={false}
              axisLine={false}
              width={128}
            />
            {/* Thin marks: a ranked row is a length to compare, not a block to fill. */}
            <Bar
              dataKey="value"
              // Rounded at the data end only, square against the baseline it is measured from - which is
              // the inline-start edge, and swaps with the reading direction.
              radius={isRtl ? [4, 0, 0, 4] : [0, 4, 4, 0]}
              barSize={14}
              isAnimationActive={false}
            >
              {drawable.map((d) => (
                <Cell key={d.key} fill="var(--color-brand-solid)" />
              ))}
              {/* The number at the end of its own bar, rather than read off a scale. It takes a text
                  token rather than the series colour: a value is text, and the bar beside it already
                  carries the identity. */}
              <LabelList
                dataKey="value"
                position={isRtl ? 'left' : 'right'}
                fill="var(--color-text-primary)"
                fontSize={11}
              />
            </Bar>
          </RechartsBarChart>
        </ResponsiveContainer>
      </div>
    )
  }

  return (
    // `aria-hidden`, deliberately. Recharts emits an SVG of paths and tick labels that a screen reader
    // reads as a stream of unrelated numbers; the table directly beneath carries the same data with
    // headers, which is the accessible presentation. Announcing both would be announcing it twice, worse
    // the first time.
    <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height ?? 220 }}>
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
