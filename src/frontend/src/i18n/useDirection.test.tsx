// The browser tab carries the portal's name in the language the reader chose.
//
// It read "frontend" - the placeholder the build tool's template ships with - on every page, beside the Ministry's
// emblem. The test switches the language both ways, because a title set once at start-up would pass a single check
// and still show Arabic to somebody who had switched to English.

import { renderHook, act } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import i18n from './config'
import { useDirection } from './useDirection'

describe('useDirection', () => {
  afterEach(async () => {
    await act(async () => {
      await i18n.changeLanguage('ar')
    })
  })

  it('names the browser tab after the portal, in the language the reader chose', async () => {
    await act(async () => {
      await i18n.changeLanguage('ar')
    })
    renderHook(() => useDirection())

    expect(document.title).toBe('بوابة الموردين')

    await act(async () => {
      await i18n.changeLanguage('en')
    })

    expect(document.title).toBe('Supplier Portal')
    expect(document.documentElement.dir).toBe('ltr')
  })
})
