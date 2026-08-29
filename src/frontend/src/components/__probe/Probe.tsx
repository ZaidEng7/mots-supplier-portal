// MSP-95 CI PROBE - reverted in the next commit.
// A conditional hook: react/rules-of-hooks is "error", and tsc cannot see this.
import { useState } from 'react'

export function Probe({ enabled }: { enabled: boolean }) {
  if (enabled) {
    const [value] = useState('conditional')
    return <span>{value}</span>
  }
  return <span>off</span>
}
