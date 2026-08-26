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

    // 'error' fails the CI test-runner (and Storybook's own vitest addon) on any axe
    // violation - required per docs/backlog/ROADMAP.md Phase 0/1 gate: "axe checks passing".
    a11y: {
      test: 'error'
    }
  },
};

export default preview;