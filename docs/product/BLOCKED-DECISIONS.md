# Decisions we need from the Ministry

An index of questions engineering cannot answer for itself, and what each one is holding up.

**This page deliberately does not restate any question.** The text of each lives in exactly one
place — [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md) for `OQ-###`, [`BUSINESS-RULES.md`](./BUSINESS-RULES.md)
for `BRULE-###` — and this page references them by ID.

That is not tidiness. Two documents holding the same question have different update triggers:
`OPEN-QUESTIONS.md` gets edited when a question is *answered*, this page when something new gets
*blocked*. The day OQ-004 is answered, someone updates the first and forgets the second, and a page
whose entire purpose is telling you what to ask starts telling you to ask something already
settled. Keeping it a view means answering a question requires updating one file, and this page
keeps working with no maintenance.

*(This is not hypothetical: the first draft of this page paraphrased OQ-009 as being about
evaluation template shape. It is actually about whether a two-envelope process is required. The
copy was wrong within hours of being written.)*

What this page adds, and what exists nowhere else, is the **mapping**: which question blocks which
work, and which decisions have already been shaped by one.

Last updated 29 August 2026.

---

## What is blocked by what

| Question | Where it is written | What it is holding up |
|---|---|---|
| [OQ-001](./OPEN-QUESTIONS.md) | OPEN-QUESTIONS.md | EPIC-18 (Ministry oversight views), EPIC-19. Also the reason `audit.read` was removed from `ministry_viewer` — see below. |
| [OQ-004](./OPEN-QUESTIONS.md) | OPEN-QUESTIONS.md | Award (EPIC-12) and the procurement approval workflow. The approval engine's shape depends entirely on the answer. |
| [OQ-009](./OPEN-QUESTIONS.md) | OPEN-QUESTIONS.md | Evaluation (EPIC-10) and Comparison (EPIC-11). Affects the evaluation data model, which is the worst thing to migrate after scoring has begun. |
| [OQ-010](./OPEN-QUESTIONS.md) | OPEN-QUESTIONS.md | Nothing outright, but it has already shaped how audit records store caller IP — see below. |
| [BRULE-016](./BUSINESS-RULES.md) | BUSINESS-RULES.md | The category-dependent half of the required-document set (MSP-68). Everything else in that ticket proceeds without it. |
| [BRULE-017](./BUSINESS-RULES.md) | BUSINESS-RULES.md | Nothing, but the rule text and the implemented behaviour disagree and we do not know which is correct. |

---

## The two that need more than an ID

These are not in `OPEN-QUESTIONS.md`, so what is *needed* to close them is recorded here. The rules
themselves stay in `BUSINESS-RULES.md`.

### BRULE-016 — what we need

A table of **category → required document types**. For example: does a catering supplier need a
health certificate that a transport supplier does not? Are there exemptions, or requirements that
depend on supplier type as well as category?

The rule already carries `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on the actual list. Today
every supplier is asked for the same required set regardless of category. The code seam is small
once the data exists — we have deliberately not built the join, because inventing a mapping risks
building the wrong shape: if the real rule is per-category *and* per-supplier-type, or carries
exemptions, we would end up arguing with our own structure rather than filling it in.

### BRULE-017 — what we need

The actual text of the **decision record dated 26 August 2026**.

Two written rules currently disagree about whether a missing required document blocks approval. Our
working hypothesis is that the rule text changed and the code is correct — but that is a
hypothesis, and it should not be settled by whoever is more confident. If the decision record says
otherwise, the code changes rather than the document.

---

## Decisions already shaped by an open question

Recorded because this is the part that evaporates. Someone later sees a truncated IP address and
assumes it was arbitrary.

**OQ-010 → audit records store a truncated IP.** Caller IP is truncated to /24 (IPv4) and /48
(IPv6) rather than stored in full. The audit log is retained **indefinitely** in v1 (ASM-085), and
with retention and right-to-erasure unresolved, a full address kept forever is the most exposed
form of that data and the hardest choice to reverse. If a bounded retention period is defined, the
full address becomes defensible again and is worth revisiting — a truncated address is less useful
in a dispute. Reasoning is also recorded at the call site in `HttpAuditContext`.

**OQ-001 → `ministry_viewer` has no `audit.read`.** A raw audit row exposes named actors and
reviewer free text at line level for every supplier. BRULE-086 grants the Ministry aggregate
governance access, and BRULE-087 defaults to aggregate-only where visibility is undecided. Granting
raw audit access as an interim stand-in would grant strictly more than BRULE-086 allows. Re-add only
if OQ-001 resolves in favour of line-level access.

---

## How these are handled meanwhile

The same approach in every case: take the option that can be reversed, and record the reasoning
where the next person will find it — in the code at the point of use, not only in a ticket.

That is why none of these is stopping work today. It is also why the list only grows: each item is
a question engineering is not in a position to close.

## BRULE-023 — which document types are award-critical

**Question for the Ministry.** Expiry of an award-critical document auto-suspends the supplier. Which of the configured document types are award-critical?

**Holding up.** Nothing is blocked from shipping. The mechanism is complete and tested; `DocumentType.IsAwardCritical` defaults to false on every seeded type, so BRULE-023 currently suspends nobody.

**Why it shipped dormant rather than with a guess.** The two ways of being wrong are not symmetric. Flagging a type the Ministry would not have chosen suspends real suppliers and blocks their participation, and reactivating them later does not undo having been blocked. Flagging none leaves behaviour exactly as it is today. The answer is a data change, not a deployment.
