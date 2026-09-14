// How much of a whole is currently usable: approved suppliers against the ones who can actually bid.
//
// Why two shades of one hue rather than two colours. These are not two categories, they are a part and its whole, and
// a sequential pair says that where a categorical pair would say they are unrelated things standing side by side.
// Measured, the brand ramp could not have carried a categorical pair anyway: eight of its ten steps sit below the 0.10
// chroma floor a hue needs to do identity work, so a second hue would have had to come from outside the token layer.
//
// Stacked rather than overlaid. The segments are "can trade" and "the rest of the pool", which sum to the pool - so
// the bar's full length is a real quantity rather than two bars drawn on top of one another and hoping the reader
// infers the difference. A gap in the surface colour separates them, because two fills of the same hue meeting at an
// edge read as one; it is drawn as a 2px stroke in the surface colour rather than as a margin, because a stacked
// segment has no margin to give.
//
// The number is written at the end of the bar, as "12 of 14", because the question this chart answers is a comparison
// of two figures and reading both off a shared axis is work the label can do. Height is optional for the same reason
// as the ranked bar chart's: a coverage chart is a list, so its height is however many rows it has.
//
// THE READOUT carries two separate corrections, both visible in a screenshot. The figures went through i18next
// interpolation raw, so the chart said "14 of 18" in Western digits directly above a table that said "١٤" and "١٨" -
// one screen, two numbering systems. And the drawing surface is deliberately direction: ltr, which is what makes
// recharts anchor its labels correctly, but it also put this mixed string of digits and Arabic into a left-to-right
// paragraph, where "١٤ من ١٨" lays out with the first number on the left and an Arabic reader meets it last. Reading
// the row as "18 of 14" is not a smaller error than the wrong digits. The isolate characters give the string its own
// right-to-left context without changing the direction the label is anchored in.
//
// THE ROWS are drawn in the order they are given, which is the order the table beneath is written in. They used to be
// re-sorted here, by pool size descending, while the table kept the server's order - so the third bar and the third
// row were different categories and a reader moving between them had to re-find their place every time. Ordering is
// the page's decision because the page owns both halves; a chart that quietly re-orders its own copy of the data
// cannot be read against anything.
//
// The readout is carried by whichever segment actually ends the bar, and by only one of them. A category with nobody
// suspended has a remainder of zero, so recharts draws no rectangle for that segment and no label with it - which is
// how every fully-covered row lost its figures. On the Ministry's own data that was four of the six rows: the reader
// saw "14 of 18" beside the one category with a gap and nothing at all beside the four that were fine, which reads as
// missing data rather than as a full pool. An empty string is a label recharts renders and nobody sees, so exactly
// one of the two is ever visible on a row.
//
// THE LEGEND is markup rather than recharts' own, so it survives the aria-hidden on the drawing: there are two series,
// identity must never be carried by colour alone, and the chart itself is hidden from a screen reader by design - the
// same reason every chart in this product is, because the table beneath is the accessible copy. Its first label is not
// "Approved": the whole bar is the approved pool, so labelling one segment "Approved" would say the other one is not.
//
// THE COVERED SEGMENT ENDS SQUARE, deliberately, and it is the one place the rounding rule is not applied. A stack has
// one outer tip and it belongs to whichever segment ends the bar - the remainder when somebody is suspended, the
// covered segment when nobody is. Rounding the covered segment unconditionally puts a 4px curve on the SEAM between
// the two fills on every row that HAS a remainder, so the two stop meeting flush and one bar reads as two. Rounding
// it per row is what recharts cannot express: Cell takes a scalar radius, which would round the baseline end as well
// and turn a fully-covered row into a pill. So a row with nobody suspended ends square. It is the smaller of the two
// wrongs, and it is written down rather than left to be rediscovered.
//
// The labels use position="right" in BOTH directions. recharts places labels in its own coordinate space and
// `reversed` has already mirrored it, so "right" is the end the value reaches either way; the "left" that shipped for
// Arabic put the figure back at the baseline, on top of the category name. The outer tip itself is rounded at the data
// end and square against the baseline, mirrored for Arabic.

import { useTranslation } from 'react-i18next'
import { Bar, BarChart as RechartsBarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { RTL_LANGUAGES } from '../../i18n/rtl'
import { TOOLTIP_ITEM_STYLE, TOOLTIP_LABEL_STYLE, TOOLTIP_STYLE, segmentTooltipRow } from './chartTooltip'
import { formatNumber } from '../../lib/datetime'

const OUTER_TIP = (isRtl: boolean): [number, number, number, number] =>
  isRtl ? [4, 0, 0, 4] : [0, 4, 4, 0]

export interface CoverageDatum {
  key: string
  label: string
  total: number
  covered: number
}

export function CoverageChart({ data, height }: {
  data: CoverageDatum[]
  height?: number
}) {
  const { t, i18n } = useTranslation()
  const isRtl = RTL_LANGUAGES.has(i18n.language)
  const locale = isRtl ? 'ar' : 'en-GB'

  const readoutFor = (covered: number, total: number) => {
    const text = t('charts.coverageReadout', {
      covered: formatNumber(covered, locale, 0),
      total: formatNumber(total, locale, 0),
    })
    return isRtl ? `\u2067${text}\u2069` : text
  }

  if (data.length === 0) {
    return (
      <p className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        {t('charts.nothingToPlot')}
      </p>
    )
  }

  const rows = data
    .map((row) => {
      const remainder = Math.max(row.total - row.covered, 0)
      const readout = readoutFor(row.covered, row.total)
      return {
        ...row,
        remainder,
        readoutAtCovered: remainder === 0 ? readout : '',
        readoutAtRemainder: remainder > 0 ? readout : '',
      }
    })

  return (
    <div>
      <ul className="mb-3 flex list-none flex-wrap gap-4 p-0 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
        <li className="flex items-center gap-2">
          <span aria-hidden="true" className="h-3 w-3 rounded-[var(--radius-sm)]" style={{ backgroundColor: 'var(--color-chart-fill)' }} />
          {t('charts.coverageCanTrade')}
        </li>
        <li className="flex items-center gap-2">
          <span aria-hidden="true" className="h-3 w-3 rounded-[var(--radius-sm)]" style={{ backgroundColor: 'var(--color-chart-fill-muted)' }} />
          {t('charts.coverageSuspended')}
        </li>
      </ul>

      <div aria-hidden="true" style={{ inlineSize: '100%', blockSize: height ?? Math.max(72, rows.length * 34 + 16), direction: 'ltr' }}>
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
            <Tooltip
              cursor={{ fill: 'var(--color-bg-sunken)' }}
              contentStyle={TOOLTIP_STYLE}
              itemStyle={TOOLTIP_ITEM_STYLE}
              labelStyle={TOOLTIP_LABEL_STYLE}
              formatter={segmentTooltipRow(
                (figure) => formatNumber(figure, locale, 0),
                (segment) => (segment === 'covered' ? t('charts.coverageCanTrade') : t('charts.coverageSuspended')),
              )}
            />
            <Bar dataKey="covered" stackId="pool" fill="var(--color-chart-fill)" barSize={14} isAnimationActive={false}>
              <LabelList dataKey="readoutAtCovered" position="right" fill="var(--color-text-primary)" fontSize={11} />
            </Bar>
            <Bar
              dataKey="remainder"
              stackId="pool"
              fill="var(--color-chart-fill-muted)"
              barSize={14}
              isAnimationActive={false}
              radius={OUTER_TIP(isRtl)}
              stroke="var(--color-bg-surface)"
              strokeWidth={2}
            >
              <LabelList
                dataKey="readoutAtRemainder"
                position="right"
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
