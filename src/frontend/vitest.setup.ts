// jest-dom matchers (toBeInTheDocument, toBeDisabled, toHaveValue) for component and page
// assertions. Registered here rather than imported per test file so a missing import cannot make
// an assertion silently no-op.
import '@testing-library/jest-dom/vitest'

// Task #7/Stage C: jsdom does not implement the Pointer Events capture methods
// (Element.hasPointerCapture/setPointerCapture/releasePointerCapture) - Radix UI's Select calls
// hasPointerCapture on pointer-down, which throws "target.hasPointerCapture is not a function" in
// jsdom and aborts the interaction before React even processes the click. Real browsers implement
// these; only the test environment is missing them, so a no-op stub here (not a behavior change)
// is what every other project hitting this well-known jsdom/Radix gap does. Global, not per-test,
// since any future test driving a Select through a real click needs the same stub.
if (!Element.prototype.hasPointerCapture) {
  Element.prototype.hasPointerCapture = () => false
}
if (!Element.prototype.setPointerCapture) {
  Element.prototype.setPointerCapture = () => {}
}
if (!Element.prototype.releasePointerCapture) {
  Element.prototype.releasePointerCapture = () => {}
}
// Same jsdom gap, same Radix Select code path: it scrolls the highlighted option into view on
// open, and jsdom does not implement scrollIntoView either.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {}
}
