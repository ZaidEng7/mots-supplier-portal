import { useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, Dialog, Field, Input, Select, Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow, useToast } from '../components/ui'
import { invalidateQuietly } from '../lib/queryClient'
import { listOfferings, createOffering, updateOffering, deactivateOffering, type Offering, type OfferingPayload } from '../api/offerings'
import { fetchCategories, fetchUnitsOfMeasure, fetchCurrencies } from '../api/reference'
import { SupplierApiError } from '../api/supplier'

const schema = z.object({
  nameAr: z.string().min(1),
  nameEn: z.string().min(1),
  description: z.string().optional(),
  categoryCode: z.string().min(1),
  unitOfMeasureCode: z.string().min(1),
  priceAmount: z.string().optional(),
  currencyCode: z.string().optional(),
  // FEAT-06.2 [ASSUMPTION]: flexible key/value rows, not a per-category schema - see
  // Offering.AttributesJson's doc comment for why. Rows with an empty key are dropped in
  // toPayload rather than rejected here, so a half-filled trailing row doesn't block save.
  attributes: z.array(z.object({ key: z.string(), value: z.string() })),
})
export type FormValues = z.infer<typeof schema>

export function toPayload(values: FormValues): OfferingPayload {
  const priceAmount = values.priceAmount ? Number(values.priceAmount) : null
  const entries = values.attributes.filter((a) => a.key.trim().length > 0)
  return {
    nameAr: values.nameAr,
    nameEn: values.nameEn,
    description: values.description || null,
    categoryCode: values.categoryCode,
    unitOfMeasureCode: values.unitOfMeasureCode,
    priceAmount,
    currencyCode: priceAmount !== null ? (values.currencyCode || null) : null,
    attributes: entries.length > 0 ? Object.fromEntries(entries.map((a) => [a.key.trim(), a.value])) : null,
  }
}

export function attributesToRows(attributes: Record<string, string> | null | undefined): { key: string; value: string }[] {
  return attributes ? Object.entries(attributes).map(([key, value]) => ({ key, value })) : []
}

function OfferingDialog({
  open,
  onOpenChange,
  offering,
  categories,
  units,
  currencies,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  offering: Offering | null
  categories: { code: string; nameAr: string; nameEn: string }[]
  units: { code: string; nameAr: string; nameEn: string }[]
  currencies: { code: string; nameAr: string; nameEn: string }[]
}) {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: offering
      ? {
          nameAr: offering.nameAr,
          nameEn: offering.nameEn,
          description: offering.description ?? '',
          categoryCode: offering.categoryCode,
          unitOfMeasureCode: offering.unitOfMeasureCode,
          priceAmount: offering.priceAmount?.toString() ?? '',
          currencyCode: offering.currencyCode ?? '',
          attributes: attributesToRows(offering.attributes),
        }
      : { nameAr: '', nameEn: '', description: '', categoryCode: '', unitOfMeasureCode: '', priceAmount: '', currencyCode: '', attributes: [] },
  })
  const categoryCode = watch('categoryCode')
  const unitOfMeasureCode = watch('unitOfMeasureCode')
  const currencyCode = watch('currencyCode')
  const { fields: attributeFields, append: appendAttribute, remove: removeAttribute } = useFieldArray({ control, name: 'attributes' })

  const errorMessage = (err: unknown, fallback: string) => {
    if (err instanceof SupplierApiError) {
      if (err.message === 'invalid_category') return t('offeringCatalog.errors.invalidCategory')
      if (err.message === 'invalid_unit_of_measure') return t('offeringCatalog.errors.invalidUnit')
      if (err.message === 'invalid_currency') return t('offeringCatalog.errors.invalidCurrency')
    }
    return fallback
  }

  const saveMutation = useMutation({
    mutationFn: (values: FormValues) =>
      offering ? updateOffering(offering.id, toPayload(values)) : createOffering(toPayload(values)),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['offerings'] })
      notify({ kind: 'success', title: t(offering ? 'offeringCatalog.updated' : 'offeringCatalog.created') })
      onOpenChange(false)
      reset()
    },
    onError: (err) => notify({ kind: 'danger', title: errorMessage(err, t('offeringCatalog.errors.saveFailed')) }),
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => { onOpenChange(o); if (!o) reset() }}
      title={t(offering ? 'offeringCatalog.editTitle' : 'offeringCatalog.createTitle')}
    >
      <form className="flex flex-col gap-4" onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
        <Field label={t('offeringCatalog.fields.nameAr')} error={errors.nameAr ? t('offeringCatalog.errors.required') : undefined} required>
          {(p) => <Input {...p} {...register('nameAr')} />}
        </Field>
        <Field label={t('offeringCatalog.fields.nameEn')} error={errors.nameEn ? t('offeringCatalog.errors.required') : undefined} required>
          {(p) => <Input {...p} {...register('nameEn')} />}
        </Field>
        <Field label={t('offeringCatalog.fields.description')}>
          {(p) => <Input {...p} {...register('description')} />}
        </Field>
        <Field label={t('offeringCatalog.fields.category')} error={errors.categoryCode ? t('offeringCatalog.errors.required') : undefined} required>
          {(p) => (
            <Select
              id={p.id}
              value={categoryCode}
              onValueChange={(v) => setValue('categoryCode', v)}
              options={categories.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))}
            />
          )}
        </Field>
        <Field label={t('offeringCatalog.fields.unit')} error={errors.unitOfMeasureCode ? t('offeringCatalog.errors.required') : undefined} required>
          {(p) => (
            <Select
              id={p.id}
              value={unitOfMeasureCode}
              onValueChange={(v) => setValue('unitOfMeasureCode', v)}
              options={units.map((u) => ({ value: u.code, label: isArabic ? u.nameAr : u.nameEn }))}
            />
          )}
        </Field>
        <Field label={t('offeringCatalog.fields.price')}>
          {(p) => <Input type="number" step="0.01" min="0" {...p} {...register('priceAmount')} />}
        </Field>
        <Field label={t('offeringCatalog.fields.currency')}>
          {(p) => (
            <Select
              id={p.id}
              value={currencyCode}
              onValueChange={(v) => setValue('currencyCode', v)}
              options={currencies.map((c) => ({ value: c.code, label: isArabic ? c.nameAr : c.nameEn }))}
            />
          )}
        </Field>
        <div className="flex flex-col gap-2">
          <span className="text-[length:var(--text-caption)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('offeringCatalog.fields.attributes')}
          </span>
          {attributeFields.map((field, index) => (
            <div key={field.id} className="flex items-center gap-2">
              <Input
                aria-label={t('offeringCatalog.fields.attributeKey')}
                placeholder={t('offeringCatalog.fields.attributeKey')}
                {...register(`attributes.${index}.key` as const)}
              />
              <Input
                aria-label={t('offeringCatalog.fields.attributeValue')}
                placeholder={t('offeringCatalog.fields.attributeValue')}
                {...register(`attributes.${index}.value` as const)}
              />
              <Button type="button" variant="ghost" size="sm" onClick={() => removeAttribute(index)}>
                {t('offeringCatalog.fields.removeAttribute')}
              </Button>
            </div>
          ))}
          <Button type="button" variant="secondary" size="sm" onClick={() => appendAttribute({ key: '', value: '' })}>
            {t('offeringCatalog.fields.addAttribute')}
          </Button>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            {t('offeringCatalog.cancel')}
          </Button>
          <Button type="submit" isLoading={saveMutation.isPending}>
            {t('offeringCatalog.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}

export function OfferingCatalogPage() {
  const { t, i18n } = useTranslation()
  const isArabic = i18n.language.startsWith('ar')
  const { notify } = useToast()
  const queryClient = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<Offering | null>(null)

  const offeringsQuery = useQuery({ queryKey: ['offerings'], queryFn: listOfferings })
  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: fetchCategories })
  const unitsQuery = useQuery({ queryKey: ['units-of-measure'], queryFn: fetchUnitsOfMeasure })
  const currenciesQuery = useQuery({ queryKey: ['currencies'], queryFn: fetchCurrencies })

  const deactivateMutation = useMutation({
    mutationFn: (offeringId: string) => deactivateOffering(offeringId),
    onSuccess: () => {
      invalidateQuietly(queryClient, { queryKey: ['offerings'] })
      notify({ kind: 'success', title: t('offeringCatalog.deactivated') })
    },
    onError: () => notify({ kind: 'danger', title: t('offeringCatalog.errors.deactivateFailed') }),
  })

  const offerings = offeringsQuery.data ?? []
  const categories = categoriesQuery.data ?? []
  const units = unitsQuery.data ?? []
  const currencies = currenciesQuery.data ?? []

  const categoryLabel = (code: string) => {
    const c = categories.find((c) => c.code === code)
    return c ? (isArabic ? c.nameAr : c.nameEn) : code
  }
  const unitLabel = (code: string) => {
    const u = units.find((u) => u.code === code)
    return u ? (isArabic ? u.nameAr : u.nameEn) : code
  }

  if (offeringsQuery.isLoading || categoriesQuery.isLoading || unitsQuery.isLoading) {
    return <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.loading')}</p>
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[length:var(--text-h2)] font-[var(--fw-semibold)]" style={{ color: 'var(--color-text-primary)' }}>
            {t('offeringCatalog.title')}
          </h1>
          <p className="mt-1 text-[length:var(--text-body-sm)]" style={{ color: 'var(--color-text-secondary)' }}>
            {t('offeringCatalog.subtitle')}
          </p>
        </div>
        <Button onClick={() => { setEditing(null); setDialogOpen(true) }}>{t('offeringCatalog.add')}</Button>
      </div>

      <Card title={t('offeringCatalog.listTitle')}>
        {offerings.length === 0 ? (
          <p style={{ color: 'var(--color-text-secondary)' }}>{t('offeringCatalog.empty')}</p>
        ) : (
          <Table caption={t('offeringCatalog.listTitle')}>
            <TableHead>
              <TableHeaderCell>{t('offeringCatalog.fields.name')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringCatalog.fields.category')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringCatalog.fields.unit')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringCatalog.fields.price')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringCatalog.status')}</TableHeaderCell>
              <TableHeaderCell>{t('offeringCatalog.actions')}</TableHeaderCell>
            </TableHead>
            <TableBody>
              {offerings.map((o) => (
                <TableRow key={o.id}>
                  <TableCell>{isArabic ? o.nameAr : o.nameEn}</TableCell>
                  <TableCell>{categoryLabel(o.categoryCode)}</TableCell>
                  <TableCell>{unitLabel(o.unitOfMeasureCode)}</TableCell>
                  <TableCell>
                    {o.priceAmount !== null
                      ? new Intl.NumberFormat(isArabic ? 'ar-SY-u-nu-latn' : 'en-US', { style: 'currency', currency: o.currencyCode ?? 'USD' }).format(o.priceAmount)
                      : '—'}
                  </TableCell>
                  <TableCell>
                    <Badge tone={o.isActive ? 'success' : 'neutral'}>{o.isActive ? t('offeringCatalog.active') : t('offeringCatalog.inactive')}</Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <Button variant="ghost" size="sm" onClick={() => { setEditing(o); setDialogOpen(true) }}>
                        {t('offeringCatalog.edit')}
                      </Button>
                      {o.isActive ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          isLoading={deactivateMutation.isPending}
                          onClick={() => deactivateMutation.mutate(o.id)}
                        >
                          {t('offeringCatalog.deactivate')}
                        </Button>
                      ) : null}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <OfferingDialog open={dialogOpen} onOpenChange={setDialogOpen} offering={editing} categories={categories} units={units} currencies={currencies} />
    </div>
  )
}
