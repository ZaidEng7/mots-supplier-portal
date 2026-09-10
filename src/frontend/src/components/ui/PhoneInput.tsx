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
    // The number takes the room the dialling code leaves, and drops to its own line rather than being
    // crushed when there is not enough. On the registration screen this pair sits in a half-width
    // column, and the number field was collapsing to about sixty pixels with its placeholder clipped
    // to "Phon" - the first form a supplier ever fills in, with the field they cannot read what they
    // typed into. An input's intrinsic width is what it shrinks from; nothing was telling it to grow.
    <div className="flex flex-wrap gap-2">
      <div className="w-36 shrink-0">
        <Select
          value={countryCode}
          onValueChange={(code) => onChange(composePhone(code, localNumber))}
          options={options}
          placeholder={t('phone.countryCode')}
          // The dialling code is half of one value: disabling the number and leaving this operable let
          // a read-only form change +963 to +962 with nothing to save it.
          disabled={disabled}
          aria-invalid={aria['aria-invalid']}
        />
      </div>
      <div className="min-w-[9rem] flex-1">
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
    </div>
  )
}
