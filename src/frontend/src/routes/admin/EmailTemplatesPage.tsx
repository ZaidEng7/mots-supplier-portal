import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {Badge, Button, Card, Field, Input, PageHeading, SkeletonList, useToast} from '../../components/ui'
import {
  listEmailTemplates, upsertEmailTemplate, deleteEmailTemplate,
  EmailTemplateError, type EmailTemplateRow,
} from '../../api/emailTemplates'

/**
 * T-076, `/back-office/email-templates`, `system_admin`.
 *
 * <p>Split out of T-061 for a reason that shows up on this screen: an in-app notification that loses a token
 * reads badly, and an email that loses <code>{'{verifyUrl}'}</code> locks the recipient out of the account
 * they are creating — and nothing in the system can tell, because the send succeeded and the body was valid.
 * So the required tokens are shown per template, the server refuses a save that drops one, and the refusal
 * names which token in which language.</p>
 *
 * <p>The shipped wording sits beside the editor with its tokens intact, so revert is not guesswork and an
 * administrator can see which placeholders they are allowed to use.</p>
 */
/** The server's two named refusals, and their strings. Anything else is a generic save failure. */
const REASON_KEYS: Record<string, string> = {
  MISSING_REQUIRED_TOKENS: 'emailTemplates.errors.missingTokens',
  UNKNOWN_TOKENS: 'emailTemplates.errors.unknownTokens',
}

export function EmailTemplatesPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState({ subjectAr: '', subjectEn: '', bodyAr: '', bodyEn: '' })
  const [rejectedTokens, setRejectedTokens] = useState<string[]>([])

  const templatesQuery = useQuery({ queryKey: ['email-templates'], queryFn: listEmailTemplates })

  const saveMutation = useMutation({
    mutationFn: () => upsertEmailTemplate(editing!, draft),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('emailTemplates.saved') })
      setEditing(null)
      setRejectedTokens([])
      await queryClient.invalidateQueries({ queryKey: ['email-templates'] })
    },
    onError: (error: Error) => {
      if (error instanceof EmailTemplateError) {
        setRejectedTokens(error.tokens)
        notify({
          kind: 'danger',
          title: t(REASON_KEYS[error.reason] ?? 'emailTemplates.errors.saveFailed'),
        })
        return
      }
      notify({ kind: 'danger', title: t('emailTemplates.errors.saveFailed') })
    },
  })

  const revertMutation = useMutation({
    mutationFn: (key: string) => deleteEmailTemplate(key),
    onSuccess: async () => {
      notify({ kind: 'success', title: t('emailTemplates.reverted') })
      await queryClient.invalidateQueries({ queryKey: ['email-templates'] })
    },
    onError: () => notify({ kind: 'danger', title: t('emailTemplates.errors.revertFailed') }),
  })

  const startEditing = (row: EmailTemplateRow) => {
    setEditing(row.key)
    setRejectedTokens([])
    // Pre-filled with whatever is in force — the override if there is one, the shipped copy otherwise. An
    // empty editor would make every edit a rewrite from scratch.
    setDraft(row.override ?? {
      subjectAr: row.shipped.subjectAr,
      subjectEn: row.shipped.subjectEn,
      bodyAr: row.shipped.bodyAr,
      bodyEn: row.shipped.bodyEn,
    })
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <PageHeading title={t('emailTemplates.title')} subtitle={t('emailTemplates.subtitle')} />
      </div>

      {templatesQuery.isLoading ? <SkeletonList label={t('common.loading')} /> : null}
      {templatesQuery.isError ? (
        <Card title={t('emailTemplates.title')}>
          <p>{t('emailTemplates.errors.loadFailed')}</p>
          <Button variant="ghost" onClick={() => void templatesQuery.refetch()}>{t('emailTemplates.retry')}</Button>
        </Card>
      ) : null}

      {templatesQuery.data?.map((row) => (
        <Card key={row.key} title={row.key}>
          <div className="mb-3 flex flex-wrap items-center gap-2">
            {row.override ? (
              <Badge tone="warning">{t('emailTemplates.overridden')}</Badge>
            ) : (
              <Badge tone="neutral">{t('emailTemplates.shippedWording')}</Badge>
            )}
            {/* Required first and in a stronger tone: these are the ones a save is refused for. */}
            {row.requiredTokens.map((token) => (
              <Badge key={token} tone="danger">{`{${token}}`} {t('emailTemplates.required')}</Badge>
            ))}
            {row.optionalTokens.map((token) => (
              <Badge key={token} tone="info">{`{${token}}`}</Badge>
            ))}
          </div>

          {editing === row.key ? (
            <div className="flex flex-col gap-3">
              {rejectedTokens.length > 0 ? (
                <p role="alert" style={{ color: 'var(--color-danger-fg)' }}>
                  {t('emailTemplates.tokensNamed', { tokens: rejectedTokens.join(', ') })}
                </p>
              ) : null}
              <Field label={t('emailTemplates.fields.subjectAr')} required>
                {(p) => <Input {...p} dir="rtl" value={draft.subjectAr} onChange={(e) => setDraft({ ...draft, subjectAr: e.target.value })} />}
              </Field>
              <Field label={t('emailTemplates.fields.subjectEn')} required>
                {(p) => <Input {...p} dir="ltr" value={draft.subjectEn} onChange={(e) => setDraft({ ...draft, subjectEn: e.target.value })} />}
              </Field>
              <Field label={t('emailTemplates.fields.bodyAr')} required>
                {(p) => (
                  <textarea
                    {...p}
                    dir="rtl"
                    rows={4}
                    className="w-full rounded-[var(--radius-md)] p-2 font-mono text-[length:var(--text-body-sm)]"
                    style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', color: 'var(--color-text-primary)' }}
                    value={draft.bodyAr}
                    onChange={(e) => setDraft({ ...draft, bodyAr: e.target.value })}
                  />
                )}
              </Field>
              <Field label={t('emailTemplates.fields.bodyEn')} required>
                {(p) => (
                  <textarea
                    {...p}
                    dir="ltr"
                    rows={4}
                    className="w-full rounded-[var(--radius-md)] p-2 font-mono text-[length:var(--text-body-sm)]"
                    style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', color: 'var(--color-text-primary)' }}
                    value={draft.bodyEn}
                    onChange={(e) => setDraft({ ...draft, bodyEn: e.target.value })}
                  />
                )}
              </Field>
              <div className="flex gap-2">
                <Button isLoading={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
                  {t('emailTemplates.save')}
                </Button>
                <Button variant="ghost" onClick={() => { setEditing(null); setRejectedTokens([]) }}>
                  {t('emailTemplates.cancel')}
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              <p className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                {t('emailTemplates.inForce')}
              </p>
              <p dir="rtl" style={{ color: 'var(--color-text-primary)' }}>{(row.override ?? row.shipped).subjectAr}</p>
              <p dir="ltr" style={{ color: 'var(--color-text-primary)' }}>{(row.override ?? row.shipped).subjectEn}</p>
              <div className="flex gap-2">
                <Button variant="ghost" onClick={() => startEditing(row)}>{t('emailTemplates.edit')}</Button>
                {/* Only offered where there is something to revert TO. On a shipped row it would be a button
                    that answers 404, and naming it "revert" would imply the shipped words are a change. */}
                {row.override ? (
                  <Button variant="ghost" disabled={revertMutation.isPending} onClick={() => revertMutation.mutate(row.key)}>
                    {t('emailTemplates.revert')}
                  </Button>
                ) : null}
              </div>
            </div>
          )}
        </Card>
      ))}
    </div>
  )
}
