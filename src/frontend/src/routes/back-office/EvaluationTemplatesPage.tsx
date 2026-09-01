import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import {
  listEvaluationTemplates, createEvaluationTemplate, addCriterion, activateEvaluationTemplate,
  archiveEvaluationTemplate, forkEvaluationTemplate, EvaluationTemplateApiError,
  type EvaluationTemplate, type CriterionDimension, type ScoringType,
} from '../../api/evaluationTemplates'

const DIMENSIONS: CriterionDimension[] = ['Technical', 'Commercial', 'Compliance', 'Delivery']
const SCORING_TYPES: ScoringType[] = ['Numeric', 'Scale', 'Boolean', 'Formula']

/** FEAT-11.1/FR-ADM-005, pulled forward for EPIC-07 - EPIC-07's evaluation-template binding needs
 * a real, Active template to exist. Weight-sum-must-equal-100 and immutable-once-referenced are
 * both domain invariants (EvaluationTemplate.cs); this page surfaces the exact refusal message the
 * domain raises rather than re-deriving validation client-side. */
export function EvaluationTemplatesPage() {
  const { t } = useTranslation()
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [newNameAr, setNewNameAr] = useState('')
  const [newNameEn, setNewNameEn] = useState('')
  const [criterionDraft, setCriterionDraft] = useState<Record<string, { nameAr: string; nameEn: string; dimension: CriterionDimension; weight: string; maxScore: string; scoringType: ScoringType }>>({})

  const templatesQuery = useQuery({ queryKey: ['evaluation-templates'], queryFn: listEvaluationTemplates })
  const templates = templatesQuery.data ?? []

  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof EvaluationTemplateApiError ? err.message : fallback

  const createMutation = useMutation({
    mutationFn: () => createEvaluationTemplate(newNameAr, newNameEn),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['evaluation-templates'] })
      notify({ kind: 'success', title: t('evaluationTemplates.created') })
      setCreateOpen(false)
      setNewNameAr('')
      setNewNameEn('')
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluationTemplates.errors.saveFailed')) }),
  })

  const addCriterionMutation = useMutation({
    mutationFn: ({ templateId }: { templateId: string }) => {
      const draft = criterionDraft[templateId]
      return addCriterion(templateId, {
        nameAr: draft.nameAr,
        nameEn: draft.nameEn,
        dimension: draft.dimension,
        weight: Number(draft.weight),
        maxScore: Number(draft.maxScore),
        threshold: null,
        scoringType: draft.scoringType,
        guidanceAr: null,
        guidanceEn: null,
      })
    },
    onSuccess: (_, { templateId }) => {
      invalidateQuietly(queryClient, { queryKey: ['evaluation-templates'] })
      notify({ kind: 'success', title: t('evaluationTemplates.criterionAdded') })
      setCriterionDraft((prev) => ({ ...prev, [templateId]: { nameAr: '', nameEn: '', dimension: 'Technical', weight: '', maxScore: '', scoringType: 'Numeric' } }))
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluationTemplates.errors.saveFailed')) }),
  })

  const activateMutation = useMutation({
    mutationFn: (templateId: string) => activateEvaluationTemplate(templateId),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['evaluation-templates'] })
      notify({ kind: 'success', title: t('evaluationTemplates.activated') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluationTemplates.errors.activateFailed')) }),
  })

  const archiveMutation = useMutation({
    mutationFn: (templateId: string) => archiveEvaluationTemplate(templateId),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['evaluation-templates'] })
      notify({ kind: 'success', title: t('evaluationTemplates.archived') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluationTemplates.errors.saveFailed')) }),
  })

  const forkMutation = useMutation({
    mutationFn: (templateId: string) => forkEvaluationTemplate(templateId),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['evaluation-templates'] })
      notify({ kind: 'success', title: t('evaluationTemplates.forked') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('evaluationTemplates.errors.saveFailed')) }),
  })

  const draftFor = (id: string) => criterionDraft[id] ?? { nameAr: '', nameEn: '', dimension: 'Technical' as CriterionDimension, weight: '', maxScore: '', scoringType: 'Numeric' as ScoringType }
  const setDraft = (id: string, patch: Partial<ReturnType<typeof draftFor>>) =>
    setCriterionDraft((prev) => ({ ...prev, [id]: { ...draftFor(id), ...patch } }))

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('evaluationTemplates.title')}
          </h1>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('evaluationTemplates.subtitle')}
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>{t('evaluationTemplates.add')}</Button>
      </div>

      {templates.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluationTemplates.empty')}</p>
      ) : (
        templates.map((template: EvaluationTemplate) => {
          const weightTotal = template.criteria.reduce((sum, c) => sum + c.weight, 0)
          const draft = draftFor(template.id)
          return (
            <Card key={template.id} title={`${template.nameEn} (v${template.version})`}>
              <div className="mb-3 flex items-center gap-2">
                <Badge tone={template.status === 'Active' ? 'success' : template.status === 'Archived' ? 'neutral' : 'info'}>
                  {template.status}
                </Badge>
                {template.isReferenced ? <Badge tone="warning">{t('evaluationTemplates.referenced')}</Badge> : null}
                <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
                  {t('evaluationTemplates.weightTotal', { total: weightTotal })}
                </span>
              </div>

              {template.criteria.length > 0 ? (
                <Table caption={template.nameEn}>
                  <TableHead>
                    <TableHeaderCell>{t('evaluationTemplates.fields.name')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluationTemplates.fields.dimension')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluationTemplates.fields.weight')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluationTemplates.fields.maxScore')}</TableHeaderCell>
                  </TableHead>
                  <TableBody>
                    {template.criteria.map((c) => (
                      <TableRow key={c.id}>
                        <TableCell>{c.nameEn}</TableCell>
                        <TableCell>{c.dimension}</TableCell>
                        <TableCell>{c.weight}</TableCell>
                        <TableCell>{c.maxScore}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              ) : null}

              {!template.isReferenced ? (
                <div className="mt-4 flex flex-wrap items-end gap-2">
                  <Input aria-label={t('evaluationTemplates.fields.nameEn')} placeholder={t('evaluationTemplates.fields.nameEn')}
                    value={draft.nameEn} onChange={(e) => setDraft(template.id, { nameEn: e.target.value })} />
                  <Input aria-label={t('evaluationTemplates.fields.nameAr')} placeholder={t('evaluationTemplates.fields.nameAr')}
                    value={draft.nameAr} onChange={(e) => setDraft(template.id, { nameAr: e.target.value })} />
                  <Select value={draft.dimension} onValueChange={(v) => setDraft(template.id, { dimension: v as CriterionDimension })}
                    placeholder={t('evaluationTemplates.fields.dimension')}
                    options={DIMENSIONS.map((d) => ({ value: d, label: d }))} />
                  <Select value={draft.scoringType} onValueChange={(v) => setDraft(template.id, { scoringType: v as ScoringType })}
                    placeholder={t('evaluationTemplates.fields.scoringType')}
                    options={SCORING_TYPES.map((s) => ({ value: s, label: s }))} />
                  <Input type="number" aria-label={t('evaluationTemplates.fields.weight')} placeholder={t('evaluationTemplates.fields.weight')}
                    value={draft.weight} onChange={(e) => setDraft(template.id, { weight: e.target.value })} className="w-24" />
                  <Input type="number" aria-label={t('evaluationTemplates.fields.maxScore')} placeholder={t('evaluationTemplates.fields.maxScore')}
                    value={draft.maxScore} onChange={(e) => setDraft(template.id, { maxScore: e.target.value })} className="w-24" />
                  <Button size="sm" isLoading={addCriterionMutation.isPending} onClick={() => addCriterionMutation.mutate({ templateId: template.id })}>
                    {t('evaluationTemplates.addCriterion')}
                  </Button>
                </div>
              ) : null}

              <div className="mt-4 flex gap-2">
                {template.status === 'Draft' ? (
                  <Button size="sm" variant="secondary" isLoading={activateMutation.isPending} onClick={() => activateMutation.mutate(template.id)}>
                    {t('evaluationTemplates.activate')}
                  </Button>
                ) : null}
                {template.status === 'Active' ? (
                  <Button size="sm" variant="ghost" isLoading={archiveMutation.isPending} onClick={() => archiveMutation.mutate(template.id)}>
                    {t('evaluationTemplates.archive')}
                  </Button>
                ) : null}
                {template.isReferenced ? (
                  <Button size="sm" variant="secondary" isLoading={forkMutation.isPending} onClick={() => forkMutation.mutate(template.id)}>
                    {t('evaluationTemplates.fork')}
                  </Button>
                ) : null}
              </div>
            </Card>
          )
        })
      )}

      <Dialog open={createOpen} onOpenChange={setCreateOpen} title={t('evaluationTemplates.createTitle')}>
        <form className="flex flex-col gap-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate() }} noValidate>
          <Field label={t('evaluationTemplates.fields.nameAr')} required>
            {(p) => <Input {...p} value={newNameAr} onChange={(e) => setNewNameAr(e.target.value)} />}
          </Field>
          <Field label={t('evaluationTemplates.fields.nameEn')} required>
            {(p) => <Input {...p} value={newNameEn} onChange={(e) => setNewNameEn(e.target.value)} />}
          </Field>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => setCreateOpen(false)}>{t('evaluationTemplates.cancel')}</Button>
            <Button type="submit" isLoading={createMutation.isPending}>{t('evaluationTemplates.save')}</Button>
          </div>
        </form>
      </Dialog>
    </div>
  )
}
