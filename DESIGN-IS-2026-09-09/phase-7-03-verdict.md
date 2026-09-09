# Phase 7 · Verdict

**REFINE.** At 20/30 with no principle at 0 and one at 3, the bones are now good and the remaining work is
specific rather than structural — but the pass left one of its own named objectives undone, and the score
says so.

## Why REFINE and not something else

The Phase 3 rule is mechanical: total ≥ 20 and no principle at 0. Both hold. The original audit's REDESIGN
verdict was earned by 12/30, and the composition, states and copy layers it scoped have been rebuilt:
eleven equal cards became four named regions with a rail, thirteen-pixel body text became each audience's
own size, fifty-one hand-rolled page titles became one component, and every screen's failure state now says
it failed and offers a way out that is proven to work.

## What the score is honest about

**#2 useful did not move.** The buyer's exits to the bids, the comparison and the award are still four raw
`<a href>` full-page reloads inside the tender screen. The direction contract listed this explicitly under
#2 in §4, the plan carried it forward, and it was never done. Every other principle moved; this one is
exactly where it started, and rounding it up would make this document the kind of instrument the whole
batch was written to remove.

**#9 did not move either**, for a smaller reason: seventeen routes are still statically imported, so every
visitor downloads the admin and ministry screens. Unchanged from the original measurement.

## The 3–5 highest-leverage moves next

1. **#2 useful — route the buyer's exits.** Four `<a href>` reloads in `RfqDetailPage.tsx` become
   TanStack `Link`s. This is the one finding the contract named and the pass skipped.
2. **#9 environmentally friendly — lazy-load the seventeen static routes** in `router.tsx`, so a supplier
   stops downloading the admin console.
3. **#4 and #5 — group the supplier navigation.** A flat list of top-level links on every screen (§D3),
   untouched by this pass; the back-office shell already shows what grouping looks like.
4. **#8 thorough — move the submit error summary in front of the controls it describes** (original §F6),
   the one state item this pass neither checked nor fixed.
5. **#10 — finish the component adoption.** `ListCard`, `LoadMore` and `FilterBar` are each used by four
   screens while the patterns they replace persist elsewhere.

## What must not be touched

Principle #7 is the only 3 on the board and it is there because the token layer was preserved verbatim
under the direction contract's §2.1. The palette, the type scale and the shadow scale are not a refinement
target. Neither are the eight guards: each asserts its own denominator and carries a revert-to-red control,
and they are what makes this score durable rather than a snapshot.
