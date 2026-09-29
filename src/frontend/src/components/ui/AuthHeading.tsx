// The title of a centred card that fills the viewport on its own: sign in, register, forgot password, verify email,
// accept an invitation.
//
// Why this is not PageHeading. These screens are not pages with a heading band. There is no navigation around them,
// no primary action beside the title, and nothing to align a heading against - the card IS the viewport. PageHeading
// is a justify-between row with an actions slot, sized at --text-h1 for a full-width workspace screen, and dropping
// it into a 24rem card gives a title larger than the card's own content and an alignment rule with nothing to align
// to.
//
// What it fixes. The audit counted <h1> rendering at three sizes across the product. Two of those were the
// page-title problem PageHeading now owns. The third was these five screens, which were already consistent with
// each other at --text-h3 and had no component saying so - so the next auth screen would have picked its own. This
// is that component, and the size is the one they already agreed on rather than a new opinion.
//
// THE MINISTRY'S EMBLEM SITS ABOVE THE TITLE, because on these screens nothing else says whose portal this is - there
// is no side menu and no header. Putting it here puts it on every one of them at once, and on the next one too.

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { MinistryEmblem } from './MinistryEmblem'

export function AuthHeading({ title, children }: Readonly<{ title: string; children?: ReactNode }>) {
  const { t } = useTranslation()

  return (
    <>
      <div className="mb-5 flex justify-center">
        <MinistryEmblem size={64} label={t('brand.emblem')} />
      </div>
      <h1
        className="mb-6 text-[length:var(--text-h3)] font-[var(--fw-semibold)]"
        style={{ color: 'var(--color-text-primary)' }}
      >
        {title}
      </h1>
      {children}
    </>
  )
}
