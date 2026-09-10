import { expect, test } from '@playwright/test'

/**
 * A second live pass for the screens the first one photographed mid-load, plus the DOM readings an
 * audit needs and a screenshot cannot settle.
 *
 * <p>Run with `CAPTURE=1 npx playwright test capture-live-2 --project=capture`.</p>
 */
const OUT = '../../DESIGN-IS-2026-09-10/shots'

test.skip(!process.env.CAPTURE, 'capture driver: run with CAPTURE=1')

for (const [persona, email] of [
  ['officer', 'officer@mots.local'],
  ['reviewer', 'reviewer@mots.local'],
] as const) {
  test(`the review queue as ${persona}: what it renders and what its filters say`, async ({ page }) => {
    test.setTimeout(120_000)
    await page.setViewportSize({ width: 1440, height: 1000 })

    await page.goto('/login?lng=en')
    await page.getByLabel(/email/i).fill(email)
    await page.getByLabel(/password/i).fill('motsdemo2026')
    await page.getByRole('button', { name: /sign in/i }).click()
    await expect(page).not.toHaveURL(/\/login/, { timeout: 20_000 })

    await page.goto('/back-office/review?lng=en', { waitUntil: 'networkidle' })
    // No assertion on a table: whether one appears is part of what is being measured.
    await page.waitForTimeout(6000)

    const comboboxes = page.getByRole('combobox')
    const readings: string[] = []
    for (let i = 0; i < await comboboxes.count(); i++) {
      readings.push(`[${i}] ${((await comboboxes.nth(i).textContent()) ?? '').trim()}`)
    }
    console.log(`FILTERS-${persona}>>${readings.join(' | ')}`)
    console.log(`TABLES-${persona}>>${await page.getByRole('table').count()}`)
    console.log(`ALERTS-${persona}>>${await page.getByRole('alert').count()}`)
    const body = ((await page.locator('main').innerText()) ?? '').replace(/\s+/g, ' ').slice(0, 400)
    console.log(`TEXT-${persona}>>${body}`)

    await page.screenshot({ path: `${OUT}/${persona}-review-queue.png`, fullPage: true })
  })
}

test('the supplier directory and offering search, loaded', async ({ page }) => {
  test.setTimeout(120_000)
  await page.setViewportSize({ width: 1440, height: 1000 })

  await page.goto('/login?lng=en')
  await page.getByLabel(/email/i).fill('officer@mots.local')
  await page.getByLabel(/password/i).fill('motsdemo2026')
  await page.getByRole('button', { name: /sign in/i }).click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 20_000 })

  for (const [route, name] of [
    ['/back-office/suppliers', 'officer-supplier-directory'],
    ['/back-office/reports', 'officer-reports'],
  ] as const) {
    await page.goto(`${route}?lng=en`, { waitUntil: 'networkidle' })
    await page.waitForLoadState('networkidle')
    await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true })
  }
})
