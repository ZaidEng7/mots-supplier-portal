# Decisions we need from the Ministry

Six questions that engineering cannot answer for itself. Each one is currently handled by taking
the reversible option and writing down why — so nothing is blocked *today*, but each is a place
where we have deliberately not guessed, and where the eventual answer may require rework.

One page, so it can be taken to a single conversation rather than reconstructed from six threads.

Last updated 29 August 2026.

---

## 1. OQ-001 — Ministry commercial visibility

**Question.** May Ministry oversight users see line-level commercial detail (individual bids,
prices, named suppliers), or only aggregate governance metrics?

**Blocks.** EPIC-18 (Ministry oversight views), EPIC-19.

**Where it already bites.** `audit.read` was removed from the `ministry_viewer` role, because a raw
audit row exposes named actors and reviewer free text at line level for every supplier. BRULE-086
grants the Ministry "read-only, cross-organization access to aggregate/governance metrics only", and
BRULE-087 defaults to aggregate-only where visibility is undecided. If the answer is that
line-level access is intended, that grant needs restoring and the oversight views need designing
around it.

---

## 2. OQ-004 — Approval hierarchy

**Question.** Who must approve an award, in what order, and above what thresholds? Is approval a
single role, a chain, or value-dependent?

**Blocks.** Award (EPIC-12), Procurement Workflow.

**Why we cannot infer it.** This is organisational policy, not a technical shape. Guessing a chain
and building it would embed an authority structure the Ministry has not agreed to.

---

## 3. OQ-009 — Evaluation template shape

**Question.** What does an evaluation actually score — fixed criteria, weighted categories,
per-RFQ templates? Are weights set centrally or per procurement?

**Blocks.** Evaluation (EPIC-10), Comparison (EPIC-11).

**Why we cannot infer it.** The data model differs substantially between a fixed rubric and a
configurable template. Building the wrong one means migrating scored evaluations later, which is
the worst time to change an evaluation schema.

---

## 4. OQ-010 — Retention and right to erasure

**Question.** Are there regulatory retention periods, or right-to-erasure obligations, that
override the current "hard delete plus audit" default? Specifically: how long must audit records be
kept, and can a person request their removal?

**Blocks.** Nothing outright, but it has already shaped a decision.

**Where it already bites.** The audit log is retained **indefinitely** in v1 (ASM-085). Because
that is unresolved, audit records now store caller IP **truncated** to /24 (IPv4) and /48 (IPv6)
rather than in full. A full address kept forever, under an open erasure question, is the most
exposed form of that data and the hardest choice to reverse — so we took the reversible one. If a
bounded retention period is defined, storing the full address becomes defensible again and is worth
revisiting, because a truncated address is less useful in a dispute.

---

## 5. BRULE-016 — Category to required-document mapping

**Question.** Which document types are required for which supplier categories? For example: does a
catering supplier need a health certificate that a transport supplier does not? Are there
exemptions, or requirements that depend on supplier type as well as category?

**Blocks.** The category-dependent half of BRULE-016. The rest of the document lifecycle work
(MSP-68) proceeds without it.

**Why we cannot infer it.** The rule already says so: it carries
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on the actual list. Today every supplier is asked
for the same required document set, regardless of category. Building the join against an invented
mapping risks building the wrong shape — if the real rule turns out to be per-category *and*
per-supplier-type, or to carry exemptions, we would have to argue with our own structure rather
than simply fill it in.

**What we need.** A table of category → required document types. Nothing more; the code seam is
small once the data exists.

---

## 6. BRULE-017 — Does a missing document block approval?

**Question.** Can a reviewer approve a supplier whose required documents are incomplete?

**Blocks.** Nothing, but two written rules currently disagree and we do not know which is correct.

**What we need.** The actual text of the decision record dated 26 August 2026. Our working
hypothesis is that the rule text changed and the code is right — but that is a hypothesis, and it
should not be resolved by whichever of us is more confident. If the decision record says the
opposite, the code needs changing rather than the document.

---

## How these are being handled meanwhile

In every case the same approach: take the option that can be reversed, and record the reasoning
where the next person will find it — in the code at the point of use, not only in a ticket.

That is why none of these is currently stopping work. It is also why the list only grows: each item
is a question that engineering is not in a position to close, and answering them is the only thing
that removes them.
