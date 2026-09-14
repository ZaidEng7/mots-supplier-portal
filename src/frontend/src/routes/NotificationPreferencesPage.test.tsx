// SCR-901, under D-60.
//
// The screen was refused for two batches because nothing classified the notification types (D-48 and D-52). These tests
// are mostly about the half that refusal was protecting: what a user is told they CANNOT switch off, and that the screen
// does not quietly offer to mute it.
//
// The always-on notifications are LISTED rather than hidden. D-60's four families are what a user would look for first,
// and a screen that omitted them would read as broken to exactly the reader who checked whether they could switch off an
// award notice. There is no checkbox for one: one per muteable type and none for the actionable one, because the server
// refuses it either way and a control that produced a 422 would be the screen inviting a mistake.
//
// An already-muted type shows as UNTICKED, because the checkbox reads "send me this" - getting this backwards would be a
// screen that silently invites a user to re-mute what they have already muted.
//
// Save sends the WHOLE muted set rather than one request per toggle, including both the newly-unticked type and the one
// that was already muted: the command REPLACES the stored set, so omitting the second would switch it back on without the
// user asking. Save stays disabled until something actually changes - ticked then unticked is back where it started, and
// a Save that stayed enabled would write a set identical to the stored one and report success for a change nobody made.
//
// And it says so when the preferences cannot be loaded.

import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../test/renderPage'
import { NotificationPreferencesPage } from './NotificationPreferencesPage'


const PREFERENCES = '/api/v1/notifications/preferences'

const INFORMATIONAL = {
  type: 'evaluation.evaluator_submitted', muteable: true, muted: false,
  titleAr: 'قدّم أحد المقيّمين تقييمه', titleEn: 'An evaluator submitted their scores',
}

const MUTED_ALREADY = {
  type: 'award.erp_synced', muteable: true, muted: true,
  titleAr: 'تمت مزامنة الترسية', titleEn: 'Award synced to the ERP',
}

const ACTIONABLE = {
  type: 'award.approved', muteable: false, muted: false,
  titleAr: 'تم اعتماد الترسية', titleEn: 'Award approved',
}

function fixtures(overrides?: unknown) {
  return { [PREFERENCES]: overrides ?? { types: [INFORMATIONAL, MUTED_ALREADY, ACTIONABLE] } }
}

let restore: (() => void) | undefined
afterEach(() => restore?.())

describe('NotificationPreferencesPage', () => {
  it('lists the always-on notifications instead of hiding them', async () => {
    restore = mockFetch(fixtures())

    renderPage(<NotificationPreferencesPage />)

    expect(await screen.findByText('Notifications that cannot be switched off')).toBeInTheDocument()
    expect(screen.getByText('Award approved')).toBeInTheDocument()
    expect(screen.getByText(/always sent/i)).toBeInTheDocument()
  })

  it('offers no checkbox for an always-on notification', async () => {
    restore = mockFetch(fixtures())

    renderPage(<NotificationPreferencesPage />)
    await screen.findByText('Award approved')

    expect(screen.getAllByRole('checkbox')).toHaveLength(2)
  })

  it('shows an already-muted type as unticked', async () => {
    restore = mockFetch(fixtures())

    renderPage(<NotificationPreferencesPage />)

    const alreadyMuted = await screen.findByLabelText('Award synced to the ERP')
    expect(alreadyMuted).not.toBeChecked()
    expect(screen.getByLabelText('An evaluator submitted their scores')).toBeChecked()
  })

  it('sends the whole muted set on save, not one request per toggle', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch(fixtures(), recorded)

    renderPage(<NotificationPreferencesPage />)
    await userEvent.click(await screen.findByLabelText('An evaluator submitted their scores'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const writes = recorded.filter((r) => r.method === 'PUT')
    expect(writes).toHaveLength(1)
    expect(JSON.parse(writes[0].body)).toEqual({
      mutedTypes: ['evaluation.evaluator_submitted', 'award.erp_synced'],
    })
  })

  it('keeps Save disabled until something actually changes', async () => {
    restore = mockFetch(fixtures())

    renderPage(<NotificationPreferencesPage />)
    await screen.findByText('An evaluator submitted their scores')

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

    await userEvent.click(screen.getByLabelText('An evaluator submitted their scores'))
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    await userEvent.click(screen.getByLabelText('An evaluator submitted their scores'))
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('says so when the preferences cannot be loaded', async () => {
    restore = mockFetch(fixtures({ __status: 500 }))

    renderPage(<NotificationPreferencesPage />)

    expect(await screen.findByText('Could not load your preferences')).toBeInTheDocument()
  })
})
