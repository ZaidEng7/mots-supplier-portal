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

// A request path containing "undefined" is always a defect, and never one the test asserting the
// happy path will notice.
//
// This exists because of a real one: the server sends a document's public code as `documentId` and
// the client's own interface declared it as `id`, so `latestDocument.id` was `undefined` on every
// screen that showed a document. Download, approve and reject all built a URL from it and asked the
// API for `/api/v1/documents/undefined/download-url`. The API answered 404, correctly. Nothing
// failed: TypeScript was satisfied because the type was wrong about the wire, and every test passed
// because the FIXTURES were written from that same wrong type.
//
// So the guard is not on the type - it is on the traffic. Any fetch whose path carries the literal
// "undefined" fails the test that made it, naming the URL, whatever the assertion was about.
const realFetch = globalThis.fetch
globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
  if (/\/undefined(\/|\?|$)/.test(url)) {
    throw new Error(
      `a request was built from an undefined value: ${url}\n` +
      '  Something read a field the server does not send under that name. Check the client type ' +
      'against the API response rather than against the fixture.',
    )
  }
  return realFetch(input as RequestInfo, init)
}) as typeof fetch
