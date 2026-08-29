import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Dialog } from './ui'

/**
 * MSP-63: suspend / reactivate / deactivate all need a mandatory reason (BRULE-096), which is the
 * same shape as the reject flow. One dialog for all four rather than four copies - and the confirm
 * variant is a prop because deactivation is terminal and should not look like the reversible two.
 */
export function ReasonDialog({
  open, onOpenChange, onSubmit, isLoading, title, confirmLabel, variant, warning,
}: {
  open: boolean
  onOpenChange: (v: boolean) => void
  onSubmit: (reason: string) => void
  isLoading: boolean
  title: string
  confirmLabel: string
  variant: 'danger' | 'primary'
  warning?: string
}) {
  const { t } = useTranslation()
  const [reason, setReason] = useState('')
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={title}>
      <div className="flex flex-col gap-4">
        {warning ? (
          <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-danger)' }}>
            {warning}
          </p>
        ) : null}
        <textarea
          className="rounded-[0.375rem] p-2"
          style={{ border: '1px solid var(--color-border-input)', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-primary)' }}
          rows={4}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={t('review.reason')}
          aria-label={t('review.reason')}
        />
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            {t('review.cancel')}
          </Button>
          <Button variant={variant} isLoading={isLoading} disabled={!reason.trim()} onClick={() => onSubmit(reason)}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Dialog>
  )
}
