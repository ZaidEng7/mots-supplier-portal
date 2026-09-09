# Phase 7 · Handoff

```
/make-plan Refine the MOTS Supplier Portal frontend based on a Dieter Rams audit (total 20/30).

Verdict paragraph (quoted from phase-7-03-verdict.md):
> REFINE. At 20/30 with no principle at 0 and one at 3, the bones are now good and the remaining work is
> specific rather than structural — but the pass left one of its own named objectives undone, and the
> score says so.

Keep (already strong, do NOT touch in this pass):
- Principle #7 (long-lasting) scored 3 — Evidence: the token layer preserved verbatim under the direction
  contract §2.1; warm-stone neutrals, evergreen teal, system fonts, 1px borders, the faintest shadow on
  the scale, and the one dated marker (an emoji notification icon) removed. Regression check: run
  `npx vitest run src/styles/tokenConformance.test.ts` and confirm `git diff` touches no line of
  `src/frontend/src/styles/tokens.css`.
- The eight guards, all of which assert their own denominator and carry a revert-to-red control:
  tokenConformance, pageHeadingCoverage, densityConformance, cardCoverage, listStateCoverage,
  asyncStateCoverage, dynamicKeyCoverage, keyParity. Regression check: `npx vitest run` — all must stay
  green, and none may gain an exemption without a written reason in the file.

Fix in priority order (the five moves from the audit, verbatim):
1. #2 useful — route the buyer's exits. Four raw `<a href>` full-page reloads to the bids, the comparison
   and the award sit inside `src/frontend/src/routes/back-office/RfqDetailPage.tsx`; they become TanStack
   `Link`s. Evidence: this is the one finding the direction contract named under #2 in §4 and the redesign
   pass skipped, and it is the only principle whose score did not move at all.
2. #9 environmentally friendly — lazy-load the seventeen still-static route imports in
   `src/frontend/src/router.tsx`, so a supplier stops downloading the admin and ministry screens.
   Evidence: 17 static imports, unchanged from the original audit; initial JS 218 KB gzipped.
3. #4 understandable and #5 unobtrusive — group the supplier navigation. It is still a flat list of
   top-level links repeated on every supplier screen. Evidence: original audit §D3, untouched by the
   redesign; the back-office shell already demonstrates the grouping.
4. #8 thorough — move the submit error summary in front of the controls it describes, rather than behind
   them. Evidence: original audit §F6; this is the one state item the redesign neither checked nor fixed,
   and it is why #8 scored 2 rather than 3.
5. #10 as little design as possible — finish the component adoption. `ListCard`, `LoadMore` and
   `FilterBar` are each used by four screens while the patterns they replace persist elsewhere. Evidence:
   PageHeading went 2 → 54 and Card 15 hand-rolled → 0 in this pass; these three did not.

Out of scope for this refine pass:
- The token layer. It is the only 3 on the board and it is preserved verbatim by contract.
- The domain model, the API contract, permissions and the audit trail.
- Decision A-4 (clarification answers broadcast to every invitee). Reversing it is a procurement-policy
  decision reserved for MOT procurement, not a design one.
- The two standing gates: the commercial-visibility disclosure sign-off (D-57) and the line-by-line
  Arabic read (D-65).

Deliverables for the plan:
- Per-fix: target files, exact change, verification step.
- A re-measurement step for each: the audit's numbers were taken from computed styles in a browser
  (`src/frontend/tests/e2e/capture-measure.spec.ts`, run with CAPTURE=1) and must be taken the same way.
- Regression checklist for every "Keep" item above.

Anti-patterns to guard against (specific to REFINE):
- Adding new abstractions where a direct change suffices.
- Restyling the token layer, which already scored 3.
- Scope creep into structural redesign; if structure must change, that is a new REDESIGN, not this.
- Letting fixes mutate principles outside the priority list.
- Reporting a number without re-measuring it the way the audit did.
```
