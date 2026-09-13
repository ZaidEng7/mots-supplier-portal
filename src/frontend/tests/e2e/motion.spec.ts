// The motion added to the shared layer plays, finishes, and lets the element go.
//
// The hazard this is really about: Radix's presence layer keeps a closing element mounted until it
// hears `animationend`. That is why the exit animations are keyframes and not transitions - but it
// also means a typo in a keyframe name, a rule the production CSS pipeline drops, or an animation that
// never starts leaves a dialog mounted over the page with its focus trap still live, and nothing in
// the component tree says so. src/styles/motion.test.ts reads the stylesheet; it cannot see that. Only
// a real browser opening and closing a real dialog can.
//
// It runs against the built Storybook rather than the app: the components under test are the shared
// ones, the stories need no backend and no session, and the static build is the same artefact the axe
// sweep already uses. openStory waits for Storybook's own readiness class, because Radix portals mount
// outside #storybook-root and waiting on the root's children never resolves for a dialog - learned the
// hard way in storybook-axe.spec.ts.
//
// The dialog. The animation's existence is asserted before the settle check, which would otherwise
// pass just as happily on a dialog that never animated at all. Then where it ends up, which matters
// more than what it does on the way: an animation with no fill mode, somehow left running, would park
// the panel at 96% forever. A modal is not anchored to a trigger, so it scales from its own middle -
// getComputedStyle resolves transform-origin to pixels, so the comparison is against half the box
// rather than the string "50%", and it uses offsetWidth rather than clientWidth because the origin
// resolves against the border box and this panel has a 1px border, which leaves clientWidth 2px short
// and the comparison off by one pixel each way. The last assertion is the real one: Escape must leave
// the element with a count of zero, because if the exit animation never fires animationend the panel
// stays in the document with its focus trap live and the page is unusable.
//
// Under reduced motion the animation is flattened, not removed. It still runs and still fires
// animationend, which is what Radix is waiting for; a rule that set `animation: none` here would
// strand the dialog on close.
//
// The select popover animates open and unmounts the same way. Its second test is about the highlight,
// and it replaces a real defect: the highlight was painted by the component's own onPointerEnter, so a
// keyboard user moving through a filter saw nothing at all - the outline that would otherwise have
// shown the focused row is removed by data-[highlighted]:outline-none. It waits for the listbox to be
// the thing receiving keys before pressing ArrowDown, because pressing it the instant after the click
// raced Radix's own focus move under parallel load: the key landed on the trigger, nothing was
// highlighted, and the test failed for a reason unrelated to the highlight. Flaky once in a full run
// and green every time in isolation, which is the worst shape a guard can take - it teaches people to
// re-run rather than to look. The background colour is polled rather than read once, because it is
// painted by a class and a single read can land in the frame before the style resolves.
//
// The toast enters from the edge it sits on. The viewport is pinned with a logical `end-4`, so it sits
// on the right in English and the left in Arabic. The direction is read from the custom property
// rather than from the rendered position, because that property is what the keyframes consume and a
// sign error there is invisible in a screenshot of either language alone.

import { test, expect } from '@playwright/test'

const DIALOG = '/iframe.html?id=ui-dialog--open&viewMode=story'
const SELECT = '/iframe.html?id=ui-select--default&viewMode=story'
const TOAST = '/iframe.html?id=ui-toast--default&viewMode=story'

async function openStory(page: import('@playwright/test').Page, url: string) {
  await page.goto(url, { waitUntil: 'load' })
  await page.waitForSelector('body.sb-show-main', { timeout: 15_000 })
}

test.describe('the dialog', () => {
  test('animates in, settles at full size, and unmounts when closed', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await openStory(page, DIALOG)

    const panel = page.locator('.msp-dialog')
    await expect(panel).toBeVisible()

    const animations = await panel.evaluate((el) =>
      el.getAnimations().map((a) => ({ name: (a as CSSAnimation).animationName, duration: a.effect?.getTiming().duration })),
    )
    expect(animations.map((a) => a.name)).toContain('msp-dialog-in')
    expect(Number(animations[0].duration)).toBeLessThanOrEqual(300)

    await panel.evaluate((el) => Promise.all(el.getAnimations().map((a) => a.finished)))
    const settled = await panel.evaluate((el) => {
      const style = getComputedStyle(el)
      return { transform: style.transform, opacity: style.opacity, origin: style.transformOrigin }
    })
    expect(settled.opacity).toBe('1')
    expect(settled.transform).not.toContain('0.96')
    const half = await panel.evaluate((el) => [
      (el as HTMLElement).offsetWidth / 2,
      (el as HTMLElement).offsetHeight / 2,
    ])
    const [originX, originY] = settled.origin.split(' ').map(parseFloat)
    expect(originX).toBeCloseTo(half[0], 0)
    expect(originY).toBeCloseTo(half[1], 0)

    await page.keyboard.press('Escape')
    await expect(panel).toHaveCount(0, { timeout: 5_000 })
  })

  test('still opens and closes when the reader asks for reduced motion', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await openStory(page, DIALOG)

    const panel = page.locator('.msp-dialog')
    await expect(panel).toBeVisible()
    const duration = await panel.evaluate((el) => getComputedStyle(el).animationDuration)
    expect(parseFloat(duration)).toBeLessThan(0.001)

    await page.keyboard.press('Escape')
    await expect(panel).toHaveCount(0, { timeout: 5_000 })
  })
})

test.describe('the select popover', () => {
  test('animates open and unmounts on close', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await openStory(page, SELECT)

    await page.getByRole('combobox').first().click()
    const popover = page.locator('.msp-pop')
    await expect(popover).toBeVisible()

    const names = await popover.evaluate((el) => el.getAnimations().map((a) => (a as CSSAnimation).animationName))
    expect(names).toContain('msp-pop-in')

    await page.keyboard.press('Escape')
    await expect(popover).toHaveCount(0, { timeout: 5_000 })
  })

  test('highlights the row the arrow keys are on, not only the one under the pointer', async ({ page }) => {
    await openStory(page, SELECT)
    await page.getByRole('combobox').first().click()

    await expect(page.getByRole('listbox')).toBeVisible()
    const highlighted = page.locator('.msp-option[data-highlighted]')

    await page.keyboard.press('ArrowDown')
    await expect(highlighted).toHaveCount(1)

    await expect
      .poll(async () => highlighted.evaluate((el) => getComputedStyle(el).backgroundColor))
      .not.toMatch(/rgba\(0, 0, 0, 0\)|transparent/)
  })
})

test.describe('the toast', () => {
  test('enters from the edge it sits on, in both directions', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await openStory(page, TOAST)

    await page.getByRole('button', { name: /Trigger toast/i }).click()
    const toast = page.locator('.msp-toast')
    await expect(toast).toBeVisible()

    const names = await toast.evaluate((el) => el.getAnimations().map((a) => (a as CSSAnimation).animationName))
    expect(names).toContain('msp-toast-in')

    const ltr = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--msp-toast-from').trim())
    expect(ltr).toBe('100%')
    const rtl = await page.evaluate(() => {
      document.documentElement.setAttribute('dir', 'rtl')
      return getComputedStyle(document.documentElement).getPropertyValue('--msp-toast-from').trim()
    })
    expect(rtl).toBe('-100%')
  })
})
