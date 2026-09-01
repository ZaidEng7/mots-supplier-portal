import { useTranslation } from 'react-i18next'
import { Select } from './Select'
import { Input } from './Input'
import { COUNTRY_DIAL_CODES, OTHER_COUNTRY_CODE, parsePhone, composePhone } from '../../lib/phoneNumber'

interface PhoneInputProps {
  id?: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  'aria-describedby'?: string
  'aria-invalid'?: boolean
}

/**
 * Task #41: country-code dropdown + local-number field, composing into the same `+<code><digits>`
 * format every phone field already stores (see lib/phoneNumber.ts). `value`/`onChange` carry that
 * composed string - the split into code/local number is internal display state only, re-derived
 * from `value` on every render via parsePhone, so this stays a normal controlled field from the
 * parent form's point of view (wire it with the same watch/setValue pattern used for every other
 * Select-backed field in this codebase, e.g. AddressDialog's `kind`).
 */
export function PhoneInput({ id, value, onChange, disabled, ...aria }: PhoneInputProps) {
  const { t } = useTranslation()
  const { countryCode, localNumber } = parsePhone(value)

  const options = [
    ...COUNTRY_DIAL_CODES.map((c) => ({ value: c.code, label: t(`phone.countries.${c.country}`) })),
    { value: OTHER_COUNTRY_CODE, label: t('phone.other') },
  ]

  return (
    <div className="flex gap-2">
      <div className="w-36 shrink-0">
        <Select
          value={countryCode}
          onValueChange={(code) => onChange(composePhone(code, localNumber))}
          options={options}
          placeholder={t('phone.countryCode')}
          aria-invalid={aria['aria-invalid']}
        />
      </div>
      <Input
        id={id}
        type="tel"
        disabled={disabled}
        value={localNumber}
        onChange={(e) => onChange(composePhone(countryCode, e.target.value))}
        placeholder={t('phone.localNumberPlaceholder')}
        {...aria}
      />
    </div>
  )
}
