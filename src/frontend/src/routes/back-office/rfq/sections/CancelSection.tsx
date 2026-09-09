import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card } from '../../../../components/ui'
import { ReasonDialog } from '../../../../components/ReasonDialog'

/**
 * Cancelling a tender, which is irreversible.
 *
 * <p>Extracted from `RfqDetailPage` in the 6B split, and given the same treatment the supplier's
 * proposal withdrawal got in 6A, for the same reason. The audit's §D1: <i>"`Cancel RFQ` — irreversible
 * — is the last card, rendered as ghost (the lowest-emphasis variant in the system) and wrapping onto
 * two lines inside its own button."</i> An inline text field beside the quietest button this design
 * system has is not how a product should ask whether to cancel a live tender that suppliers are
 * bidding on.</p>
 *
 * <p>The draft reason lives here rather than in the route: it is this section's own working state, and
 * nothing else reads it.</p>
 */
export function CancelSection({ onCancel, isPending }: { onCancel: (reason: string) => void; isPending: boolean }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

  return (
    <Card title={t('rfq.cancelTitle')}>
      <Button variant="danger" onClick={() => setOpen(true)}>
        {t('rfq.cancelRfq')}
      </Button>
      <ReasonDialog
        open={open}
        onOpenChange={setOpen}
        onSubmit={(reason) => onCancel(reason.trim())}
        isLoading={isPending}
        title={t('rfq.cancelTitle')}
        confirmLabel={t('rfq.cancelRfq')}
        variant="danger"
        warning={t('rfq.cancelWarning')}
      />
    </Card>
  )
}
