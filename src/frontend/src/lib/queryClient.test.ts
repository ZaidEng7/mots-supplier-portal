// Task #19: invalidateQuietly exists specifically so invalidateQueries' rejection is neither a floating promise NOR silently
// dropped - it must reach console.error. Both halves are asserted: a rejecting invalidation is logged, not thrown and not
// swallowed, and a resolving one logs nothing.
//
// It is fire-and-forget by design, so the first test gives the microtask queue a turn to run the catch handler before asserting
// on it.

import { describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { invalidateQuietly } from './queryClient'

describe('invalidateQuietly', () => {
  it('logs the error when the underlying invalidateQueries call rejects', async () => {
    const client = new QueryClient()
    const failure = new Error('refetch failed')
    vi.spyOn(client, 'invalidateQueries').mockRejectedValue(failure)
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    invalidateQuietly(client, { queryKey: ['probe'] })

    await Promise.resolve()
    await Promise.resolve()

    expect(errorSpy).toHaveBeenCalledWith('Background query invalidation failed', { queryKey: ['probe'] }, failure)

    errorSpy.mockRestore()
  })

  it('logs nothing when the invalidation succeeds', async () => {
    const client = new QueryClient()
    vi.spyOn(client, 'invalidateQueries').mockResolvedValue(undefined)
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    invalidateQuietly(client, { queryKey: ['probe'] })
    await Promise.resolve()
    await Promise.resolve()

    expect(errorSpy).not.toHaveBeenCalled()

    errorSpy.mockRestore()
  })
})
