// The Ministry's emblem, as it appears inside the product: the gold eagle on its own dark green square.
//
// IT IS THE FILE THE MINISTRY GAVE, not a redrawing. The square is cut from it with the emblem centred and the green
// kept, because the green is part of the mark - the same square reads on the white pages and on the dark side menu,
// where a gold-only mark would sit at low contrast on white.
//
// IT IS DECORATIVE WHERE THE NAME IS WRITTEN BESIDE IT, and named where it stands alone. In the side menu and the
// landing header the portal's name sits right next to it, so a screen reader announcing the emblem as well would say
// the same thing twice; on the sign-in card it is the only thing saying whose portal this is, so it carries the
// Ministry's name.
//
// It is served from public/ rather than imported, like the tab icons cut from the same file, so all three share one
// source of truth and the image is fetched once and cached under a stable address.

export function MinistryEmblem({ size, label }: Readonly<{ size: number; label?: string }>) {
  return (
    <img
      src="/ministry-emblem.png"
      width={size}
      height={size}
      alt={label ?? ''}
      aria-hidden={label ? undefined : true}
      className="shrink-0 rounded-[var(--radius-sm)]"
    />
  )
}
