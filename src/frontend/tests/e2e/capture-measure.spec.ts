import { test } from '@playwright/test'
import { mockBackend } from './fixtures'

/**
 * Phase 7's re-measurement: the audit's own visual numbers, taken again from computed styles.
 *
 * <p>§D measured twelve screens and found 13px the most common size on every one, `<h1>` rendering at
 * three sizes for one job, and four spacing values off the 4px grid. Those are the numbers this pass
 * claimed to move, so this takes them the same way rather than asserting from source.</p>
 *
 * <p>Skipped unless CAPTURE=1, like the screenshot driver beside it: it reports rather than asserts, and
 * a suite should not fail because a distribution shifted.</p>
 */
test.skip(!process.env.CAPTURE, 'measurement driver: run with CAPTURE=1')

const SCREENS = [
  ['/back-office/dashboard', 'back office dashboard'],
  ['/back-office/rfqs', 'tender list'],
  ['/back-office/rfqs/RFQ-2026-000001', 'buyer tender detail'],
  ['/back-office/review', 'review queue'],
  ['/back-office/operations', 'operations console'],
  ['/dashboard', 'supplier dashboard'],
  ['/onboarding', 'supplier onboarding'],
  ['/documents', 'supplier documents'],
  ['/rfqs', 'supplier tenders'],
  ['/proposals', 'supplier proposals'],
  ['/back-office/ministry/awards', 'ministry analytics'],
  ['/settings', 'settings'],
] as const

test('measure the audit’s own numbers again', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 })
  await mockBackend(page)

  const report: string[] = []
  const everyH1 = new Set<string>()
  const offGrid = new Set<number>()

  for (const [route, name] of SCREENS) {
    await page.goto(`${route}?lng=en`)
    // waitFor, not isVisible: isVisible reports the CURRENT state with no auto-wait, so it answered
    // "no" for every screen before any of them had rendered.
    const rendered = await page.getByRole('heading', { level: 1 })
      .waitFor({ state: 'visible', timeout: 8000 })
      .then(() => true)
      .catch(() => false)
    if (!rendered) {
      // Reported, not skipped silently: a screen the mocked backend cannot render is a gap in the
      // measurement, and pretending otherwise is how a re-audit flatters itself.
      report.push(`  ${name.padEnd(24)} DID NOT RENDER under the mocked backend`)
      continue
    }

    const measured = await page.evaluate(() => {
      const sizes: Record<string, number> = {}
      const spacing = new Set<number>()
      const h1 = new Set<string>()
      // script/style/title carry text and no children, so a naive sweep counts them - at the browser's
      // default 16px, which is nobody's design decision. The first run of this measurement reported 16px
      // as the commonest size on four screens for exactly that reason.
      const INVISIBLE = new Set(['SCRIPT', 'STYLE', 'TITLE', 'HEAD', 'META', 'LINK', 'NOSCRIPT'])
      document.querySelectorAll('*').forEach((el) => {
        const cs = getComputedStyle(el)
        const text = (el.textContent ?? '').trim()
        if (text && el.children.length === 0 && !INVISIBLE.has(el.tagName) && cs.display !== 'none') {
          sizes[cs.fontSize] = (sizes[cs.fontSize] ?? 0) + 1
        }
        if (el.tagName === 'H1') h1.add(cs.fontSize)
        for (const prop of ['paddingTop', 'paddingLeft', 'marginTop', 'gap'] as const) {
          const value = parseFloat(cs[prop])
          if (value > 0 && value % 4 !== 0) spacing.add(value)
        }
      })
      // What is AT the commonest size, and where the off-grid spacing comes from - a distribution with
      // no examples cannot tell you whether the number means anything.
      const top = Object.entries(sizes).sort((a, b) => b[1] - a[1])[0]
      const examples: string[] = []
      document.querySelectorAll('*').forEach((el) => {
        if (examples.length >= 4) return
        const cs = getComputedStyle(el)
        const text = (el.textContent ?? '').trim()
        if (text && el.children.length === 0 && !INVISIBLE.has(el.tagName) && cs.fontSize === top?.[0]) {
          examples.push(`${el.tagName.toLowerCase()}:"${text.slice(0, 22)}"`)
        }
      })
      const offGridWhere: string[] = []
      document.querySelectorAll('*').forEach((el) => {
        if (offGridWhere.length >= 6) return
        const cs = getComputedStyle(el)
        for (const prop of ['paddingTop', 'paddingLeft', 'marginTop', 'gap'] as const) {
          const value = parseFloat(cs[prop])
          if (value > 0 && value % 4 !== 0 && (value === 7 || value === 10)) {
            offGridWhere.push(`${el.tagName.toLowerCase()}.${String(el.className).slice(0, 26)} ${prop}=${value}`)
            return
          }
        }
      })
      return { commonest: top?.[0] ?? 'none', count: top?.[1] ?? 0, h1: [...h1], offGrid: [...spacing], examples, offGridWhere }
    })

    measured.h1.forEach((s) => everyH1.add(s))
    measured.offGrid.forEach((s) => offGrid.add(s))
    report.push(`  ${name.padEnd(24)} commonest: ${measured.commonest} (${measured.count} nodes)  h1: ${measured.h1.join(', ') || 'none'}`)
    report.push(`       at that size: ${measured.examples.join('  ')}`)
    if (measured.offGridWhere.length) report.push(`       off-grid: ${measured.offGridWhere.join('  ')}`)
  }

  console.log('\n=== Phase 7 re-measurement ===')
  console.log(report.join('\n'))
  console.log(`\n  distinct <h1> sizes across all screens: ${[...everyH1].join(', ')}`)
  console.log(`  spacing values off the 4px grid: ${[...offGrid].sort((a, b) => a - b).join(', ') || 'none'}`)
})
