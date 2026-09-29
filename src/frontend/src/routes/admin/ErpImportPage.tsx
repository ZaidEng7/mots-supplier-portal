// What importing the ministry's ERP suppliers would do, at /back-office/erp-import, for system_admin.
//
// NOTHING RUNS UNTIL SOMEBODY PRESSES THE BUTTON. Every other listing in this product loads on arrival, and this
// one deliberately does not: it calls another ministry's server over a link nobody here controls, and a screen
// that did that on navigation would do it every time an administrator passed through - including when they only
// meant to read the last result.
//
// THE REFUSED COUNT IS THE NUMBER THE PAGE IS FOR. An operator deciding whether to import is asking one question
// - how many of these can actually become accounts - and the answer is the total minus the refusals. It is given
// its own tile, toned, and it is the only tile that changes colour, because a row of coloured tiles says nothing
// is more important than anything else.
//
// EVERY ROW SHOWS ITS REASONS IN FULL, unpaged and untruncated. Eighty rows is a list a person reads before
// agreeing to write eighty records, and the rows worth reading are the unusual ones - exactly what a page
// boundary or a "show more" would hide. The notes are the deliverable: counts say how big the job is, the notes
// say what is being agreed to.
//
// THE THREE FAILURES ARE THREE DIFFERENT SCREENS, because they are three different jobs. Nobody configured the
// integration - an administrator changes a setting. The ERP refused us - somebody asks the other team, and the
// ERP's own words are shown verbatim so they can be forwarded. Anything else is ours to fix. A single "could not
// load" would send all three to read logs, and the most likely of them is fixed by sending a message.
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
            {outcome.suspensionsHeldBack ? <HeldBack reason={outcome.suspensionsHeldBack} /> : null}
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
            {report.suspensionsHeldBack ? <HeldBack reason={report.suspensionsHeldBack} forecast /> : null}
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
// really removed those suppliers.
function HeldBack({ reason, forecast = false }: Readonly<{ reason: string; forecast?: boolean }>) {
  const { t } = useTranslation()

  return (
    <div role="status" className="mt-4 flex flex-col gap-1">
      <Badge tone="warning">{t(forecast ? 'erpImport.heldBackForecastTitle' : 'erpImport.heldBackTitle')}</Badge>
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

// A failure names the action that failed and, where the server said why, says it too. Another import already
// running is not a failure of anything - it is somebody else's run, or the hourly scheduled one - so it gets its own
// card rather than the portal-fault wording that used to tell the reader the import "did not finish".
//
// A failure names the action that failed and, where the server said why, says it too.
//
// The first version of this card said "the preview could not be produced" whichever button had been pressed, so a
// failed IMPORT read as a failed preview - and somebody who had just pressed "Run the import" could not tell
// whether anything had been written. It also dropped the server's own explanation on a 503, which matters because
// the import has two different ways of being unconfigured: no connection to the ERP, or no initial password for the
// accounts it creates. The server names which; the card now passes that on instead of guessing.
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
