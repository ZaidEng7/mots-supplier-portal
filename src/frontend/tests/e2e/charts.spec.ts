// The chart claims a unit test cannot reach.
//
// Why a browser is required. jsdom reports zero metrics for every text node, so recharts renders its
// axis tick groups empty: no unit test in this repository can read an axis label, measure a label
// against a bar, or tell whether two pieces of text overlap. Every geometry defect these charts shipped
// was of exactly that kind - an Arabic category name drawn on top of its own bar, a figure pinned to
// the baseline instead of the tip - and none of them was visible to any instrument that existed.
//
// It runs against the built Storybook, which is where the charts have deterministic data. The product's
// own award screens cannot serve: the demonstration database holds zero awards, so all three of them
// render "no figures available to chart".
//
// open() waits for the story, then the recharts surface, then 300ms - recharts lays out on a measured
// container, so the first frame is the pre-measure one.
//
// boxes() reads the bounding rectangle of everything matching a selector, and the selectors it is
// called with are worth stating. recharts 3 renders tick TEXT in a `recharts-*Axis-tick-labels` layer
// that is a SIBLING of the axis group rather than a descendant, so the obvious
// `.recharts-yAxis .recharts-cartesian-axis-tick-value` matches nothing and every assertion built on it
// passes over an empty list - which is the failure mode this whole file exists to catch, arriving in the
// file itself.
//
// The withheld figure. Five months, three of them with a figure. The two withheld ones are the reason
// that test exists: they were filtered out of the data entirely, so a five-month chart drew three
// months and said nothing about the other two, under a comment promising "a gap with the category still
// labelled".
//
// The scale is asserted twice. Written the way the figures on the bars are written, because the axis
// read "240000" beside bars labelled "240,000" - the last place a raw JavaScript number reached a
// reader on these screens; and not clipped by the room reserved for it, so no label starts outside the
// drawing surface.
//
// The marks obey their own spec: a column no thicker than the 24px cap, and a grid line with no dash
// array - a hairline.
//
// Arabic carries three cases. The ranked chart's names must be clear of the bars: every category name
// used to be painted on top of its own bar, because recharts anchors axis text in logical terms and the
// surrounding page is right-to-left, which inverts what "start" means. Nothing in the SVG says so; only
// the boxes do. Its figures belong at the end the value reaches, which in Arabic is the left one. The
// coverage readout must be written in Arabic-Indic digits, because the table beneath the chart writes
// them that way and one screen does not get to write a number two ways - and each figure must carry the
// isolate characters that make "١٤ من ١٨" lay out with ١٤ first inside a left-to-right drawing surface,
// since without them an Arabic reader meets the total first and reads the row backwards. And the
// coverage names, like the ranked ones, stay clear of their stacks.
//
// The hover readout is checked in the dark theme, because recharts writes that row with an inline
// `color: #000` that an inherited colour cannot beat. On the dark surface that is 1.32:1 - a readout
// nobody can read, in the theme nobody screenshotted.

import { test, expect } from '@playwright/test'

const story = (id: string) => `/iframe.html?id=${id}&viewMode=story`

async function open(page: import('@playwright/test').Page, id: string) {
  await page.goto(story(id), { waitUntil: 'load' })
  await page.waitForSelector('body.sb-show-main', { timeout: 15_000 })
  await page.waitForSelector('svg.recharts-surface', { timeout: 15_000 })
  await page.waitForTimeout(300)
}

async function boxes(page: import('@playwright/test').Page, selector: string) {
  return page.$$eval(selector, (nodes) =>
    nodes.map((n) => {
      const b = n.getBoundingClientRect()
      return { text: (n.textContent ?? '').trim(), x: b.x, right: b.right, y: b.y, bottom: b.bottom, width: b.width, height: b.height }
    }),
  )
}

const overlap = (a: { x: number; right: number; y: number; bottom: number }, b: typeof a) =>
  a.x < b.right && b.x < a.right && a.y < b.bottom && b.y < a.bottom

test.describe('a withheld figure keeps its place', () => {
  test('the category stays on the axis with no bar over it', async ({ page }) => {
    await open(page, 'charts-barchart--some-values-withheld')

    const ticks = (await boxes(page, '.recharts-xAxis-tick-labels .recharts-cartesian-axis-tick-value')).map((t) => t.text)
    const bars = await boxes(page, '.recharts-bar-rectangle')

    expect(ticks).toEqual(['2026-04', '2026-05', '2026-06', '2026-07', '2026-08'])
    expect(bars).toHaveLength(3)
  })
})

test.describe('the scale', () => {
  test('is written the way the figures on the bars are written', async ({ page }) => {
    await open(page, 'charts-barchart--by-month')

    const ticks = (await boxes(page, '.recharts-yAxis-tick-labels .recharts-cartesian-axis-tick-value'))
      .map((t) => t.text)
      .filter(Boolean)

    expect(ticks.length).toBeGreaterThan(2)
    const large = ticks.filter((t) => /\d{4,}/.test(t.replace(/[^0-9]/g, '')))
    for (const tick of large) {
      expect(tick, `the axis writes ${tick} unformatted`).toMatch(/[,\u066C\u060C]/)
    }
  })

  test('is not clipped by the room reserved for it', async ({ page }) => {
    await open(page, 'charts-barchart--by-month')

    const svg = (await boxes(page, 'svg.recharts-surface'))[0]
    const ticks = await boxes(page, '.recharts-yAxis-tick-labels .recharts-cartesian-axis-tick-value')

    for (const tick of ticks) {
      expect(tick.x, `the axis label "${tick.text}" starts outside the chart`).toBeGreaterThanOrEqual(svg.x - 1)
    }
  })
})

test.describe('the marks obey their own spec', () => {
  test('a column is no thicker than the cap, and the grid is a hairline', async ({ page }) => {
    await open(page, 'charts-barchart--single-bar')

    const bar = (await boxes(page, '.recharts-bar-rectangle .recharts-rectangle'))[0]
    expect(bar.width).toBeLessThanOrEqual(24)

    const dash = await page.getAttribute('.recharts-cartesian-grid line', 'stroke-dasharray')
    expect(dash).toBeNull()
  })
})

test.describe('Arabic', () => {
  test('the ranked chart puts its names clear of the bars and its figures at the tip', async ({ page }) => {
    await open(page, 'charts-barchart--ranked-in-arabic')

    const bars = await boxes(page, '.recharts-bar-rectangle')
    const names = await boxes(page, '.recharts-yAxis-tick-labels .recharts-cartesian-axis-tick-value')
    const figures = await boxes(page, '.recharts-label-list text')

    expect(bars.length).toBe(6)
    expect(names.length).toBe(6)

    for (const name of names) {
      for (const bar of bars) {
        expect(overlap(name, bar), `the category name "${name.text}" is drawn over a bar`).toBe(false)
      }
    }

    for (const figure of figures) {
      const own = bars.find((bar) => figure.y >= bar.y - 6 && figure.bottom <= bar.bottom + 6)
      expect(own, `no bar found on the row of "${figure.text}"`).toBeDefined()
      expect(figure.right, `"${figure.text}" is not at the tip of its bar`).toBeLessThanOrEqual(own!.x + 2)
    }
  })

  test('the coverage readout is written in this locale, and reads in the right order', async ({ page }) => {
    await open(page, 'charts-coveragechart--in-arabic')

    const figures = (await boxes(page, '.recharts-label-list text')).map((f) => f.text).filter(Boolean)
    expect(figures.length).toBe(5)

    for (const figure of figures) {
      expect(figure, `"${figure}" is not written in this locale's digits`).toMatch(/[٠-٩]/)
      expect(figure).not.toMatch(/[0-9]/)
    }

    expect(figures.every((f) => f.startsWith('⁧') && f.endsWith('⁩'))).toBe(true)
  })

  test('the coverage names stay clear of the stacks', async ({ page }) => {
    await open(page, 'charts-coveragechart--in-arabic')

    const bars = await boxes(page, '.recharts-bar-rectangle')
    const names = await boxes(page, '.recharts-yAxis-tick-labels .recharts-cartesian-axis-tick-value')

    for (const name of names) {
      for (const bar of bars) {
        expect(overlap(name, bar), `the category name "${name.text}" is drawn over a stack`).toBe(false)
      }
    }
  })
})

test.describe('the hover readout', () => {
  test('is legible in the dark theme, where recharts paints its own text black', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' })
    await open(page, 'charts-barchart--by-month')

    const bar = await page.locator('.recharts-bar-rectangle').first().boundingBox()
    await page.mouse.move(bar!.x + bar!.width / 2, bar!.y + bar!.height / 2)
    await page.waitForSelector('.recharts-tooltip-item', { timeout: 5_000 })

    const colour = await page.locator('.recharts-tooltip-item').first().evaluate((el) => getComputedStyle(el).color)
    expect(colour).not.toBe('rgb(0, 0, 0)')
  })
})
