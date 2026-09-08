// Builds the walkthrough rehearsal document. Run prepare.py first (it writes build/steps.json
// and slices the tall screenshots), then `npm install docx` and `node build.js` here.
//
// Builds the walkthrough rehearsal document: one section per step, 96 of them, grouped into the
// seventeen acts the walk actually has. Plain formatting on purpose - this is read from, not
// admired.
const fs = require('fs')
const path = require('path')
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, ImageRun, PageBreak,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle, AlignmentType,
  TableOfContents, PositionalTab, PositionalTabAlignment, PositionalTabLeader,
} = require('docx')

const BUILD = path.join(__dirname, 'build')
const steps = JSON.parse(fs.readFileSync(path.join(BUILD, 'steps.json'), 'utf8'))
const narration = JSON.parse(fs.readFileSync(path.join(__dirname, 'narration.json'), 'utf8'))

const EMU_PER_IN = 914400
const inches = (n) => Math.round(n * EMU_PER_IN)
const DXA = (n) => Math.round(n * 1440)

// ---------------------------------------------------------------- the seventeen acts

const ACTS = [
  { from: 1, to: 4, title: 'Act 1 — The public surface, and the only account that exists',
    blurb: 'The system starts with nothing in it but reference data and one administrator. These four screens are what a visitor sees, and how the one account that exists signs in.' },
  { from: 5, to: 8, title: 'Act 2 — A buying body, and the people who will run the tender',
    blurb: 'An organization is the unit a tender belongs to and the boundary every query is scoped by, so it comes first. Six members of staff are then invited by name — there is no self-service route to a ministry account.' },
  { from: 9, to: 12, title: 'Act 3 — A supplier registers',
    blurb: 'The only self-service sign-up in the product, and it always produces a supplier. Registration alone proves nothing: the account cannot sign in until the address is verified.' },
  { from: 13, to: 21, title: 'Act 4 — Onboarding: who this company is',
    blurb: 'A checklist the server enforces, filled in one section at a time: legal identity, company profile, a named contact, an address, a bank account, and what the company supplies.' },
  { from: 22, to: 25, title: 'Act 5 — Documents, and submitting the application',
    blurb: 'Each required document is uploaded with its own expiry date and virus-scanned before it is accepted. Only when the checklist is complete does the application become submittable.' },
  { from: 26, to: 31, title: 'Act 6 — The reviewer decides',
    blurb: 'A different person, a different set of screens, and a decision recorded with a written reason. The reviewer claims the case before the decision controls appear.' },
  { from: 32, to: 33, title: 'Act 7 — An offering, which is what makes a supplier findable',
    blurb: 'Approval alone does not make a company visible to buyers. Ticking a category says what it does in principle; an offering is the concrete thing it sells, and it is what invitations are drawn from.' },
  { from: 34, to: 37, title: 'Act 8 — How the bids will be scored, decided before any bid exists',
    blurb: 'The scoring template belongs to the manager, not to the officer who writes the tender. It must total one hundred and be activated before anything can bind it.' },
  { from: 38, to: 48, title: 'Act 9 — Authoring the tender',
    blurb: 'The longest act: a tender from an empty draft to something ready for review — line items, a scored requirement, the specification, the scoring template, and an invited supplier.' },
  { from: 49, to: 52, title: 'Act 10 — Approval, publication, and a window that opens itself',
    blurb: 'Authorship and approval are separate jobs, and approval and publication are separate steps. The submission window then opens on a schedule rather than when somebody presses something.' },
  { from: 53, to: 58, title: 'Act 11 — A question, and an answer everybody gets',
    blurb: 'The fairness mechanism of the whole tender: one supplier asks, and every invited supplier receives the answer at the same moment, with the asker anonymised.' },
  { from: 59, to: 63, title: 'Act 12 — The bid',
    blurb: 'Two envelopes in one form. The supplier prices the buyer’s lines, answers the requirement in both languages, sets terms, attaches a certificate and submits.' },
  { from: 64, to: 66, title: 'Act 13 — Closing submissions and opening the evaluation',
    blurb: 'Bids are fixed at close. The evaluation is created from the criteria frozen when the template was bound, and an evaluator is assigned by name.' },
  { from: 67, to: 72, title: 'Act 14 — Scoring, with the bidders anonymous',
    blurb: 'A conflict declaration before any bid is visible, then scoring against a technical criterion with the price still sealed. The financial envelope unlocks only on technical qualification.' },
  { from: 73, to: 82, title: 'Act 15 — From scores to an issued award',
    blurb: 'Consolidation, the comparison matrix, a finalized ranking, a recommendation with its justification, a self-approval refused, an approval by somebody else, and the award issued.' },
  { from: 83, to: 84, title: 'Act 16 — The outcome, and the ERP seam',
    blurb: 'What the winning supplier sees, and what happened to the integration message the award queued.' },
  { from: 85, to: 96, title: 'Act 17 — Each persona’s own screens',
    blurb: 'The screens that belong to a persona rather than to the tender: the administrator’s platform tools, the Ministry’s overview, and the supplier’s own account.' },
]

// ---------------------------------------------------------------- small helpers

const S = { body: 21, small: 19, mono: 18 }   // half-points: 10.5pt, 9.5pt, 9pt

const p = (text, opts = {}) => new Paragraph({
  spacing: { after: opts.after ?? 100, line: 264 },
  keepNext: opts.keepNext,
  alignment: opts.align,
  children: [new TextRun({ text, size: opts.size ?? S.body, bold: opts.bold, italics: opts.italics, color: opts.color, font: opts.font })],
})

const labelled = (label, text, opts = {}) => new Paragraph({
  spacing: { after: opts.after ?? 90, line: 264 },
  keepNext: opts.keepNext,
  children: [
    new TextRun({ text: label + '  ', bold: true, size: S.small, color: '333333' }),
    new TextRun({ text, size: S.body, italics: opts.italics }),
  ],
})

const heading = (text, level) => new Paragraph({ text, heading: level, spacing: { before: 240, after: 140 } })

const rule = () => new Paragraph({
  spacing: { before: 60, after: 160 },
  border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: 'BBBBBB', space: 6 } },
  children: [new TextRun({ text: '', size: 2 })],
})

function table(rows, widths, opts = {}) {
  const total = widths.reduce((a, b) => a + b, 0)
  return new Table({
    columnWidths: widths.map(DXA),
    width: { size: DXA(total), type: WidthType.DXA },
    rows: rows.map((cells, r) => new TableRow({
      tableHeader: r === 0 && opts.header !== false,
      children: cells.map((c, i) => new TableCell({
        width: { size: DXA(widths[i]), type: WidthType.DXA },
        shading: r === 0 && opts.header !== false
          ? { type: ShadingType.CLEAR, fill: 'EDEDED' } : undefined,
        margins: { top: 60, bottom: 60, left: 90, right: 90 },
        children: [new Paragraph({
          spacing: { after: 0, line: 252 },
          children: [new TextRun({
            text: String(c),
            size: S.small,
            bold: r === 0 && opts.header !== false,
            font: opts.mono && i === opts.mono ? 'Consolas' : undefined,
          })],
        })],
      })),
    })),
  })
}

const image = (part) => new Paragraph({
  spacing: { before: 120, after: 60 },
  alignment: AlignmentType.CENTER,
  children: [new ImageRun({
    type: 'png',
    data: fs.readFileSync(part.file),
    transformation: { width: inches(part.w_in), height: inches(part.h_in) },
  })],
})

const caption = (text) => new Paragraph({
  spacing: { after: 120 },
  alignment: AlignmentType.CENTER,
  children: [new TextRun({ text, size: S.mono, italics: true, color: '666666' })],
})

const pageBreak = () => new Paragraph({ children: [new PageBreak()] })

// ---------------------------------------------------------------- front matter

const front = []

front.push(new Paragraph({
  spacing: { before: 1200, after: 200 },
  children: [new TextRun({ text: 'MOTS Supplier Portal', bold: true, size: 56 })],
}))
front.push(new Paragraph({
  spacing: { after: 400 },
  children: [new TextRun({ text: 'A walk through the whole system, in 96 screens', size: 30, color: '444444' })],
}))
front.push(p('Every screenshot in this document was taken while driving the real application against a database that started empty. Nothing was seeded by a script: each supplier, tender, clarification, proposal and award below came into existence through the interface, in the order a real procurement would produce them.', { after: 200 }))
front.push(p('It is written to be presented from. Each step carries the screenshot, who is signed in, what has just happened, the next click, and one line to say out loud.', { after: 300 }))
front.push(p('Reference: walkthrough/GUIDE.md and walkthrough/screenshots/ in the repository. Reproduce the whole run with ./walkthrough/run.sh — roughly twelve minutes, most of it waiting for the five-minute job that opens the submission window.', { size: S.small, color: '555555' }))
front.push(pageBreak())

front.push(heading('Contents', HeadingLevel.HEADING_1))
front.push(p('Right-click and choose “Update field” to fill in page numbers after opening.', { size: S.small, italics: true, color: '666666', after: 160 }))
front.push(new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }))
front.push(pageBreak())

front.push(heading('The personas, and what each one does', HeadingLevel.HEADING_1))
front.push(p('Eight roles appear in this walk. Every one of them is a real account created during it.', { after: 160 }))
front.push(table([
  ['Persona', 'What they do in this walk'],
  ['system_admin', 'Creates the buying body and invites every member of staff. Owns reference data, interface text, email wording, the audit log and the operations screens. The only role for which two-factor authentication is compulsory.'],
  ['supplier_admin', 'Registers the company, completes onboarding, uploads documents, lists an offering, asks a clarification, prices and submits the bid, and sees the outcome.'],
  ['onboarding_reviewer', 'Claims the submitted application, checks each document against the profile, and approves it with a written reason. Sees applications, never tenders.'],
  ['procurement_officer', 'Authors the tender, invites suppliers, answers clarifications, closes submissions, opens and consolidates the evaluation, and recommends a winner. Cannot approve their own recommendation.'],
  ['procurement_manager', 'Owns the scoring template, approves and publishes the tender, assigns evaluators, finalizes the evaluation, and issues the award.'],
  ['procurement_manager (second)', 'Exists only because the manager who recommends may not approve. Approves the award.'],
  ['evaluator', 'Declares any conflict of interest, then scores the bids with the bidders anonymous and the prices sealed until technical qualification.'],
  ['ministry_viewer', 'Reads cross-organization totals. Deliberately holds no permission that reaches an individual tender.'],
], [1.6, 4.9]))
front.push(pageBreak())

front.push(heading('Credentials', HeadingLevel.HEADING_1))
front.push(p('These are the accounts this walk created. They are regenerated on every run of walkthrough/run.sh, and are printed into walkthrough/GUIDE.md each time.', { after: 160 }))
front.push(table([
  ['Persona', 'Email', 'Password'],
  ['system_admin (bootstrap)', 'admin@mots.local', 'motsadmin2026 + TOTP'],
  ['procurement_officer', 'officer@mots.local', 'Walkthrough2026!'],
  ['procurement_manager', 'manager@mots.local', 'Walkthrough2026!'],
  ['procurement_manager (approver)', 'manager2@mots.local', 'Walkthrough2026!'],
  ['evaluator', 'evaluator@mots.local', 'Walkthrough2026!'],
  ['onboarding_reviewer', 'reviewer@mots.local', 'Walkthrough2026!'],
  ['ministry_viewer', 'ministry@mots.local', 'Walkthrough2026!'],
  ['supplier_admin', 'supplier@gulfcatering.example', 'Walkthrough2026!'],
], [1.9, 2.6, 2.0], { mono: 1 }))
front.push(p('The administrator’s six-digit code comes from an authenticator app enrolled at first sign-in. Its secret is generated once, on the run that creates the account — so it changes every time the database is reset.', { size: S.small, color: '555555', after: 0 }))
front.push(pageBreak())

// ---------------------------------------------------------------- the walk

const body = []

for (const act of ACTS) {
  body.push(heading(act.title, HeadingLevel.HEADING_1))
  body.push(p(act.blurb, { after: 200 }))

  for (const step of steps.filter((s) => +s.num >= act.from && +s.num <= act.to)) {
    body.push(heading(`${step.num}. ${step.title}`, HeadingLevel.HEADING_2))
    body.push(labelled('Persona', step.persona, { keepNext: true }))
    body.push(labelled('Screen', step.route ? `${step.screen}  (${step.route})` : step.screen, { keepNext: true }))
    body.push(labelled('What just happened', step.happened, { keepNext: true }))
    body.push(labelled('What to do next', step.next, { keepNext: true }))
    body.push(labelled('What to say', '“' + narration[step.num] + '”', { italics: true, keepNext: true, after: 60 }))

    step.parts.forEach((part, i) => {
      if (!part.same_page && i > 0) body.push(pageBreak())
      body.push(image(part))
      if (part.label) body.push(caption(part.label))
    })

    body.push(rule())
    body.push(pageBreak())
  }
}

// ---------------------------------------------------------------- back matter

const back = []

back.push(heading('Things that look wrong and are not', HeadingLevel.HEADING_1))
back.push(p('Three answers worth having ready. Each one is a decision or an enforced rule, and each one looks like a defect the first time somebody meets it.', { after: 200 }))

back.push(heading('The Ministry dashboard shows no commercial figures', HeadingLevel.HEADING_2))
back.push(p('This is a deliberate hold, not missing work. What the Ministry may see has been referred to MOT Legal and has not been answered. BRULE-086 grants the Ministry aggregate, cross-organization access; BRULE-087 says that wherever visibility is undecided, the default is aggregate-only. So the overview carries counts and states and no bid values, no supplier names against those values, and no drill-down into an individual tender.'))
back.push(p('If asked what happens when the answer comes back: the screen gains figures, and the permission that reaches them is one row in the seed. Nothing has to be rebuilt.', { italics: true }))

back.push(heading('Uploads are refused when the virus scanner is down', HeadingLevel.HEADING_2))
back.push(p('The scanner is fail-closed by design: any failure — including the scanner being unavailable — is folded into “infected” rather than letting an unscanned file through. With clamd stopped, onboarding cannot be completed, and that is correct behaviour, not an outage in the portal.'))
back.push(p('If a demonstration machine starts refusing every upload, start the scanner before filing a bug.', { italics: true }))

back.push(heading('The orderings the product enforces, and why', HeadingLevel.HEADING_2))
back.push(p('Each of these was found by being refused during the walk. They are rules, not accidents, and any of them will look like a bug to somebody trying to skip a step.', { after: 160 }))
back.push(table([
  ['The rule', 'Why it exists'],
  ['Invitations come before approval, not after publication', 'A tender cannot be sent for internal review without at least one invited candidate — and it cannot be sent with a submission window that has already started. Approval is a judgement about a complete tender, and who it is going to is part of it.'],
  ['A supplier is only invitable once approved AND listing an offering', 'The categories ticked at sign-up say what a company does in principle. The offering is the concrete thing it sells, and it is what a buyer is actually inviting a bid on.'],
  ['A scoring template must be activated before a tender can bind it, and binding comes before review', 'A template in draft can still change. Binding freezes the criteria against the tender, so bidders and evaluators are working from the same fixed scheme — and the reviewer approving the tender can see what it will be judged by.'],
  ['The submission window opens on a schedule, not on publication', 'Publication and opening are separate so a tender can be published in advance of the date bidding starts. A job moves it when the start time passes; no person presses anything.'],
  ['The financial envelope stays locked until technical qualification', 'Two-envelope evaluation. An evaluator judges the answer before knowing the price, so the price cannot colour the technical score. The financial criterion is visible but disabled until the bid passes technically.'],
  ['The officer who recommends may not approve', 'Segregation of duties, §6.1. The recommender is offered no Approve control at all, and the endpoint refuses it as well — hiding the affordance is a courtesy, the refusal is the control.'],
], [2.3, 4.2]))

back.push(pageBreak())
back.push(heading('If something goes wrong during the demonstration', HeadingLevel.HEADING_1))
back.push(table([
  ['Symptom', 'Most likely cause'],
  ['Every document upload is refused', 'clamd is not running. Fail-closed, as above.'],
  ['“Invalid email or password” on a password you know is right', 'The sign-in rate limit answers before the password is checked. Wait a minute. The message now says so explicitly, but an older build does not.'],
  ['A tender will not move to internal review', 'It needs an invited candidate, a bound template, and a submission window that has not already started.'],
  ['The submission window has not opened', 'The job runs every five minutes. Wait for it rather than looking for a button.'],
  ['A screen shows no data at all', 'Check which persona is signed in. Almost every list here is scoped to an organization or to an invitation.'],
], [2.6, 3.9]))

// ---------------------------------------------------------------- assemble

const doc = new Document({
  creator: 'MOTS Supplier Portal',
  title: 'MOTS Supplier Portal — walkthrough',
  description: 'A 96-step walk through the whole system, from an empty database to an issued award.',
  styles: {
    default: {
      document: { run: { font: 'Calibri', size: S.body }, paragraph: { spacing: { line: 264 } } },
      heading1: { run: { font: 'Calibri', size: 32, bold: true, color: '1F3864' }, paragraph: { spacing: { before: 320, after: 160 } } },
      heading2: { run: { font: 'Calibri', size: 26, bold: true, color: '2E4B7C' }, paragraph: { spacing: { before: 240, after: 120 } } },
    },
  },
  sections: [{
    properties: { page: { margin: { top: DXA(0.8), bottom: DXA(0.7), left: DXA(0.9), right: DXA(0.9) } } },
    footers: {},
    children: [...front, ...body, ...back],
  }],
})

Packer.toBuffer(doc).then((buf) => {
  const out = path.join(BUILD, 'MOTS-Supplier-Portal-Walkthrough.docx')
  fs.writeFileSync(out, buf)
  console.log('wrote', out, (buf.length / 1024 / 1024).toFixed(1), 'MB')
})
