import type { StorybookConfig } from '@storybook/react-vite';

const config: StorybookConfig = {
  "stories": [
    "../src/**/*.mdx",
    "../src/**/*.stories.@(js|jsx|mjs|ts|tsx)"
  ],
  // The a11y addon is EXCLUDED from the build the e2e pass reads, and only from that build.
  //
  // It injects and runs axe in the story frame on render. `tests/e2e/storybook-axe.spec.ts` injects
  // its own axe through @axe-core/playwright, and two engines in one document collide with
  // "Axe is already running" - deterministically on portal-rendering stories and intermittently
  // elsewhere under load. `parameters.a11y.manual` used to suppress the automatic run and Storybook 10
  // dropped the option, so the suppression silently stopped working and the race came back; setting
  // `disable` instead did not stop the injection either. Both were checked by probing a built story for
  // `window.axe`, not inferred from the error text - the previous two attempts at this race were fixed
  // by inference and both came back.
  //
  // Not shipping the addon is the only version of this that cannot quietly lapse: there is no second
  // engine to race. `storybook dev` keeps it, so the panel and its live checks are unaffected, and the
  // Playwright pass remains the single authority for the gate (which it already was).
  "addons": [
    ...(process.env.STORYBOOK_A11Y_AUTORUN === 'off' ? [] : ["@storybook/addon-a11y"]),
    "@storybook/addon-docs"
  ],
  "framework": "@storybook/react-vite"
};
export default config;