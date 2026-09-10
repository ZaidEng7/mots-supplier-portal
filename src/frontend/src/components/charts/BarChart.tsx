import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, CartesianGrid, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { RTL_LANGUAGES } from '../../i18n/rtl'
import { formatNumber } from '../../lib/datetime'
import { TOOLTIP_ITEM_STYLE, TOOLTIP_LABEL_STYLE, TOOLTIP_STYLE, tooltipRow } from './chartTooltip'

export interface BarDatum {
  /** The category this bar names — a month, a category, a buying body. */
  key: string
  /** The measure. `null` means withheld by disclosure policy, which is not the same as zero. */
  value: number | null
}

/**
 * A single-measure bar chart, in one hue.
 *
 * <p><b>Why one hue and not a palette.</b> The brand ramp was measured with the dataviz validator, and
 * only two of its ten steps - --brand-400 at chroma 0.111 and --brand-500 at 0.108 - clear the 0.10
 * floor a hue needs before it can carry identity at all. Two steps of one hue, one apart, are not two
 * identities. So a categorical palette cannot be built from this ramp without introducing colours from
 * outside the token layer, and these charts carry identity in the axis label and the table beneath
 * instead. That is the same answer the comp reached.</p>
 *
 * <p>The sentence this replaces claimed the family "sits at chroma 0.053-0.094", credited a completed
 * dataviz pass for the number, and was wrong at both ends - --brand-200 is 0.062 and --brand-400 is
 * 0.111. Nothing computed it and nothing could have caught it. The figures above come from
 * `scripts/validate_palette.js` and the ones that matter are now asserted in themeContrast.test.ts.</p>
 *
 * <p><b>The table is not a fallback.</b> Every chart here sits above the table it draws, which is the
 * accessibility requirement and also the honest ordering: the numbers are the record and the chart is a
 * reading of them.</p>
 *
 * <p><b>Withheld is not zero.</b> A `null` value is a commercial figure D-57 withholds, and it is drawn
 * as a gap with the category still labelled — never as a bar of height zero, which would assert that
 * nothing was awarded.</p>
 */
export function BarChart({ data, valueLabel, orientation = 'columns', height, formatValue }: {
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
   * How a figure is written on the chart. Defaults to the same locale-aware number formatting the
   * table beneath uses, which is the point: the chart wrote `412000` while the table two inches below
   * wrote `412,000`, and in Arabic the table wrote `٤١٢٬٠٠٠` while the chart still wrote Western
   * digits. Two renderings of one number on one screen.
   *
   * <p>A caller charting money passes its own, because a bar chart cannot know a currency.</p>
   */
  formatValue?: (value: number) => string
  /**
   * Optional. A column chart has a fixed height because its bars share one baseline; a ranked chart is a
   * list, and its height is however many rows it has. Six categories in a 220px box left a bar floating
   * in the middle of an empty card, which reads as missing data rather than as one row.
   */
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)
  const locale = isRtl ? 'ar' : 'en-GB'
  const write = formatValue ?? ((value: number) => formatNumber(value, locale, 0))
  /*
    A withheld figure keeps its place on the axis. It used to be filtered out entirely, while the
    comment above this function said it was "drawn as a gap with the category still labelled" and the
    component's own test asserted the deletion - three statements, two of them wrong, and the one that
    shipped was the worst of the three: a month whose value D-57 withholds simply vanished, so a
    reader counting columns found five months in a six-month range and no reason given.

    recharts draws no bar for a null and still draws its tick, which is exactly the gap that was
    described. Ranked keeps them too, sorted to the end, because a category with a withheld figure is
    still a category that was awarded something.
  */
  const drawable = orientation === 'ranked'
    ? [...data].sort((a, b) => (b.value ?? -1) - (a.value ?? -1))
    : data

  if (drawable.every((d) => d.value === null)) {
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
      <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: rankedHeight, direction: 'ltr' }}>
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
            <Tooltip
              cursor={{ fill: 'var(--color-bg-sunken)' }}
              contentStyle={TOOLTIP_STYLE}
              itemStyle={TOOLTIP_ITEM_STYLE}
              labelStyle={TOOLTIP_LABEL_STYLE}
              formatter={tooltipRow(write, valueLabel)}
            />
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
                <Cell key={d.key} fill="var(--color-chart-fill)" />
              ))}
                {/* The number at the end of its own bar, rather than read off a scale. It takes a text
                  token rather than the series colour: a value is text, and the bar beside it already
                  carries the identity.

                  `position="right"` in BOTH directions, which looks wrong and is not. recharts places
                  labels in its own coordinate space, and `reversed` has already mirrored that space -
                  so "right" means "past the end the value reaches" either way. Flipping it to "left"
                  for Arabic, which is what shipped, put every figure back at the baseline end, on top
                  of the category name. Measured, not reasoned: with the axis reversed, "right" lands
                  the label at x 190-229 against a bar starting at 234, and "left" lands it at 613-653
                  against a name occupying 616-697. */}
              <LabelList
                dataKey="value"
                position="right"
                formatter={(value) => (typeof value === 'number' ? write(value) : String(value ?? ''))}
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
    <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height ?? 220, direction: 'ltr' }}>
      <ResponsiveContainer width="100%" height="100%">
        {/* `accessibilityLayer={false}` because recharts otherwise emits <svg role="application"
            tabindex="0">, which is focusable - and a focusable element inside an aria-hidden
            wrapper is a keyboard trap for a screen-reader user: they can tab into something their
            reader will not describe. axe's aria-hidden-focus rule caught it. The table beneath is
            the accessible presentation, so the chart should be inert in every sense, not half. */}
        <RechartsBarChart data={drawable} accessibilityLayer={false} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          {/* --color-chart-grid, not --color-border: a grid line is scaffolding and must recede, and the
              dark theme's border reads at 3.63:1 against the same surface the light theme's meets at
              1.26:1 - the same chart would have read as two different charts. */}
          {/* Solid, not dashed. A dashed grid is a second texture competing with the marks for
              attention, and the mark spec rules it out; recessiveness is the job of the colour, which
              --color-chart-grid does at 1.26:1. */}
          <CartesianGrid stroke="var(--color-chart-grid)" vertical={false} />
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
            contentStyle={TOOLTIP_STYLE}
            itemStyle={TOOLTIP_ITEM_STYLE}
            labelStyle={TOOLTIP_LABEL_STYLE}
            formatter={tooltipRow(write, valueLabel)}
          />
          {/* `maxBarSize` because a category count is not a width. A month with one award drew a bar
              spanning the whole card, which reads as a full scale rather than as one data point -
              and sparse data is the normal case early in a tender year, not a fixture artefact.

              24px, not the 56 that shipped: the mark spec caps a bar at 24 and the ranked bars in this
              same file are 14. A column that could grow to 56 was two and a half times its own
              sibling, which is not a thin mark and made a two-month chart read as a block diagram. */}
          <Bar dataKey="value" radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false}>
            {drawable.map((d) => (
              <Cell key={d.key} fill="var(--color-chart-fill)" />
            ))}
          </Bar>
        </RechartsBarChart>
      </ResponsiveContainer>
    </div>
  )
}
