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
import { Badge, Button, Card, Metric, MetricRow, PageHeading, type Tone } from '../../components/ui'
import {
  ErpPreviewError,
  previewErpImport,
  type ErpImportAction,
  type ErpImportPreviewReport,
  type ErpImportPreviewRow,
} from '../../api/erpImport'

const actionTone: Record<ErpImportAction, Tone> = {
  Create: 'success',
  Update: 'info',
  Refuse: 'danger',
}

export function ErpImportPage() {
  const { t } = useTranslation()
  const [report, setReport] = useState<ErpImportPreviewReport | null>(null)
  const [failure, setFailure] = useState<ErpPreviewError | null>(null)

  const preview = useMutation({
    mutationFn: previewErpImport,
    onSuccess: (result) => {
      setFailure(null)
      setReport(result)
    },
    onError: (error) => {
      setReport(null)
      setFailure(error instanceof ErpPreviewError ? error : new ErpPreviewError(0, null))
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

      {failure !== null && <Failure failure={failure} />}

      {report !== null && (
        <>
          <Card title={t('erpImport.summaryTitle')}>
            <MetricRow>
              <Metric label={t('erpImport.inErp')} value={report.erpSupplierCount} />
              <Metric label={t('erpImport.wouldCreate')} value={report.wouldCreate} />
              <Metric label={t('erpImport.wouldUpdate')} value={report.wouldUpdate} />
              <Metric
                label={t('erpImport.refused')}
                value={report.refused}
                tone={report.refused > 0 ? 'warning' : 'neutral'}
              />
            </MetricRow>
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

function Failure({ failure }: Readonly<{ failure: ErpPreviewError }>) {
  const { t } = useTranslation()

  if (failure.status === 503) {
    return (
      <Card title={t('erpImport.notConfiguredTitle')}>
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.notConfigured')}</p>
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
    <Card title={t('erpImport.failedTitle')}>
      <p style={{ color: 'var(--color-text-secondary)' }}>{t('erpImport.failed')}</p>
    </Card>
  )
}
