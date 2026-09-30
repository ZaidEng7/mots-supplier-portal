// What importing the ministry's ERP suppliers would do, at /back-office/erp-import, for system_admin.
//
// NOTHING RUNS UNTIL SOMEBODY PRESSES THE BUTTON. Every other listing in this product loads on arrival, and this
// one deliberately does not: it calls another ministry's server over a link nobody here controls, and a screen
// that did that on navigation would do it every time an administrator passed through - including when they only
// meant to read the last result.
//
// THE REFUSED COUNT IS THE NUMBER THE PAGE IS FOR. An operator deciding whether to import is asking one question
// - how many of these can actually become accounts - and the answer is the total minus the refusals. It is given
// its own tile, toned. A tile takes a colour only when its count is above zero and worth a look - refused and
// suspended in the forecast; after a run those two, failed, and created in green - and the rest stay neutral,
// because a row of coloured tiles says nothing is more important than anything else.
//
// EVERY ROW SHOWS ITS REASONS IN FULL, unpaged and untruncated. Eighty rows is a list a person reads before
// agreeing to write eighty records, and the rows worth reading are the unusual ones - exactly what a page
// boundary or a "show more" would hide. The notes are the deliverable: counts say how big the job is, the notes
// say what is being agreed to.
//
// THE FOUR FAILURES ARE FOUR DIFFERENT SCREENS, because they are four different jobs. Nobody configured the
// integration - an administrator changes a setting. The ERP refused us - somebody asks the other team, and the
// ERP's own words are shown verbatim so they can be forwarded. Another import, or a supplier push to the ERP, is
// running - nothing to fix, only to wait. Anything else is ours to fix. A single "could not load" would send all four to read logs, and the
// most likely of them is fixed by sending a message.
//
// THE ERP'S ERROR TEXT IS NOT TRANSLATED. It is another system's words about its own state, and a translated
// approximation is a worse thing to forward to that system's owner than the original.

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Metric, MetricRow, PageHeading, type Tone } from '../../components/ui'
import { formatNumber } from '../../lib/datetime'
import {
  ErpPreviewError,
  previewErpImport,
  runErpImport,
  type ErpImportAction,
  type ErpImportOutcome,
  type ErpImportPreviewReport,
  type ErpImportPreviewRow,
  type ErpImportResultRow,
  type ErpImportRunReport,
} from '../../api/erpImport'

const actionTone: Record<ErpImportAction, Tone> = {
  Create: 'success',
  Update: 'info',
  Refuse: 'danger',
  Suspend: 'warning',
}

const outcomeTone: Record<ErpImportOutcome, Tone> = {
  Created: 'success',
  Updated: 'info',
  Refused: 'warning',
  Failed: 'danger',
  Suspended: 'warning',
}

export function ErpImportPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.language.startsWith('ar') ? 'ar' : 'en-GB'
  const [report, setReport] = useState<ErpImportPreviewReport | null>(null)
  const [outcome, setOutcome] = useState<ErpImportRunReport | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [failure, setFailure] = useState<{ error: ErpPreviewError; action: 'preview' | 'import' } | null>(null)

  const preview = useMutation({
    mutationFn: previewErpImport,
    onSuccess: (result) => {
      setFailure(null)
      setOutcome(null)
      setReport(result)
    },
    onError: (error) => {
      setReport(null)
      setFailure({
        error: error instanceof ErpPreviewError ? error : new ErpPreviewError(0, null),
        action: 'preview',
      })
    },
  })

  const run = useMutation({
    mutationFn: runErpImport,
    onSuccess: (result) => {
      setFailure(null)
      setConfirming(false)
      setReport(null)
      setOutcome(result)
    },
    onError: (error) => {
      setConfirming(false)
      setOutcome(null)
      setFailure({
        error: error instanceof ErpPreviewError ? error : new ErpPreviewError(0, null),
        action: 'import',
      })
    },
  })

  return (
    <div className="flex flex-col gap-6">
      <PageHeading title={t('erpImport.title')} subtitle={t('erpImport.subtitle')} />

      <Card title={t('erpImport.previewTitle')}>
        <div className="flex flex-col gap-3">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.previewHelp')}</p>
          <div>
            <Button onClick={() => preview.mutate()} disabled={preview.isPending}>
              {preview.isPending ? t('erpImport.running') : t('erpImport.run')}
            </Button>
          </div>
        </div>
      </Card>

      <Card title={t('erpImport.runTitle')}>
        <div className="flex flex-col gap-3">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.runHelp')}</p>
          <div>
            <Button onClick={() => setConfirming(true)} disabled={run.isPending}>
              {run.isPending ? t('erpImport.importing') : t('erpImport.runImport')}
            </Button>
          </div>
        </div>
      </Card>

      <Dialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('erpImport.confirmTitle')}
        description={t('erpImport.confirmBody')}
      >
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => run.mutate()} disabled={run.isPending}>
            {t('erpImport.confirmRun')}
          </Button>
          <Button variant="secondary" onClick={() => setConfirming(false)}>
            {t('erpImport.cancel')}
          </Button>
        </div>
      </Dialog>

      {failure !== null && <Failure failure={failure.error} action={failure.action} />}

      {outcome !== null && (
        <>
          <Card title={t('erpImport.outcomeTitle')}>
            <MetricRow>
              <Metric label={t('erpImport.inErp')} value={formatNumber(outcome.erpSupplierCount, locale, 0)} />
              <Metric
                label={t('erpImport.created')}
                value={formatNumber(outcome.created, locale, 0)}
                tone={outcome.created > 0 ? 'success' : 'neutral'}
              />
              <Metric label={t('erpImport.updated')} value={formatNumber(outcome.updated, locale, 0)} />
              <Metric
                label={t('erpImport.suspendedCount')}
                value={formatNumber(outcome.suspended, locale, 0)}
                tone={outcome.suspended > 0 ? 'warning' : 'neutral'}
              />
              <Metric
                label={t('erpImport.refused')}
                value={formatNumber(outcome.refused, locale, 0)}
                tone={outcome.refused > 0 ? 'warning' : 'neutral'}
              />
              <Metric
                label={t('erpImport.failedCount')}
                value={formatNumber(outcome.failed, locale, 0)}
                tone={outcome.failed > 0 ? 'danger' : 'neutral'}
              />
            </MetricRow>
            {outcome.suspensionsHeldBack ? (
              <HeldBack reason={outcome.suspensionsHeldBack} someSuspended={outcome.suspended > 0} />
            ) : null}
          </Card>

          <Card title={t('erpImport.rowsTitle')}>
            <ul className="flex flex-col gap-4" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {outcome.rows.map((row) => (
                <OutcomeRow key={row.externalId} row={row} />
              ))}
            </ul>
          </Card>
        </>
      )}

      {report !== null && (
        <>
          <Card title={t('erpImport.summaryTitle')}>
            <MetricRow>
              <Metric label={t('erpImport.inErp')} value={formatNumber(report.erpSupplierCount, locale, 0)} />
              <Metric label={t('erpImport.wouldCreate')} value={formatNumber(report.wouldCreate, locale, 0)} />
              <Metric label={t('erpImport.wouldUpdate')} value={formatNumber(report.wouldUpdate, locale, 0)} />
              <Metric
                label={t('erpImport.wouldSuspend')}
                value={formatNumber(report.wouldSuspend, locale, 0)}
                tone={report.wouldSuspend > 0 ? 'warning' : 'neutral'}
              />
              <Metric
                label={t('erpImport.refused')}
                value={formatNumber(report.refused, locale, 0)}
                tone={report.refused > 0 ? 'warning' : 'neutral'}
              />
            </MetricRow>
            {report.suspensionsHeldBack ? (
              <HeldBack reason={report.suspensionsHeldBack} someSuspended={report.wouldSuspend > 0} forecast />
            ) : null}
          </Card>

          <Card title={t('erpImport.rowsTitle')}>
            {report.rows.length === 0 ? (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.noRows')}</p>
            ) : (
              <ul className="flex flex-col gap-4" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                {report.rows.map((row) => (
                  <PreviewRow key={row.externalId} row={row} />
                ))}
              </ul>
            )}
          </Card>
        </>
      )}
    </div>
  )
}

function PreviewRow({ row }: Readonly<{ row: ErpImportPreviewRow }>) {
  const { t } = useTranslation()

  return (
    <li className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <span style={{ fontWeight: 600 }}>{row.name ?? row.externalId}</span>
        <Badge tone={actionTone[row.action]}>{t(`erpImport.action.${row.action}`)}</Badge>
        {row.matchedReferenceCode !== null && (
          <span style={{ color: 'var(--color-text-secondary)', fontFamily: 'var(--font-numeric)' }}>
            {row.matchedReferenceCode}
          </span>
        )}
      </div>
      <ul
        className="flex flex-col gap-1"
        style={{ color: 'var(--color-text-secondary)', paddingInlineStart: '1.25rem', margin: 0 }}
      >
        {row.notes.map((note) => (
          <li key={note}>{note}</li>
        ))}
      </ul>
    </li>
  )
}

// When the ERP's list looks like a broken read rather than real deletions, nothing is suspended and the reason is
// shown here in the server's own words - the numbers in it are the ones a person needs to decide whether Seven Gates
// really removed those suppliers. "Nobody was suspended" is said only when nobody was: a hold on one kind can stand
// beside suspensions of the other - a few disappearances held back while one supplier the ERP disabled is suspended.
function HeldBack({
  reason,
  someSuspended,
  forecast = false,
}: Readonly<{ reason: string; someSuspended: boolean; forecast?: boolean }>) {
  const { t } = useTranslation()
  const title = someSuspended
    ? (forecast ? 'erpImport.heldBackSomeForecastTitle' : 'erpImport.heldBackSomeTitle')
    : (forecast ? 'erpImport.heldBackForecastTitle' : 'erpImport.heldBackTitle')

  return (
    <div role="status" className="mt-4 flex flex-col gap-1">
      <Badge tone="warning">{t(title)}</Badge>
      <p style={{ color: 'var(--color-text-secondary)' }}>{reason}</p>
    </div>
  )
}

function OutcomeRow({ row }: Readonly<{ row: ErpImportResultRow }>) {
  const { t } = useTranslation()

  return (
    <li className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <span style={{ fontWeight: 600 }}>{row.name ?? row.externalId}</span>
        <Badge tone={outcomeTone[row.outcome]}>{t(`erpImport.outcome.${row.outcome}`)}</Badge>
        {row.referenceCode !== null && (
          <span style={{ color: 'var(--color-text-secondary)', fontFamily: 'var(--font-numeric)' }}>
            {row.referenceCode}
          </span>
        )}
      </div>
      <ul
        className="flex flex-col gap-1"
        style={{ color: 'var(--color-text-secondary)', paddingInlineStart: '1.25rem', margin: 0 }}
      >
        {row.notes.map((note) => (
          <li key={note}>{note}</li>
        ))}
      </ul>
    </li>
  )
}

// A failure names the action that failed and, where the server said why, says it too. A 409 is not a failure of
// anything: another import is running - somebody else's, or the hourly scheduled one - or the portal is creating an
// approved supplier in the ERP, which holds the same lock because an import in the middle of it would make a second
// portal supplier from the one it has just created. So it gets its own card, which names both, rather than the
// portal-fault wording, which would tell the reader the import "did not finish".
//
// A failed IMPORT must not read as a failed preview: somebody who has just pressed "Run the import" needs to know
// whether anything was written, which a single "the preview could not be produced" hid. A 503 shows the server's own
// detail, because the import has two different ways of being unconfigured - no connection to the ERP, or no initial
// password for the accounts it creates - and only the detail says which; the card's title and first line still
// speak of the connection whichever it is.
function Failure({ failure, action }: Readonly<{ failure: ErpPreviewError; action: 'preview' | 'import' }>) {
  const { t } = useTranslation()

  if (failure.status === 409) {
    return (
      <Card title={t('erpImport.busyTitle')}>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.busy')}</p>
      </Card>
    )
  }

  if (failure.status === 503) {
    return (
      <Card title={t('erpImport.notConfiguredTitle')}>
        <div className="flex flex-col gap-2">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.notConfigured')}</p>
          {failure.serverDetail !== null && (
            <p style={{ fontFamily: 'var(--font-mono, monospace)' }}>{failure.serverDetail}</p>
          )}
        </div>
      </Card>
    )
  }

  if (failure.status === 502) {
    return (
      <Card title={t('erpImport.erpRefusedTitle')}>
        <div className="flex flex-col gap-2">
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.erpRefused')}</p>
          {failure.serverDetail !== null && (
            <p style={{ fontFamily: 'var(--font-mono, monospace)' }}>{failure.serverDetail}</p>
          )}
        </div>
      </Card>
    )
  }

  return (
    <Card title={t(action === 'import' ? 'erpImport.importFailedTitle' : 'erpImport.failedTitle')}>
      <p style={{ color: 'var(--color-text-secondary)' }}>
        {t(action === 'import' ? 'erpImport.importFailed' : 'erpImport.failed')}
      </p>
    </Card>
  )
}
