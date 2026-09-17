// The head and the tab strip every view of a tender stands inside, on every branch that view can render.
//
// The defect this closes. Each view drew TenderHeader and TenderTabs inside its own success return, so every earlier return
// drew neither: the skeleton, the failure panel, "not found", the comparison's "no proposals submitted yet", the evaluator's
// conflict-of-interest declaration and "not assigned" each filled the screen with one card or one sentence and no way back
// to the tender. Some of those are the common case rather than the rare one - every tender before its window closes has no
// submitted bid, and every evaluator meets the declaration the first time they open an assignment, where the only controls
// are declarations they cannot take back.
//
// The frame was missing only because of WHERE it was drawn. It needs nothing from the page: the head and the strip read
// their own cached answers, and the head names the tender by its reference code until the tender arrives, so a view can
// draw both before its own data has answered or after it has failed. Every return in a view goes through here instead.
//
// ACTIONS are optional and the view decides when to pass them. The tender's own view passes its lifecycle buttons only once
// the tender has loaded, because each of them is chosen by the tender's state. The declaration gate passes none: the brief
// link there would read the evaluator's workspace, and that read opens scoring before the declaration has been made.
//
// It is a component rather than a layout route around the tender's pages. A layout would move the lifecycle buttons and
// the brief link out of the heading's actions, put a second page title on the two screens that carry their own, and break
// the route collector and the heading sweep, both of which read the router one parent deep.

import type { ReactNode } from 'react'
import { TenderHeader } from './TenderHeader'
import { TenderTabs } from './TenderTabs'

export function TenderFrame({ referenceCode, actions, children }: Readonly<{
  referenceCode: string
  actions?: ReactNode
  children: ReactNode
}>) {
  return (
    <div className="flex flex-col gap-6">
      <TenderHeader referenceCode={referenceCode} actions={actions} />
      <TenderTabs referenceCode={referenceCode} />
      {children}
    </div>
  )
}
