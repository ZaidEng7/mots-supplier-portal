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
    // ONE axe engine per page. CI's gate is tests/e2e/storybook-axe.spec.ts, which injects its own
    // axe via @axe-core/playwright; with the addon auto-running too, two engines share one document
    // and collide - "Axe is already running" - deterministically on portal-rendering stories and
    // intermittently elsewhere under load.
    //
    // `manual: true` used to stop that (MSP-79) and DOES NOT ANY MORE: Storybook 10 dropped the
    // option, so the line sat here doing nothing and the race quietly came back. Proved by probing a
    // built story - `window.axe` is present before Playwright injects anything - rather than inferred
    // from the error message, which is how the previous two attempts at this went wrong.
    //
    // `disable` here did not stop the injection either. The addon is therefore not SHIPPED in the build
    // the e2e pass reads - see .storybook/main.ts - and this parameter now only affects
    // `storybook dev`, where the developer wants the checks. Retrying past the collision was never an
    // option: a `retries: 1` here hid a deterministic Dialog failure for weeks.
    a11y: {
      test: 'error',
    }
  },
};

export default preview;