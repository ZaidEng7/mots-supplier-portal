// An axe pass against every built Storybook story: docs/backlog gap item 1, a component library
// "with axe checks passing on each". It runs against the static build rather than a live dev
// server, and reuses the same axe-core engine CI already ships.
//
// Two waits stand before analyze(), and both exist because of a race that took three attempts.
//
// The first waits for Storybook's own readiness signal - it swaps the body class from
// `sb-show-preparing` to `sb-show-main` once the story has rendered. MSP-79's root cause was that
// this wait used to end in `.catch(() => undefined)`, so a story that never finished rendering fell
// through to analyze() anyway; axe then ran against a document that was still settling, and a run
// still in flight when the next injection landed produced "Axe is already running". Failing here
// rather than swallowing it removes the race at its source instead of retrying past it. The class
// is deliberately not "#storybook-root has children": portal-based components (Radix Dialog) mount
// outside the root, so that condition fails for them forever - the first version of this fix used
// exactly it, and UI/Dialog/Open timed out 6 runs out of 6.
//
// The second wait is T-074. CI showed `Error: frame.evaluate: Error: Axe is already running`, on a
// different story each run and only under load. That message is axe-core's own re-entrancy guard
// refusing a second `axe.run()` while one is in flight in the same frame. Waiting for the story to
// render was necessary but not sufficient - it says nothing about whether a run from a previous
// injection into this frame has finished. So the precondition is stated directly, in terms of the
// flag the error is about: when axe has never been injected the check passes immediately, and when
// a run is in flight it waits for it. No retry, because a retry would hide exactly the overlap it
// papers over - which is what `retries: 1` did before batch 9 removed it, masking a deterministic
// Dialog failure for weeks.

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

for (const story of stories) {
  test(`a11y: ${story.title} / ${story.name}`, async ({ page }) => {
    await page.goto(`/iframe.html?id=${story.id}&viewMode=story`, { waitUntil: 'load' })

    await page.waitForSelector('body.sb-show-main', { timeout: 15_000 })

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
