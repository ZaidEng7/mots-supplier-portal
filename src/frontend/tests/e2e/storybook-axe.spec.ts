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
    await page.goto(`/iframe.html?id=${story.id}&viewMode=story`)
    await page.waitForSelector('#storybook-root, #storybook-root *', { timeout: 10_000 }).catch(() => undefined)

    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze()

    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([])
  })
}
