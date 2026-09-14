// Whether one of the two authenticated shells is on screen. AppShell says so on mount, because a shell that is on
// screen owns the bottom of the page; PublicFooter reads it and stands down while a shell is drawing its own.
//
// The public footer needs this and cannot work it out for itself. Inferring from the session is wrong: a signed-in
// reader on /about is authenticated and has no shell, and that page is reached FROM the footer, so suppressing it
// there would strand them. Inferring from the path means restating which routes each layout owns in a second place,
// where it would drift. So the shell says so, and the footer asks.
//
// Its own module rather than a second export beside the footer: a store and a component in one file is what the
// fast-refresh rule objects to, and the objection is fair - the thing a shell declares is not a piece of the footer.

import { useEffect } from 'react'
import { create } from 'zustand'

const useShellPresence = create<{ mounted: boolean; setMounted: (mounted: boolean) => void }>((set) => ({
  mounted: false,
  setMounted: (mounted) => set({ mounted }),
}))

export function useDeclareShellMounted(): void {
  const setMounted = useShellPresence((state) => state.setMounted)
  useEffect(() => {
    setMounted(true)
    return () => setMounted(false)
  }, [setMounted])
}

export function useShellMounted(): boolean {
  return useShellPresence((state) => state.mounted)
}
