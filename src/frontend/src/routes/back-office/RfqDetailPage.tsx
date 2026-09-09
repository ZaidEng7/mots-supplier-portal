import { useState } from 'react'
import { useAuthStore } from '../../lib/authStore'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Badge, Button, Card, Dialog, Field, Input, QueryError, Select, SkeletonList, StatusChip, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../../components/ui'
import { invalidateQuietly } from '../../lib/queryClient'
import {
  getRfq, addRfqItem, removeRfqItem, addRequirement, removeRequirement, bindEvaluationTemplate,
  addRfqAttachment, removeRfqAttachment, getRfqAttachmentDownloadUrl,
  submitRfqForReview, returnRfqForEdits, approveRfq, publishRfq, closeRfqSubmission, cancelRfq,
  changeSubmissionDeadline, reassignRfq, listRfqAssignees,
  inviteSupplier, suggestInvitationCandidates, answerClarification, publishClarification, issueAddendum,
  updateRfqBasics, updateRfqItem, updateRequirement,
  RfqApiError,
} from '../../api/rfqs'
import { listEvaluationTemplates } from '../../api/evaluationTemplates'
import { fetchCategories, fetchUnitsOfMeasure } from '../../api/reference'
import {
  getEvaluation, openEvaluation, assignEvaluators, listEvaluatorCandidates, recuseEvaluator, consolidateEvaluation, finalizeEvaluation, reopenEvaluation,
  EvaluationApiError,
} from '../../api/evaluations'
import { getWorkspace } from '../../api/workspace'
import { formatDate, formatDateTime, formatNumber } from '../../lib/datetime'
import { ReasonDialog } from '../../components/ReasonDialog'
import { CancelSection } from './rfq/sections/CancelSection'

/** FEAT-07.1..07.10: the RFQ workspace. State-gated actions shown here are a UI convenience only
 * (hide, never gate, per this codebase's own established rule) - every action re-enforces its own
 * state guard server-side regardless of what this page shows. */
/**
 * An ISO instant as `<input type="datetime-local">` wants it: local wall time, no zone, no seconds.
 *
 * <p>Slicing the ISO string instead would put UTC into a control the browser reads as local, which
 * shifts every displayed deadline by the offset - three hours here, and silently.</p>
 */
function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export function RfqDetailPage() {
  const { referenceCode } = useParams({ from: '/back-office/rfqs/$referenceCode' })
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const locale = isArabic ? 'ar' : 'en-GB'
  const { notify } = useToast()
  const queryClient = useQueryClient()

  // A-7: the ownership controls. The approver nomination is on the submit-for-review action; the
  // reassignment is its own card, because it applies at every point in a tender's life rather than
  // at one transition.
  const [approverDraft, setApproverDraft] = useState('')
  const [newOwnerDraft, setNewOwnerDraft] = useState('')
  const [reassignReason, setReassignReason] = useState('')
  const [deadlineDraft, setDeadlineDraft] = useState('')
  const [deadlineReason, setDeadlineReason] = useState('')
  const [itemTitleAr, setItemTitleAr] = useState('')
  const [itemTitleEn, setItemTitleEn] = useState('')
  const [itemCategory, setItemCategory] = useState('')
  const [itemUom, setItemUom] = useState('')
  const [itemQty, setItemQty] = useState('1')
  const [reqTextAr, setReqTextAr] = useState('')
  const [reqTextEn, setReqTextEn] = useState('')
  const [reqMandatory, setReqMandatory] = useState(true)
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [returnComments, setReturnComments] = useState('')
  const [answerDrafts, setAnswerDrafts] = useState<Record<string, { text: string }>>({})
  // Which row is being corrected, and the values being typed into it. Null means "nobody is editing".
  //
  // An inline editor rather than a dialog: a correction is almost always one field on one row, and
  // the row is the context. The tender's own fields get a dialog, because there are ten of them.
  const [editingItemId, setEditingItemId] = useState<string | null>(null)
  const [editItem, setEditItem] = useState({ titleAr: '', titleEn: '', categoryCode: '', unitOfMeasureCode: '', quantity: '1' })
  const [editingRequirementId, setEditingRequirementId] = useState<string | null>(null)
  const [editRequirement, setEditRequirement] = useState({ textAr: '', textEn: '', isMandatory: true })
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [details, setDetails] = useState({
    titleAr: '', titleEn: '', currencyCode: '', submissionOpensAt: '', submissionClosesAt: '',
  })
  const [addendumTitleAr, setAddendumTitleAr] = useState('')
  const [addendumTitleEn, setAddendumTitleEn] = useState('')
  const [addendumDescAr, setAddendumDescAr] = useState('')
  const [addendumDescEn, setAddendumDescEn] = useState('')
  /**
   * The page renders every transition and lets the SERVER refuse the ones this persona cannot take - which is
   * the pattern here and a good one, because a permission list in the client is a second authority.
   *
   * This one is different: it gates a READ that exists only to feed the assign control. An officer opening this
   * page would fetch a list they cannot act on and get a 403 in the console for their trouble, so the query is
   * skipped rather than the button hidden.
   */
  const canAssignEvaluators = useAuthStore((state) => state.claims?.permissions.includes('evaluation.assign') ?? false)
  // Finalize and reopen are the MANAGER's, not the officer's - evaluation.finalize and
  // evaluation.reopen are granted to procurement_manager alone. Both buttons were rendered for
  // anyone who could see the evaluation panel, so a procurement officer was shown an enabled
  // Finalize that answered 403 every time. Same hide-never-gate rule as every other control here:
  // the endpoint re-enforces the permission regardless of what these do.
  const canFinalizeEvaluation = useAuthStore((state) => state.claims?.permissions.includes('evaluation.finalize') ?? false)
  const canReopenEvaluation = useAuthStore((state) => state.claims?.permissions.includes('evaluation.reopen') ?? false)

  const [evaluatorUserId, setEvaluatorUserId] = useState('')
  const [recuseReason, setRecuseReason] = useState('')
  const [recuseTargetId, setRecuseTargetId] = useState('')
  const [reopenReason, setReopenReason] = useState('')

  const rfqQuery = useQuery({ queryKey: ['rfq', referenceCode], queryFn: () => getRfq(referenceCode) })
  const workspaceQuery = useQuery({ queryKey: ['workspace', referenceCode], queryFn: () => getWorkspace(referenceCode) })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const unitsQuery = useQuery({ queryKey: ['units-of-measure'], queryFn: fetchUnitsOfMeasure })
  const templatesQuery = useQuery({ queryKey: ['evaluation-templates'], queryFn: listEvaluationTemplates })
  const candidatesQuery = useQuery({
    queryKey: ['rfq-candidates', referenceCode],
    queryFn: () => suggestInvitationCandidates(referenceCode),
  })
  const rfq = rfqQuery.data
  /**
   * Where the evaluation panel is shown - and it used to be two states, which was a dead end.
   *
   * <p>Consolidating advances the RFQ to Shortlisting. The panel disappeared at that point, taking FINALIZE
   * with it - and recommending an award refuses until the evaluation is finalized ("Cannot recommend an award:
   * the evaluation has not been finalized"). So a tender that had been scored and consolidated could not be
   * carried any further through the UI at all. Found by walking one: the award screen offered a winner and the
   * server refused, with the button that would have unblocked it on a panel no longer rendered.</p>
   *
   * <p>Every state from SubmissionClosed onward that is not terminal: the evaluation still exists, its results
   * are still what the award is being decided from, and Reopen is still a legitimate action. Cancelled and
   * Completed are excluded - nothing is left to do to an evaluation on either.</p>
   */
  const evaluationEligible = !!rfq && [
    'SubmissionClosed', 'UnderEvaluation', 'Clarification', 'Shortlisting',
    'Recommendation', 'AwardApproval', 'Awarded',
  ].includes(rfq.state)
  const evaluationQuery = useQuery({
    queryKey: ['evaluation', referenceCode],
    queryFn: () => getEvaluation(referenceCode),
    enabled: evaluationEligible,
  })

  /**
   * Who this manager may assign. Fetched only when an evaluation can exist, and only for a caller who may
   * assign - the endpoint is behind evaluation.assign, so an officer opening this page would get a 403 for a
   * list they cannot act on.
   */
  const evaluatorCandidatesQuery = useQuery({
    queryKey: ['evaluator-candidates', referenceCode],
    queryFn: () => listEvaluatorCandidates(referenceCode),
    enabled: evaluationEligible && canAssignEvaluators,
  })

  const categories = categoriesQuery.data ?? []
  const units = unitsQuery.data ?? []
  const activeTemplates = (templatesQuery.data ?? []).filter((tpl) => tpl.status === 'Active')
  const candidates = candidatesQuery.data ?? []
  const evaluation = evaluationQuery.data ?? null

  const errorMessage = (err: unknown, fallback: string) =>
    err instanceof RfqApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof RfqApiError ? err.message : fallback
  const invalidate = () => {
    invalidateQuietly(queryClient, { queryKey: ['rfq', referenceCode] })
    invalidateQuietly(queryClient, { queryKey: ['workspace', referenceCode] })
  }

  const addItemMutation = useMutation({
    mutationFn: () => addRfqItem(referenceCode, {
      titleAr: itemTitleAr, titleEn: itemTitleEn, specificationAr: null, specificationEn: null,
      categoryCode: itemCategory, quantity: Number(itemQty), unitOfMeasureCode: itemUom, isUnitPrice: true, isOptional: false,
    }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.itemAdded') }); setItemTitleAr(''); setItemTitleEn(''); setItemQty('1') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const updateItemMutation = useMutation({
    mutationFn: () => updateRfqItem(referenceCode, editingItemId!, {
      titleAr: editItem.titleAr, titleEn: editItem.titleEn, specificationAr: null, specificationEn: null,
      categoryCode: editItem.categoryCode, quantity: Number(editItem.quantity),
      unitOfMeasureCode: editItem.unitOfMeasureCode, isUnitPrice: true, isOptional: false,
    }),
    onSuccess: () => { invalidate(); setEditingItemId(null); notify({ kind: 'success', title: t('rfq.itemUpdated') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const removeItemMutation = useMutation({
    mutationFn: (itemId: string) => removeRfqItem(referenceCode, itemId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const addRequirementMutation = useMutation({
    mutationFn: () => addRequirement(referenceCode, { textAr: reqTextAr, textEn: reqTextEn, isMandatory: reqMandatory, documentTypeCode: null }),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.requirementAdded') }); setReqTextAr(''); setReqTextEn('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const updateRequirementMutation = useMutation({
    mutationFn: () => updateRequirement(referenceCode, editingRequirementId!, {
      textAr: editRequirement.textAr, textEn: editRequirement.textEn,
      isMandatory: editRequirement.isMandatory, documentTypeCode: null,
    }),
    onSuccess: () => { invalidate(); setEditingRequirementId(null); notify({ kind: 'success', title: t('rfq.requirementUpdated') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const removeRequirementMutation = useMutation({
    mutationFn: (requirementId: string) => removeRequirement(referenceCode, requirementId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  // SCR-414. The upload/remove/download endpoints have existed since EPIC-07 and nothing on any
  // screen called them, so the tender documents could only be attached through the API. Found by the
  // per-screen sweep (batch 9 phase 12a).
  const addAttachmentMutation = useMutation({
    mutationFn: (file: File) => addRfqAttachment(referenceCode, file),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.attachments.added') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const removeAttachmentMutation = useMutation({
    mutationFn: (attachmentId: string) => removeRfqAttachment(referenceCode, attachmentId),
    onSuccess: () => invalidate(),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const downloadAttachmentMutation = useMutation({
    // Short-lived and issued per request (D-16), so the URL is fetched on the click rather than
    // rendered into the page where it would outlive its own validity.
    mutationFn: (attachmentId: string) => getRfqAttachmentDownloadUrl(referenceCode, attachmentId),
    onSuccess: (url) => window.open(url, '_blank', 'noopener,noreferrer'),
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const bindTemplateMutation = useMutation({
    mutationFn: () => bindEvaluationTemplate(referenceCode, selectedTemplateId),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.templateBound') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const submitMutation = useMutation({
    // Empty string means "named nobody", which the server reads as the manager pool - not a defect,
    // see Rfq.SubmitForReview on why there is no routing rule to fall back on.
    mutationFn: () => submitRfqForReview(referenceCode, approverDraft || undefined),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.submitted') }); setApproverDraft('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  // A-7. Fetched for every buyer viewing the RFQ rather than only when a picker opens, because both
  // pickers need it and neither is behind a click that could prefetch it in time.
  const assigneesQuery = useQuery({
    queryKey: ['rfq-assignees', referenceCode],
    queryFn: () => listRfqAssignees(referenceCode),
  })

  const reassignMutation = useMutation({
    mutationFn: () => reassignRfq(referenceCode, newOwnerDraft, reassignReason),
    onSuccess: () => {
      invalidate()
      // The list's owner column and the "mine" filter both read from a different query.
      invalidateQuietly(queryClient, { queryKey: ['rfqs'] })
      notify({ kind: 'success', title: t('rfq.ownership.reassigned') })
      setNewOwnerDraft(''); setReassignReason('')
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const returnMutation = useMutation({
    mutationFn: () => returnRfqForEdits(referenceCode, returnComments),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.returned') }); setReturnComments('') },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const approveMutation = useMutation({
    mutationFn: () => approveRfq(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.approved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const publishMutation = useMutation({
    mutationFn: () => publishRfq(referenceCode),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.published') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  // The officer's own words, not a constant.
  //
  // The aggregate refuses an early close without a reason, because closing bidding before the
  // advertised deadline is a decision bidders can challenge and the answer has to be on the record.
  // This sent a fixed translated string, so every early close in the system carried the same sentence
  // and the audit trail said nothing about why - satisfying the rule while defeating it.
  const [closeOpen, setCloseOpen] = useState(false)
  const closeMutation = useMutation({
    mutationFn: (reason: string) => closeRfqSubmission(referenceCode, reason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.closed') }); setCloseOpen(false) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  // T-018: one control for both directions. The server decides which permission applies from the
  // direction, so the UI does not have to know the caller's role - a 403 is surfaced as "not your
  // direction" rather than hidden, because an officer who cannot shorten needs to know why.
  const deadlineMutation = useMutation({
    mutationFn: ({ deadline, reason }: { deadline: string; reason: string }) =>
      changeSubmissionDeadline(referenceCode, new Date(deadline).toISOString(), reason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.deadline.changed') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.deadline.failed')) }),
  })

  const inviteMutation = useMutation({
    mutationFn: (supplierId: string) => inviteSupplier(referenceCode, supplierId),
    onSuccess: () => {
      invalidate()
      invalidateQuietly(queryClient, { queryKey: ['rfq-candidates', referenceCode] })
      notify({ kind: 'success', title: t('rfq.invitations.invited') })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.invitations.errors.inviteFailed')) }),
  })

  const answerMutation = useMutation({
    mutationFn: ({ clarificationId, answer }: { clarificationId: string; answer: string }) =>
      answerClarification(referenceCode, clarificationId, answer),
    onSuccess: (_, { clarificationId }) => {
      invalidate()
      notify({ kind: 'success', title: t('rfq.clarifications.answered') })
      setAnswerDrafts((prev) => { const next = { ...prev }; delete next[clarificationId]; return next })
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  const publishClarificationMutation = useMutation({
    mutationFn: (clarificationId: string) => publishClarification(referenceCode, clarificationId),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.clarifications.published') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.clarifications.errors.answerFailed')) }),
  })

  // F-4: PUT /rfqs/{code} and updateRfqBasics both existed and nothing called either, so a tender
  // created with the wrong submission window - the easiest mistake on that form, both dates typed by
  // hand - could only be recovered by cancelling the tender and authoring it again.
  const detailsMutation = useMutation({
    mutationFn: () => updateRfqBasics(referenceCode, {
      titleAr: details.titleAr,
      titleEn: details.titleEn,
      descriptionAr: rfq?.descriptionAr ?? null,
      descriptionEn: rfq?.descriptionEn ?? null,
      currencyCode: details.currencyCode,
      publishAt: rfq?.publishAt ?? null,
      submissionOpensAt: details.submissionOpensAt ? new Date(details.submissionOpensAt).toISOString() : null,
      submissionClosesAt: details.submissionClosesAt ? new Date(details.submissionClosesAt).toISOString() : null,
      clarificationDeadlineAt: rfq?.clarificationDeadlineAt ?? null,
      evaluationTargetDate: rfq?.evaluationTargetDate ?? null,
    }),
    onSuccess: () => { invalidate(); setDetailsOpen(false); notify({ kind: 'success', title: t('rfq.detailsSaved') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.saveFailed')) }),
  })

  const addendumMutation = useMutation({
    mutationFn: () => issueAddendum(referenceCode, {
      titleAr: addendumTitleAr, titleEn: addendumTitleEn, descriptionAr: addendumDescAr, descriptionEn: addendumDescEn,
    }),
    onSuccess: () => {
      invalidate()
      notify({ kind: 'success', title: t('rfq.addenda.issued') })
      setAddendumTitleAr(''); setAddendumTitleEn(''); setAddendumDescAr(''); setAddendumDescEn('')
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.addenda.errors.issueFailed')) }),
  })

  const cancelMutation = useMutation({
    // The reason comes from the section's dialog rather than from state up here: it is that section's
    // own working value and nothing else reads it.
    mutationFn: (reason: string) => cancelRfq(referenceCode, reason),
    onSuccess: () => { invalidate(); notify({ kind: 'success', title: t('rfq.cancelled') }) },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('rfq.errors.transitionFailed')) }),
  })

  const evaluationErrorMessage = (err: unknown, fallback: string) =>
    err instanceof EvaluationApiError && err.isConcurrencyConflict ? t('common.concurrencyConflict') : err instanceof EvaluationApiError ? err.message : fallback
  const invalidateEvaluation = () => {
    invalidateQuietly(queryClient, { queryKey: ['evaluation', referenceCode] })
    invalidateQuietly(queryClient, { queryKey: ['workspace', referenceCode] })
  }

  const openEvaluationMutation = useMutation({
    mutationFn: () => openEvaluation(referenceCode),
    onSuccess: () => { invalidate(); invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.opened') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const assignEvaluatorsMutation = useMutation({
    mutationFn: () => assignEvaluators(referenceCode, [evaluatorUserId]),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.assigned') }); setEvaluatorUserId('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const recuseEvaluatorMutation = useMutation({
    mutationFn: () => recuseEvaluator(referenceCode, recuseTargetId, recuseReason),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.recused') }); setRecuseReason(''); setRecuseTargetId('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const consolidateMutation = useMutation({
    mutationFn: () => consolidateEvaluation(referenceCode),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.consolidated') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const finalizeMutation = useMutation({
    mutationFn: () => finalizeEvaluation(referenceCode),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.finalized') }) },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  const reopenEvaluationMutation = useMutation({
    mutationFn: () => reopenEvaluation(referenceCode, reopenReason),
    onSuccess: () => { invalidateEvaluation(); notify({ kind: 'success', title: t('evaluation.reopened') }); setReopenReason('') },
    onError: (err) => notify({ kind: 'danger', title: evaluationErrorMessage(err, t('evaluation.errors.actionFailed')) }),
  })

  // A tender that failed to load is not a tender that does not exist, and the branch below says
  // "not found" for both.
  if (rfqQuery.isError) return <QueryError error={rfqQuery.error} onRetry={() => void rfqQuery.refetch()} />

  if (rfqQuery.isLoading || !rfq) {
    return <SkeletonList label={t('common.loading')} />
  }

  const isDraft = rfq.state === 'Draft'
  const isInternalReview = rfq.state === 'InternalReview'
  const isApproved = rfq.state === 'Approved'
  const isSubmissionOpen = rfq.state === 'SubmissionOpen'
  const canCancel = !['Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const canInvite = !['SubmissionClosed', 'UnderEvaluation', 'Clarification', 'Shortlisting', 'Recommendation', 'AwardApproval', 'Awarded', 'Completed', 'Cancelled'].includes(rfq.state)
  const invitedSupplierIds = new Set(rfq.invitations.map((i) => i.supplierId))
  const uninvitedCandidates = candidates.filter((c) => !invitedSupplierIds.has(c.supplierId))
  const canIssueAddendum = rfq.state === 'Published' || rfq.state === 'SubmissionOpen'
  const draftFor = (id: string) => answerDrafts[id] ?? { text: '' }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {rfq.referenceCode} — {isArabic ? rfq.titleAr : rfq.titleEn}
          </h1>
          <StatusChip machine="rfq" value={rfq.state} />
          {/* A-7: who is answerable, on the screen rather than only in the audit trail. */}
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.ownership.ownerLabel')}: {rfq.ownerName ?? t('rfq.unassigned')}
            {rfq.assignedApproverName ? ` · ${t('rfq.ownership.approverLabel')}: ${rfq.assignedApproverName}` : ''}
          </p>
        </div>
        <div className="flex gap-2">
          {isDraft ? (
            <div className="flex flex-wrap items-center gap-2">
              {/* Optional by design: an empty selection submits to the manager pool, exactly as this
                  transition behaved before A-7. The placeholder says so rather than reading as an
                  unfilled required field.

                  §C2.4: what the placeholder could not say is that the choice is committed by the
                  button NEXT to it rather than by this control, so an officer could pick an approver,
                  never press Submit, and reasonably believe they had nominated somebody. The hint is
                  wired through aria-describedby rather than left as an adjacent paragraph, so it
                  reaches a screen reader as part of the control. */}
              <Select
                aria-label={t('rfq.ownership.nominateApprover')}
                aria-describedby="rfq-approver-hint"
                placeholder={t('rfq.ownership.anyManager')}
                value={approverDraft}
                onValueChange={setApproverDraft}
                options={(assigneesQuery.data?.approvers ?? []).map((a) => ({ value: a.userId, label: a.fullName }))}
              />
              <Button isLoading={submitMutation.isPending} onClick={() => submitMutation.mutate()}>{t('rfq.submitForReview')}</Button>
              <p
                id="rfq-approver-hint"
                className="basis-full text-[length:var(--text-caption)]"
                style={{ color: 'var(--color-text-secondary)' }}
              >
                {t('rfq.ownership.approverHint')}
              </p>
            </div>
          ) : null}
          {isInternalReview ? (
            <Button isLoading={approveMutation.isPending} onClick={() => approveMutation.mutate()}>{t('rfq.approve')}</Button>
          ) : null}
          {isApproved ? (
            <Button isLoading={publishMutation.isPending} onClick={() => publishMutation.mutate()}>{t('rfq.publish')}</Button>
          ) : null}
          {isSubmissionOpen ? (
            <>
              {/* window.prompt rendered browser chrome in a product where every other reason field is
                  themed, and - the part that mattered - it said nothing at all when it was dismissed or
                  filled with spaces, while the copy promised the reason was recorded. The same dialog
                  the rest of this product uses for a mandatory reason refuses an empty one visibly. */}
              <Button variant="secondary" onClick={() => setCloseOpen(true)}>
                {t('rfq.closeSubmission')}
              </Button>
              <ReasonDialog
                open={closeOpen}
                onOpenChange={setCloseOpen}
                onSubmit={(reason) => closeMutation.mutate(reason.trim())}
                isLoading={closeMutation.isPending}
                title={t('rfq.closeSubmission')}
                confirmLabel={t('rfq.closeSubmission')}
                variant="danger"
                warning={t('rfq.closeReasonPrompt')}
              />
            </>
          ) : null}
        </div>
      </div>

      {/* A-7. Shown for every non-closed state rather than only Draft: ownership moves when people
          do, not when a tender does. Hide-never-gate as everywhere else - the endpoint re-enforces
          rfq.reassign, which officers do not hold, so an officer sees this card and gets a 403 rather
          than being told the control does not exist. */}
      {canCancel ? (
        <Card title={t('rfq.ownership.title')}>
          <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.ownership.help')}
          </p>
          <div className="flex flex-wrap items-end gap-2">
            <Select
              aria-label={t('rfq.ownership.newOwner')}
              placeholder={t('rfq.ownership.newOwner')}
              value={newOwnerDraft}
              onValueChange={setNewOwnerDraft}
              options={(assigneesQuery.data?.owners ?? []).map((o) => ({ value: o.userId, label: o.fullName }))}
            />
            {/* Mandatory. The audit row is the whole point of this operation, and a row saying only
                that ownership moved answers nothing a month later. */}
            <Input
              aria-label={t('rfq.ownership.reason')}
              placeholder={t('rfq.ownership.reason')}
              value={reassignReason}
              onChange={(e) => setReassignReason(e.target.value)}
            />
            <Button
              variant="secondary"
              isLoading={reassignMutation.isPending}
              disabled={!newOwnerDraft || !reassignReason}
              onClick={() => reassignMutation.mutate()}
            >
              {t('rfq.ownership.reassign')}
            </Button>
          </div>
        </Card>
      ) : null}

      {/*
        * F-4: the tender's own fields, editable while Draft - which is what Draft is documented to mean.
        *
        * `PUT /api/v1/rfqs/{code}` and `updateRfqBasics` both existed and nothing called either, so an
        * officer who typed the submission window wrongly at creation had two options: cancel the tender
        * and author it again, or ask an engineer to issue the PUT. Both happened during the walkthrough.
        *
        * Draft only, and that is the domain's rule rather than this screen's: UpdateBasics calls
        * EnsureDraftEditable, because bidders price against what they were shown.
        */}
      {isDraft ? (
        <Card title={t('rfq.details.title')}>
          <p className="mb-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.details.help')}
          </p>
          <Button variant="secondary" onClick={() => {
            setDetails({
              titleAr: rfq.titleAr,
              titleEn: rfq.titleEn,
              currencyCode: rfq.currencyCode,
              submissionOpensAt: toLocalInput(rfq.submissionOpensAt),
              submissionClosesAt: toLocalInput(rfq.submissionClosesAt),
            })
            setDetailsOpen(true)
          }}>{t('rfq.details.edit')}</Button>

          <Dialog open={detailsOpen} onOpenChange={setDetailsOpen} title={t('rfq.details.title')}>
            <div className="flex flex-col gap-3">
              <Field label={t('rfq.fields.titleEn')}>
                {(inputProps) => (
                  <Input {...inputProps} value={details.titleEn} onChange={(e) => setDetails((p) => ({ ...p, titleEn: e.target.value }))} />
                )}
              </Field>
              <Field label={t('rfq.fields.titleAr')}>
                {(inputProps) => (
                  <Input {...inputProps} value={details.titleAr} onChange={(e) => setDetails((p) => ({ ...p, titleAr: e.target.value }))} />
                )}
              </Field>
              <Field label={t('rfq.fields.currency')}>
                {(inputProps) => (
                  <Input {...inputProps} value={details.currencyCode} onChange={(e) => setDetails((p) => ({ ...p, currencyCode: e.target.value }))} />
                )}
              </Field>
              <Field label={t('rfq.fields.submissionOpensAt')}>
                {(inputProps) => (
                  <Input {...inputProps} type="datetime-local" value={details.submissionOpensAt}
                    onChange={(e) => setDetails((p) => ({ ...p, submissionOpensAt: e.target.value }))} />
                )}
              </Field>
              <Field label={t('rfq.fields.submissionClosesAt')}>
                {(inputProps) => (
                  <Input {...inputProps} type="datetime-local" value={details.submissionClosesAt}
                    onChange={(e) => setDetails((p) => ({ ...p, submissionClosesAt: e.target.value }))} />
                )}
              </Field>
              <div className="mt-2 flex justify-end gap-2">
                <Button variant="ghost" onClick={() => setDetailsOpen(false)}>{t('rfq.cancelEdit')}</Button>
                <Button isLoading={detailsMutation.isPending} onClick={() => detailsMutation.mutate()}>{t('rfq.save')}</Button>
              </div>
            </div>
          </Dialog>
        </Card>
      ) : null}

      {/* T-018/BRULE-035: changeable while Published or SubmissionOpen, the same two states the
          domain accepts. Same gate as the addendum control above, and for the same reason. */}
      {canIssueAddendum ? (
        <Card title={t('rfq.deadline.title')}>
          <p className="mb-2 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.deadline.help')}
          </p>
          <div className="flex flex-wrap items-end gap-2">
            <Input
              type="datetime-local"
              aria-label={t('rfq.deadline.newDeadline')}
              value={deadlineDraft}
              onChange={(e) => setDeadlineDraft(e.target.value)}
            />
            {/* A-6: mandatory. A deadline moved with no stated basis is what the ruling exists to
                prevent, and the supplier reads this on their own view of the RFQ. */}
            <Input
              aria-label={t('rfq.deadline.reason')}
              placeholder={t('rfq.deadline.reason')}
              value={deadlineReason}
              onChange={(e) => setDeadlineReason(e.target.value)}
            />
            <Button
              variant="secondary"
              isLoading={deadlineMutation.isPending}
              disabled={!deadlineDraft || !deadlineReason}
              onClick={() => deadlineMutation.mutate({ deadline: deadlineDraft, reason: deadlineReason })}
            >
              {t('rfq.deadline.apply')}
            </Button>
          </div>
        </Card>
      ) : null}

      {workspaceQuery.data ? (
        <Card title={t('workspace.title')}>
          {workspaceQuery.data.isCancelled ? (
            <Badge tone="danger">{t('workspace.cancelledBanner')}</Badge>
          ) : (
            <div className="flex flex-col gap-4">
              <div className="flex flex-wrap gap-2" aria-label={t('workspace.stages')}>
                {workspaceQuery.data.stages.map((stage) => (
                  <span key={stage.key} className="inline-flex items-center gap-1">
                    {stage.isCompleted ? <span aria-hidden="true">✓</span> : null}
                    <StatusChip machine="rfq" value={stage.key} tone={stage.isCurrent ? 'brand' : stage.isCompleted ? 'success' : 'neutral'} />
                  </span>
                ))}
              </div>
              {workspaceQuery.data.nextActions.length > 0 ? (
                <div className="flex flex-col gap-2">
                  {workspaceQuery.data.nextActions.map((action) => (
                    <div key={action.action} className="flex items-center gap-2 flex-wrap">
                      <Badge tone={action.permitted ? 'success' : 'warning'}>
                        {isArabic ? action.labelAr : action.labelEn}
                      </Badge>
                      {!action.permitted ? (
                        <span style={{ color: 'var(--color-text-secondary)' }}>
                          {isArabic ? action.blockedReasonAr : action.blockedReasonEn}
                        </span>
                      ) : null}
                    </div>
                  ))}
                </div>
              ) : (
                <span style={{ color: 'var(--color-text-secondary)' }}>{t('workspace.noNextAction')}</span>
              )}
            </div>
          )}
        </Card>
      ) : null}

      {isInternalReview ? (
        <Card title={t('rfq.returnForEditsTitle')}>
          <div className="flex gap-2">
            <Input aria-label={t('rfq.fields.comments')} placeholder={t('rfq.fields.comments')} value={returnComments} onChange={(e) => setReturnComments(e.target.value)} />
            <Button variant="ghost" isLoading={returnMutation.isPending} onClick={() => returnMutation.mutate()}>{t('rfq.returnForEdits')}</Button>
          </div>
        </Card>
      ) : null}

      <Card title={t('rfq.fields.items')}>
        {rfq.items.length > 0 ? (
          <Table caption={t('rfq.fields.items')}>
            <TableHead>
              <TableHeaderCell>#</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.category')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.quantity')}</TableHeaderCell>
              {isDraft ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {rfq.items.map((item) => (
                editingItemId === item.id ? (
                  <TableRow key={item.id}>
                    <TableCell>{item.lineNo}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Input aria-label={`${t('rfq.fields.titleEn')} — ${item.lineNo}`} value={editItem.titleEn}
                          onChange={(e) => setEditItem((p) => ({ ...p, titleEn: e.target.value }))} />
                        <Input aria-label={`${t('rfq.fields.titleAr')} — ${item.lineNo}`} value={editItem.titleAr}
                          onChange={(e) => setEditItem((p) => ({ ...p, titleAr: e.target.value }))} />
                      </div>
                    </TableCell>
                    <TableCell>
                      <Select value={editItem.categoryCode} onValueChange={(v) => setEditItem((p) => ({ ...p, categoryCode: v }))}
                        placeholder={t('rfq.fields.category')}
                        options={categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))} />
                    </TableCell>
                    <TableCell>
                      <Input type="number" aria-label={`${t('rfq.fields.quantity')} — ${item.lineNo}`} value={editItem.quantity}
                        onChange={(e) => setEditItem((p) => ({ ...p, quantity: e.target.value }))} />
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-2">
                        <Button size="sm" isLoading={updateItemMutation.isPending} onClick={() => updateItemMutation.mutate()}>{t('rfq.save')}</Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditingItemId(null)}>{t('rfq.cancelEdit')}</Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ) : (
                  <TableRow key={item.id}>
                    <TableCell>{item.lineNo}</TableCell>
                    <TableCell>{isArabic ? item.titleAr : item.titleEn}</TableCell>
                    <TableCell>{item.categoryCode}</TableCell>
                    <TableCell>{formatNumber(item.quantity, locale, 0)}</TableCell>
                    {isDraft ? (
                      <TableCell>
                        <div className="flex gap-2">
                          <Button size="sm" variant="ghost" onClick={() => {
                            setEditingItemId(item.id)
                            setEditItem({
                              titleAr: item.titleAr, titleEn: item.titleEn, categoryCode: item.categoryCode,
                              unitOfMeasureCode: item.unitOfMeasureCode, quantity: String(item.quantity),
                            })
                          }}>{t('rfq.edit')}</Button>
                          <Button size="sm" variant="ghost" onClick={() => removeItemMutation.mutate(item.id)}>{t('rfq.remove')}</Button>
                        </div>
                      </TableCell>
                    ) : null}
                  </TableRow>
                )
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noItems')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex flex-wrap items-end gap-2">
            <Input aria-label={t('rfq.fields.titleEn')} placeholder={t('rfq.fields.titleEn')} value={itemTitleEn} onChange={(e) => setItemTitleEn(e.target.value)} />
            <Input aria-label={t('rfq.fields.titleAr')} placeholder={t('rfq.fields.titleAr')} value={itemTitleAr} onChange={(e) => setItemTitleAr(e.target.value)} />
            <Select value={itemCategory} onValueChange={setItemCategory} placeholder={t('rfq.fields.category')}
              options={categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))} />
            <Select value={itemUom} onValueChange={setItemUom} placeholder={t('rfq.fields.unit')}
              options={units.map((u) => ({ value: u.code, label: isArabic ? u.nameAr : u.nameEn }))} />
            <Input type="number" aria-label={t('rfq.fields.quantity')} placeholder={t('rfq.fields.quantity')} value={itemQty} onChange={(e) => setItemQty(e.target.value)} className="w-24" />
            <Button size="sm" isLoading={addItemMutation.isPending} onClick={() => addItemMutation.mutate()}>{t('rfq.addItem')}</Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.fields.requirements')}>
        {rfq.requirements.length > 0 ? (
          <Table caption={t('rfq.fields.requirements')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.text')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.mandatory')}</TableHeaderCell>
              {isDraft ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
            </TableHead>
            <TableBody>
              {rfq.requirements.map((req, index) => (
                editingRequirementId === req.id ? (
                  <TableRow key={req.id}>
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Input aria-label={`${t('rfq.fields.textEn')} — ${index + 1}`} value={editRequirement.textEn}
                          onChange={(e) => setEditRequirement((p) => ({ ...p, textEn: e.target.value }))} />
                        <Input aria-label={`${t('rfq.fields.textAr')} — ${index + 1}`} value={editRequirement.textAr}
                          onChange={(e) => setEditRequirement((p) => ({ ...p, textAr: e.target.value }))} />
                      </div>
                    </TableCell>
                    <TableCell>{req.isMandatory ? t('rfq.yes') : t('rfq.no')}</TableCell>
                    <TableCell>
                      <div className="flex gap-2">
                        <Button size="sm" isLoading={updateRequirementMutation.isPending} onClick={() => updateRequirementMutation.mutate()}>{t('rfq.save')}</Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditingRequirementId(null)}>{t('rfq.cancelEdit')}</Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ) : (
                  <TableRow key={req.id}>
                    <TableCell>{isArabic ? req.textAr : req.textEn}</TableCell>
                    <TableCell>{req.isMandatory ? t('rfq.yes') : t('rfq.no')}</TableCell>
                    {isDraft ? (
                      <TableCell>
                        <div className="flex gap-2">
                          <Button size="sm" variant="ghost" onClick={() => {
                            setEditingRequirementId(req.id)
                            setEditRequirement({ textAr: req.textAr, textEn: req.textEn, isMandatory: req.isMandatory })
                          }}>{t('rfq.edit')}</Button>
                          <Button size="sm" variant="ghost" onClick={() => removeRequirementMutation.mutate(req.id)}>{t('rfq.remove')}</Button>
                        </div>
                      </TableCell>
                    ) : null}
                  </TableRow>
                )
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noRequirements')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex flex-wrap items-end gap-2">
            <Input aria-label={t('rfq.fields.textEn')} placeholder={t('rfq.fields.textEn')} value={reqTextEn} onChange={(e) => setReqTextEn(e.target.value)} />
            <Input aria-label={t('rfq.fields.textAr')} placeholder={t('rfq.fields.textAr')} value={reqTextAr} onChange={(e) => setReqTextAr(e.target.value)} />
            <label className="flex items-center gap-1 text-[length:var(--text-body-sm)]">
              <input type="checkbox" checked={reqMandatory} onChange={(e) => setReqMandatory(e.target.checked)} />
              {t('rfq.fields.mandatory')}
            </label>
            <Button size="sm" isLoading={addRequirementMutation.isPending} onClick={() => addRequirementMutation.mutate()}>{t('rfq.addRequirement')}</Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.attachments.title')}>
        {/*
          * F-8, and the warning is the whole fix.
          *
          * Attachments are Draft-only by design: bidders price against what they downloaded, and a
          * file swapped underneath them is what that lock prevents. The ruling (D-56) is that an
          * addendum announces a change and does not carry a document, so there is NO route to correct
          * a published tender's attachments - the tender has to be cancelled and authored again.
          *
          * That is defensible and it is invisible. An officer attaching the wrong file has no way to
          * know, at the moment they attach it, that they are making a permanent decision. Saying so
          * here is what turns a trap into a rule.
          */}
        {isDraft ? (
          <p className="mb-3 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.attachments.permanentWarning')}
          </p>
        ) : null}
        {rfq.attachments.length > 0 ? (
          <ul className="flex flex-col gap-2">
            {rfq.attachments.map((attachment) => (
              <li key={attachment.id} className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex min-w-0 flex-col">
                  <span className="truncate">{attachment.originalFileName}</span>
                  {attachment.caption ? (
                    <span className="text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                      {attachment.caption}
                    </span>
                  ) : null}
                </div>
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    variant="ghost"
                    isLoading={downloadAttachmentMutation.isPending && downloadAttachmentMutation.variables === attachment.id}
                    onClick={() => downloadAttachmentMutation.mutate(attachment.id)}
                  >
                    {t('rfq.attachments.download')}
                  </Button>
                  {/* Removal is Draft-only, like every other structural edit on this page: an
                      attachment a supplier has already been invited to read must not vanish. */}
                  {isDraft ? (
                    <Button size="sm" variant="ghost" onClick={() => removeAttachmentMutation.mutate(attachment.id)}>
                      {t('rfq.remove')}
                    </Button>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.attachments.none')}</p>
        )}
        {isDraft ? (
          <div className="mt-4">
            <label className="flex flex-col gap-1 text-[length:var(--text-body-sm)]">
              {t('rfq.attachments.add')}
              <input
                type="file"
                onChange={(event) => {
                  const file = event.target.files?.[0]
                  if (file) addAttachmentMutation.mutate(file)
                  // Cleared so re-picking the same file fires change again - otherwise a failed
                  // upload cannot be retried without choosing a different file.
                  event.target.value = ''
                }}
              />
            </label>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.fields.evaluationTemplate')}>
        {rfq.evaluationTemplateId ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>
            {t('rfq.boundTemplate', { id: rfq.evaluationTemplateId, version: rfq.evaluationTemplateVersion })}
          </p>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.noTemplateBound')}</p>
        )}
        {isDraft ? (
          <div className="mt-4 flex items-end gap-2">
            <Select value={selectedTemplateId} onValueChange={setSelectedTemplateId} placeholder={t('rfq.fields.evaluationTemplate')}
              options={activeTemplates.map((tpl) => ({ value: tpl.id, label: `${tpl.nameEn} (v${tpl.version})` }))} />
            <Button size="sm" isLoading={bindTemplateMutation.isPending} disabled={!selectedTemplateId} onClick={() => bindTemplateMutation.mutate()}>
              {t('rfq.bindTemplate')}
            </Button>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.invitations.title')}>
        {rfq.invitations.length > 0 ? (
          <Table caption={t('rfq.invitations.title')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.invitations.fields.supplier')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.invitedAt')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.viewedAt')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.invitations.fields.declineReason')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.invitations.map((inv) => (
                <TableRow key={inv.id}>
                  <TableCell>{isArabic ? inv.supplierDisplayNameAr : inv.supplierDisplayNameEn}</TableCell>
                  <TableCell>
                    <StatusChip machine="invitation" value={inv.status} />
                  </TableCell>
                  <TableCell>{formatDate(inv.invitedAt, i18n.language)}</TableCell>
                  <TableCell>{inv.viewedAt ? formatDate(inv.viewedAt, i18n.language) : '—'}</TableCell>
                  <TableCell>{inv.declineReason ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.invitations.none')}</p>
        )}
        {canInvite && uninvitedCandidates.length > 0 ? (
          <div className="mt-4">
            <p className="mb-2 text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
              {t('rfq.invitations.candidatesTitle')}
            </p>
            <ul className="flex flex-col gap-2">
              {uninvitedCandidates.map((c) => (
                <li key={c.supplierId} className="flex items-center justify-between gap-2">
                  <span>{isArabic ? c.displayNameAr : c.displayNameEn} ({t('rfq.invitations.matchCount', { count: c.matchCount })})</span>
                  <Button size="sm" isLoading={inviteMutation.isPending} onClick={() => inviteMutation.mutate(c.supplierId)}>
                    {t('rfq.invitations.invite')}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </Card>

      <Card title={t('rfq.clarifications.title')}>
        {rfq.clarifications.length > 0 ? (
          <ul className="flex flex-col gap-4">
            {rfq.clarifications.map((c) => {
              const draft = draftFor(c.id)
              return (
                <li key={c.id} className="border-b pb-4 last:border-b-0" style={{ borderColor: 'var(--color-border)' }}>
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-[var(--fw-medium)]">{isArabic ? c.askedBySupplierNameAr : c.askedBySupplierNameEn}: {c.question}</p>
                    <Badge tone={c.visibility === 'PublishedToAll' ? 'success' : 'info'}>
                      {c.visibility === 'PublishedToAll' ? t('rfq.clarifications.published') : t('rfq.clarifications.private')}
                    </Badge>
                  </div>
                  {c.answer ? (
                    <p className="mt-1" style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.answerLabel')}: {c.answer}</p>
                  ) : (
                    <div className="mt-2 flex flex-wrap items-end gap-2">
                      <Input aria-label={t('rfq.clarifications.answerLabel')} placeholder={t('rfq.clarifications.answerLabel')}
                        value={draft.text} onChange={(e) => setAnswerDrafts((prev) => ({ ...prev, [c.id]: { ...draft, text: e.target.value } }))} />
                      <p className="w-full text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
                        {t('rfq.clarifications.broadcastNotice')}
                      </p>
                      {/* A-4: no publish checkbox. Answering broadcasts to every invitee with the
                          asker anonymised, so the officer is told that rather than asked it - an
                          option whose only fair setting is "yes" is not a choice. */}
                      <Button size="sm" isLoading={answerMutation.isPending} disabled={!draft.text}
                        onClick={() => answerMutation.mutate({ clarificationId: c.id, answer: draft.text })}>
                        {t('rfq.clarifications.answer')}
                      </Button>
                    </div>
                  )}
                  {c.answer && c.visibility === 'PrivateToAsker' ? (
                    <Button size="sm" variant="secondary" className="mt-2" isLoading={publishClarificationMutation.isPending}
                      onClick={() => publishClarificationMutation.mutate(c.id)}>
                      {t('rfq.clarifications.publish')}
                    </Button>
                  ) : null}
                </li>
              )
            })}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.clarifications.none')}</p>
        )}
      </Card>

      <Card title={t('rfq.addenda.title')}>
        {rfq.addenda.length > 0 ? (
          <ul className="flex flex-col gap-2">
            {rfq.addenda.map((a) => (
              <li key={a.id}>
                <p className="font-[var(--fw-medium)]">{isArabic ? a.titleAr : a.titleEn}</p>
                <p style={{ color: 'var(--color-text-secondary)' }}>{isArabic ? a.descriptionAr : a.descriptionEn}</p>
              </li>
            ))}
          </ul>
        ) : (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('rfq.addenda.none')}</p>
        )}
        {canIssueAddendum ? (
          <form className="mt-4 flex flex-col gap-2" onSubmit={(e) => { e.preventDefault(); addendumMutation.mutate() }}>
            <div className="flex flex-wrap gap-2">
              <Input aria-label={t('rfq.fields.titleEn')} placeholder={t('rfq.fields.titleEn')} value={addendumTitleEn} onChange={(e) => setAddendumTitleEn(e.target.value)} />
              <Input aria-label={t('rfq.fields.titleAr')} placeholder={t('rfq.fields.titleAr')} value={addendumTitleAr} onChange={(e) => setAddendumTitleAr(e.target.value)} />
            </div>
            <div className="flex flex-wrap gap-2">
              <Input aria-label={t('rfq.addenda.descriptionEn')} placeholder={t('rfq.addenda.descriptionEn')} value={addendumDescEn} onChange={(e) => setAddendumDescEn(e.target.value)} />
              <Input aria-label={t('rfq.addenda.descriptionAr')} placeholder={t('rfq.addenda.descriptionAr')} value={addendumDescAr} onChange={(e) => setAddendumDescAr(e.target.value)} />
            </div>
            <Button type="submit" size="sm" className="self-start" isLoading={addendumMutation.isPending}
              disabled={!addendumTitleAr || !addendumTitleEn || !addendumDescAr || !addendumDescEn}>
              {t('rfq.addenda.issue')}
            </Button>
          </form>
        ) : null}
      </Card>

      {rfq.approvals.length > 0 ? (
        <Card title={t('rfq.fields.approvals')}>
          <Table caption={t('rfq.fields.approvals')}>
            <TableHead>
              <TableHeaderCell>{t('rfq.fields.step')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.decision')}</TableHeaderCell>
              <TableHeaderCell>{t('rfq.fields.comments')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {rfq.approvals.map((a) => (
                <TableRow key={a.stepNo}>
                  <TableCell>{a.stepNo}</TableCell>
                  <TableCell>{a.decision ? <StatusChip machine="award" value={a.decision} /> : <Badge tone="info">{t('rfq.pending')}</Badge>}</TableCell>
                  <TableCell>{a.comment ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      ) : null}

      {evaluationEligible ? (
        <Card title={t('evaluation.title')}>
          <div className="mb-4 flex gap-2">
            {/* T-082: the bids themselves, readable from SubmissionClosed onward - before the
                comparison matrix exists and without needing an opened evaluation. */}
            <a href={`/back-office/rfqs/${referenceCode}/proposals`}>
              <Button size="sm" variant="secondary">{t('receivedProposals.title')}</Button>
            </a>
            <a href={`/back-office/rfqs/${referenceCode}/comparison`}>
              <Button size="sm" variant="secondary">{t('comparison.title')}</Button>
            </a>
            {evaluation?.state === 'Finalized' || ['AwardApproval', 'Awarded', 'Completed'].includes(rfq.state) ? (
              <a href={`/back-office/rfqs/${referenceCode}/award`}>
                <Button size="sm" variant="secondary">{t('award.title')}</Button>
              </a>
            ) : null}
          </div>
          {!evaluation ? (
            rfq.state === 'SubmissionClosed' ? (
              <Button isLoading={openEvaluationMutation.isPending} onClick={() => openEvaluationMutation.mutate()}>
                {t('evaluation.open')}
              </Button>
            ) : (
              <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.notOpened')}</p>
            )
          ) : (
            <div className="flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <StatusChip machine="evaluation" value={evaluation.state} />
                {evaluation.state !== 'NotStarted' ? (
                  <a href={`/back-office/rfqs/${referenceCode}/my-evaluation`}>
                    <Button size="sm" variant="secondary">{t('evaluation.my.title')}</Button>
                  </a>
                ) : null}
              </div>

              <div>
                <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.criteria')}</p>
                <Table caption={t('evaluation.criteria')}>
                  <TableHead>
                    <TableHeaderCell>{t('rfq.fields.title')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.dimension')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.weight')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.threshold')}</TableHeaderCell>
                    <TableHeaderCell>{t('evaluation.envelope')}</TableHeaderCell>
                  </TableHead>
                  <TableBody>
                    {evaluation.criteria.map((c) => (
                      <TableRow key={c.id}>
                        <TableCell>{isArabic ? c.nameAr : c.nameEn}</TableCell>
                        <TableCell>{c.dimension}</TableCell>
                        <TableCell>{formatNumber(c.weight, locale, 0)}</TableCell>
                        <TableCell>{c.threshold ?? '—'}</TableCell>
                        <TableCell>
                          <Badge tone={c.isFinancial ? 'warning' : 'info'}>
                            {c.isFinancial ? t('evaluation.financialEnvelope') : t('evaluation.technicalEnvelope')}
                          </Badge>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>

              <div>
                <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.assignments')}</p>
                {evaluation.assignments.length > 0 ? (
                  <Table caption={t('evaluation.assignments')}>
                    <TableHead>
                      <TableHeaderCell>{t('evaluation.evaluator')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.submittedAt')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.recusedAt')}</TableHeaderCell>
                      {evaluation.state !== 'Finalized' ? <TableHeaderCell>{t('rfq.actions')}</TableHeaderCell> : null}
                    </TableHead>
                    <TableBody>
                      {evaluation.assignments.map((a) => (
                        <TableRow key={a.evaluatorUserId}>
                          {/* The name, with the id only as a fallback - an assignment whose user row has gone
                              should still be visible rather than blank. */}
                          <TableCell>{a.evaluatorName ?? a.evaluatorUserId}</TableCell>
                          <TableCell>{a.submittedAt ? formatDateTime(a.submittedAt, i18n.language) : '—'}</TableCell>
                          <TableCell>{a.recusedAt ? t('evaluation.recusedWithReason', { reason: a.recusalReason }) : '—'}</TableCell>
                          {evaluation.state !== 'Finalized' ? (
                            <TableCell>
                              {!a.recusedAt && !a.submittedAt ? (
                                <Button size="sm" variant="ghost" onClick={() => setRecuseTargetId(a.evaluatorUserId)}>
                                  {t('evaluation.recuse')}
                                </Button>
                              ) : null}
                            </TableCell>
                          ) : null}
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                ) : (
                  <p style={{ color: 'var(--color-text-secondary)' }}>{t('evaluation.noAssignments')}</p>
                )}

                {evaluation.state !== 'Finalized' && evaluation.state !== 'Consolidated' ? (
                  <div className="mt-4 flex flex-wrap items-end gap-2">
                    {/*
                      A picker, not a free-text GUID box. This was an Input asking a manager to type
                      01a07461-fa48-7721-abe2-018baaa84d11, and the only staff list in the product needs
                      admin.users.manage - which a procurement_manager does not hold. The assign step was
                      unusable without database access; found by walking the tender in the browser.
                    */}
                    <div className="min-w-[16rem]">
                      <Select
                        value={evaluatorUserId}
                        onValueChange={setEvaluatorUserId}
                        placeholder={t('evaluation.chooseEvaluator')}
                        options={(evaluatorCandidatesQuery.data ?? [])
                          .filter((c) => !evaluation.assignments.some((a) => a.evaluatorUserId === c.userId))
                          .map((c) => ({ value: c.userId, label: `${c.fullName} · ${c.email}` }))}
                      />
                    </div>
                    <Button size="sm" isLoading={assignEvaluatorsMutation.isPending} disabled={!evaluatorUserId}
                      onClick={() => assignEvaluatorsMutation.mutate()}>
                      {t('evaluation.assign')}
                    </Button>
                  </div>
                ) : null}

                {recuseTargetId ? (
                  <div className="mt-2 flex flex-wrap items-end gap-2">
                    <Input aria-label={t('evaluation.recuseReason')} placeholder={t('evaluation.recuseReason')}
                      value={recuseReason} onChange={(e) => setRecuseReason(e.target.value)} />
                    <Button size="sm" variant="ghost" isLoading={recuseEvaluatorMutation.isPending} disabled={!recuseReason}
                      onClick={() => recuseEvaluatorMutation.mutate()}>
                      {t('evaluation.confirmRecuse')}
                    </Button>
                  </div>
                ) : null}
              </div>

              {evaluation.results.length > 0 ? (
                <div>
                  <p className="mb-2 font-[var(--fw-medium)]">{t('evaluation.results')}</p>
                  <Table caption={t('evaluation.results')}>
                    <TableHead>
                      <TableHeaderCell>{t('evaluation.rank')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.proposal')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.qualified')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.technicalScore')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.financialScore')}</TableHeaderCell>
                      <TableHeaderCell>{t('evaluation.total')}</TableHeaderCell>
                    </TableHead>
                    <TableBody>
                      {[...evaluation.results].sort((a, b) => (a.rank ?? 999) - (b.rank ?? 999)).map((r) => (
                        <TableRow key={r.proposalId}>
                          <TableCell>{r.rank ?? '—'}</TableCell>
                          {/* §3's reference code, with the internal id only as a fallback. This cell was the
                              GUID - on the screen where a tender is decided. */}
                          <TableCell>{r.proposalReferenceCode ?? r.proposalId}</TableCell>
                          <TableCell>
                            <Badge tone={r.technicallyQualified ? 'success' : 'danger'}>
                              {r.technicallyQualified ? t('evaluation.qualifiedYes') : t('evaluation.qualifiedNo')}
                            </Badge>
                          </TableCell>
                          <TableCell>{formatNumber(r.technicalWeightedScore, locale)}</TableCell>
                          <TableCell>{r.financialWeightedScore !== null ? formatNumber(r.financialWeightedScore, locale) : '—'}</TableCell>
                          <TableCell>{formatNumber(r.weightedTotal, locale)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              ) : null}

              <div className="flex flex-wrap gap-2">
                {evaluation.state === 'EvaluatorSubmitted' ? (
                  <Button isLoading={consolidateMutation.isPending} onClick={() => consolidateMutation.mutate()}>
                    {t('evaluation.consolidate')}
                  </Button>
                ) : null}
                {evaluation.state === 'Consolidated' && canFinalizeEvaluation ? (
                  <Button isLoading={finalizeMutation.isPending} onClick={() => finalizeMutation.mutate()}>
                    {t('evaluation.finalize')}
                  </Button>
                ) : null}
              </div>

              {evaluation.state === 'Consolidated' && canReopenEvaluation ? (
                <div className="flex flex-wrap items-end gap-2">
                  <Input aria-label={t('evaluation.reopenReason')} placeholder={t('evaluation.reopenReason')}
                    value={reopenReason} onChange={(e) => setReopenReason(e.target.value)} />
                  <Button size="sm" variant="ghost" isLoading={reopenEvaluationMutation.isPending} disabled={!reopenReason}
                    onClick={() => reopenEvaluationMutation.mutate()}>
                    {t('evaluation.reopen')}
                  </Button>
                </div>
              ) : null}
            </div>
          )}
        </Card>
      ) : null}

      {canCancel ? (
        <CancelSection onCancel={(reason) => cancelMutation.mutate(reason)} isPending={cancelMutation.isPending} />
      ) : null}
    </div>
  )
}
