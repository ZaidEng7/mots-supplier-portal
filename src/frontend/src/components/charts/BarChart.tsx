// A single-measure bar chart, in one hue, in two orientations.
//
// Why one hue and not a palette. The brand ramp was measured with the dataviz validator, and only two of its ten
// steps - --brand-400 at chroma 0.111 and --brand-500 at 0.108 - clear the 0.10 floor a hue needs before it can
// carry identity at all. Two steps of one hue, one apart, are not two identities. So a categorical palette cannot be
// built from this ramp without introducing colours from outside the token layer, and these charts carry identity in
// the axis label and the table beneath instead. That is the same answer the comp reached.
//
// The sentence this replaces claimed the family "sits at chroma 0.053-0.094", credited a completed dataviz pass for
// the number, and was wrong at both ends - --brand-200 is 0.062 and --brand-400 is 0.111. Nothing computed it and
// nothing could have caught it. The figures above come from scripts/validate_palette.js and the ones that matter are
// now asserted in themeContrast.test.ts.
//
// The table is not a fallback. Every chart here sits above the table it draws, which is the accessibility requirement
// and also the honest ordering: the numbers are the record and the chart is a reading of them. Both drawings are
// aria-hidden for that reason - recharts emits an SVG of paths and tick labels that a screen reader reads as a stream
// of unrelated numbers, and announcing both would announce it twice, worse the first time. accessibilityLayer is off
// because recharts otherwise emits an svg with role="application".
//
// WITHHELD IS NOT ZERO. A null value is a commercial figure D-57 withholds, and it keeps its place on the axis: drawn
// as a gap with the category still labelled, never as a bar of height zero, which would assert that nothing was
// awarded. It used to be filtered out entirely, while the note above this component said it was "drawn as a gap with
// the category still labelled" and the component's own test asserted the deletion - three statements, two of them
// wrong, and the one that shipped was the worst of the three: a month whose value D-57 withholds simply vanished, so
// a reader counting columns found five months in a six-month range and no reason given. recharts draws no bar for a
// null and still draws its tick, which is exactly the gap that was described. Ranked keeps them too, sorted to the
// end, because a category with a withheld figure is still a category that was awarded something.
//
// THE TWO ORIENTATIONS. 'columns' is for a series read along time - a month beside a month - where the order is the
// data's own and reversing it would lie about it. 'ranked' is for a comparison of unordered things, where the question
// is which is biggest: bars run along the reading direction, sorted, with the value written at the end of each. A
// category name is prose and does not fit under a column, so ranked puts it on the axis where it has room, and puts
// the number where the eye already is instead of making the reader walk back to a scale. A ranked chart leaves room
// at the inline end for that value - without it the widest number is clipped by the container, which is the one
// number a reader most wants - and its bars are thin, because a ranked row is a length to compare rather than a block
// to fill. They are rounded at the data end only and square against the baseline they are measured from, which is
// the inline-start edge and swaps with the reading direction.
//
// HEIGHT is optional, because a column chart has a fixed height while its bars share one baseline, and a ranked chart
// is a list whose height is however many rows it has. Six categories in a 220px box left a bar floating in the middle
// of an empty card, which reads as missing data rather than as one row.
//
// FORMATTING defaults to the same locale-aware number formatting the table beneath uses, which is the point: the
// chart wrote 412000 while the table two inches below wrote 412,000, and in Arabic the table wrote ٤١٢٬٠٠٠ while the
// chart still wrote Western digits - two renderings of one number on one screen. A caller charting money passes its
// own, because a bar chart cannot know a currency. The y-axis is written the same way, and was the last place raw
// JavaScript numbers reached a reader: the bars carried formatted values and the axis beside them read "240000", in
// Western digits even in Arabic. Its width grew with it, because a separator makes the widest tick wider and a
// clipped axis label is worse than an unformatted one.
//
// THE GRID is --color-chart-grid rather than --color-border, because a grid line is scaffolding and must recede, and
// it is solid rather than dashed - a dashed grid is a second texture competing with the marks.
//
// maxBarSize is there because a category count is not a width. A month with one award drew a bar spanning the whole
// card, which reads as a full scale rather than as one data point - and sparse data is the normal case early in a
// tender year rather than a fixture artefact. It is 24px, not the 56 that shipped: the mark spec caps a bar at 24 and
// the ranked bars in this same file are 14, so a column that could grow to 56 was two and a half times its own
// sibling, which is not a thin mark and made a two-month chart read as a block diagram.

import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, CartesianGrid, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { RTL_LANGUAGES } from '../../i18n/rtl'
import { formatNumber } from '../../lib/datetime'
import { TOOLTIP_ITEM_STYLE, TOOLTIP_LABEL_STYLE, TOOLTIP_STYLE, tooltipRow } from './chartTooltip'

export interface BarDatum {
  key: string
  value: number | null
}

export function BarChart({ data, valueLabel, orientation = 'columns', height, formatValue }: {
  data: BarDatum[]
  valueLabel: string
  orientation?: 'columns' | 'ranked'
  formatValue?: (value: number) => string
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)
  const locale = isRtl ? 'ar' : 'en-GB'
  const write = formatValue ?? ((value: number) => formatNumber(value, locale, 0))
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
      <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: rankedHeight, direction: 'ltr' }}>
        <ResponsiveContainer width="100%" height="100%">
          <RechartsBarChart
            data={drawable}
            layout="vertical"
            accessibilityLayer={false}
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
            <Bar
              dataKey="value"
              radius={isRtl ? [4, 0, 0, 4] : [0, 4, 4, 0]}
              barSize={14}
              isAnimationActive={false}
            >
              {drawable.map((d) => (
                <Cell key={d.key} fill="var(--color-chart-fill)" />
              ))}
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
    <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height ?? 220, direction: 'ltr' }}>
      <ResponsiveContainer width="100%" height="100%">
        <RechartsBarChart data={drawable} accessibilityLayer={false} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
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
            tickFormatter={(value: number) => write(value)}
            tickLine={false}
            axisLine={false}
            width={72}
          />
          <Tooltip
            cursor={{ fill: 'var(--color-bg-sunken)' }}
            contentStyle={TOOLTIP_STYLE}
            itemStyle={TOOLTIP_ITEM_STYLE}
            labelStyle={TOOLTIP_LABEL_STYLE}
            formatter={tooltipRow(write, valueLabel)}
          />
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
