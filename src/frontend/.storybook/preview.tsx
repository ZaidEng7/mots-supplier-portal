import type { Preview } from '@storybook/react-vite'
import '../src/styles/tokens.css'
import '../src/index.css'

const preview: Preview = {
  parameters: {
    controls: {
      matchers: {
       color: /(background|color)$/i,
       date: /Date$/i,
      },
    },

    // 'error' fails Storybook's own test-runner/vitest addon on any axe violation - required per
    // docs/backlog/ROADMAP.md Phase 0/1 gate: "axe checks passing".
    //
    // MSP-79: `manual` stops addon-a11y from ALSO auto-running axe inside the page on every story.
    // CI's gate is tests/e2e/storybook-axe.spec.ts, which injects its own axe via
    // @axe-core/playwright; with the addon auto-running too, two axe engines shared one document
    // and collided - "Axe is already running" - deterministically on portal-rendering stories
    // (UI/Dialog/Open failed 6/6) and intermittently elsewhere. That race was previously absorbed
    // by a `retries: 1`, which hid it rather than fixing it. One axe engine per page, and the
    // Playwright pass is the single authority. The a11y panel still runs on demand in dev.
    a11y: {
      test: 'error',
      manual: true
    }
  },
};

export default preview;