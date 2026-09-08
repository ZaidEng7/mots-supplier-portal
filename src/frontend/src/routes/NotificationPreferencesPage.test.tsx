import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockFetch, renderPage, type RecordedRequest } from '../test/renderPage'
import { NotificationPreferencesPage } from './NotificationPreferencesPage'

/**
 * SCR-901, under D-60.
 *
 * <p>The screen was refused for two batches because nothing classified the notification types (D-48/D-52).
 * These tests are mostly about the half that refusal was protecting: what a user is told they CANNOT switch
 * off, and that the screen does not quietly offer to mute it.</p>
 */

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
    // D-60's four families are what a user would look for first. A screen that omitted them would read as
    // broken to exactly the reader who checked whether they could switch off an award notice.
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

    // One checkbox per muteable type, and none for the actionable one - the server refuses it either way,
    // and a control that produced a 422 would be the screen inviting a mistake.
    expect(screen.getAllByRole('checkbox')).toHaveLength(2)
  })

  it('shows an already-muted type as unticked', async () => {
    // The checkbox reads "send me this", so muted means UNTICKED. Getting this backwards would be a screen
    // that silently invites a user to re-mute what they have already muted.
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
    // Both the newly-unticked type and the one that was already muted: the command REPLACES the stored set,
    // so omitting the second would switch it back on without the user asking.
    expect(JSON.parse(writes[0].body)).toEqual({
      mutedTypes: ['evaluation.evaluator_submitted', 'award.erp_synced'],
    })
  })

  it('keeps Save disabled until something actually changes', async () => {
    restore = mockFetch(fixtures())

    renderPage(<NotificationPreferencesPage />)
    await screen.findByText('An evaluator submitted their scores')

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

    // Ticked then unticked is back where it started, and a Save that stayed enabled would write a set
    // identical to the stored one and report success for a change nobody made.
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
