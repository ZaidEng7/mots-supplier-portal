import type { ComponentType } from 'react'
import type { LucideProps } from 'lucide-react'

/**
 * Every glyph in the product, at one apparent weight.
 *
 * <p><b>Why a wrapper and not the icons directly.</b> The approved templates draw their glyphs by hand
 * on a 16-unit artboard stroked at 1.4, which is 1.31 device pixels at the sidebar's size. Lucide draws
 * on a 24-unit artboard, and its `strokeWidth` is in artboard units - so the same number renders thinner
 * as the icon grows. Four call sites choosing their own size would have produced four weights, which is
 * the thing that makes an icon set look bought rather than drawn.</p>
 *
 * <p>This converts instead: the caller says how big, and the stroke is solved for so the line lands at
 * the same {@link APPARENT_STROKE} device pixels at every size. `iconWeight` is exported so the guard
 * beside this file can check the arithmetic rather than trusting it.</p>
 *
 * <p>Icons are decoration here, never the label. Every one is `aria-hidden`, and every control that
 * carries one also carries text - or, where it cannot, an accessible name from its own props. A glyph
 * that had to be read would be a glyph that could not be translated.</p>
 */
export const APPARENT_STROKE = 1.3

/** Lucide's artboard. The conversion below is meaningless without it, so it is named, not inlined. */
const LUCIDE_ARTBOARD = 24

/** The stroke width, in artboard units, that renders {@link APPARENT_STROKE} pixels at `size`. */
export function iconWeight(size: number): number {
  return (APPARENT_STROKE * LUCIDE_ARTBOARD) / size
}

export interface IconProps {
  /** The lucide glyph to draw. */
  as: ComponentType<LucideProps>
  /** Rendered size in pixels. 15 is the navigation row; 18 is the top bar's controls. */
  size?: number
  className?: string
}

export function Icon({ as: Glyph, size = 15, className }: Readonly<IconProps>) {
  return (
    <Glyph
      size={size}
      strokeWidth={iconWeight(size)}
      className={className}
      aria-hidden="true"
      focusable="false"
    />
  )
}
