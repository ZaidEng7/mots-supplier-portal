// The entry point: mount the app, start web-vitals reporting, and apply the administrator's string overrides.
//
// SCR-716's overrides are fired and NOT awaited: the app renders in its shipped language immediately and the rewordings land a
// moment later. Awaiting them would put a network round trip in front of the first paint to change some wording, and a failure
// would then delay sign-in - see overrides.ts.
//
// And again on a language switch, because the overrides are per language and the bundle for the one just switched to has not
// been fetched.

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import i18n from './i18n/config'
import { applyStringOverrides } from './i18n/overrides'
import './index.css'
import { initWebVitals } from './lib/webVitals'

initWebVitals()

void applyStringOverrides(i18n.language)
i18n.on('languageChanged', (language) => {
  void applyStringOverrides(language)
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
