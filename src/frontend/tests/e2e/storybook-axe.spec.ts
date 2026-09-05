import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const dirname = path.dirname(fileURLToPath(import.meta.url))
const indexPath = path.join(dirname, '../../storybook-static/index.json')

interface StoryIndex {
  entries: Record<string, { id: string; title: string; name: string }>
}

const index: StoryIndex = JSON.parse(readFileSync(indexPath, 'utf-8'))
const stories = Object.values(index.entries)

/** Axe smoke pass against every built Storybook story (docs/backlog gap item 1:
 * component library "with axe checks passing on each"). Runs against the static build so it
 * doesn't depend on a live dev server, and reuses the same axe-core engine CI already ships. */
for (const story of stories) {
  test(`a11y: ${story.title} / ${story.name}`, async ({ page }) => {
    await page.goto(`/iframe.html?id=${story.id}&viewMode=story`, { waitUntil: 'load' })

    // MSP-79 root cause: this wait previously ended in `.catch(() => undefined)`, so a story that
    // had not finished rendering fell through to analyze() anyway. AxeBuilder then injected and ran
    // axe against a document that was still settling, and a run still in flight when the next
    // injection landed produced "Axe is already running". Waiting for the story to actually render
    // - and FAILING if it never does, rather than swallowing it - removes the race at its source
    // instead of retrying past it.
    // Storybook's own readiness signal: it swaps body from `sb-show-preparing` to `sb-show-main`
    // once the story is rendered. Deliberately NOT "#storybook-root has children" - portal-based
    // components (Radix Dialog) mount outside the root, so that check fails for them forever.
    // The first version of this fix used exactly that condition and UI/Dialog/Open timed out 6/6.
    await page.waitForSelector('body.sb-show-main', { timeout: 15_000 })

    // T-074, and this is the third attempt at the same race - so it is worth saying exactly what CI
    // showed rather than guessing again. The failure is `Error: frame.evaluate: Error: Axe is already
    // running`, on a DIFFERENT story each run, and only under CI load.
    //
    // That message comes from axe-core's own re-entrancy guard: it refuses a second `axe.run()` while one
    // is in flight in the same frame. Waiting for the story to render was necessary but not sufficient -
    // it says nothing about whether a run from a previous injection into this frame has finished.
    //
    // So the precondition is stated directly, in terms of the flag the error itself is about. When axe has
    // never been injected the check passes immediately; when a run is in flight it waits for it. No retry,
    // because a retry here would hide exactly the overlap it papers over - which is what `retries: 1` did
    // before batch 9 removed it, masking a deterministic Dialog failure for weeks.
    await page.waitForFunction(
      () => {
        const axe = (window as unknown as { axe?: { _running?: boolean } }).axe
        return axe === undefined || axe._running !== true
      },
      undefined,
      { timeout: 15_000 },
    )

    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze()

    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([])
  })
}
