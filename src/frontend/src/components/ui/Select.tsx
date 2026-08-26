import * as RadixSelect from '@radix-ui/react-select'
import { Check, ChevronDown } from 'lucide-react'

export interface SelectOption {
  value: string
  label: string
}

interface SelectProps {
  id?: string
  value?: string
  onValueChange: (value: string) => void
  options: SelectOption[]
  placeholder?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean
}

/** Accessible select built on Radix — keyboard nav, typeahead, and screen-reader semantics for free.
 * Radix's placeholder text is visual only and does not contribute an accessible name, so the
 * trigger needs an explicit aria-label (falls back to the placeholder when no Field label wraps it). */
export function Select({ id, value, onValueChange, options, placeholder, ...aria }: SelectProps) {
  return (
    // Radix's hidden native <select> (mounted for form/autofill semantics) can emit a spurious
    // "" change once while the Portal content is still mounting, which would otherwise clobber a
    // just-restored controlled value - ignore empty emissions since none of our option sets
    // include a blank value to legitimately select.
    <RadixSelect.Root value={value} onValueChange={(v) => v && onValueChange(v)}>
      <RadixSelect.Trigger
        id={id}
        aria-label={placeholder}
        {...aria}
        className="flex w-full items-center justify-between gap-2 rounded-[0.375rem] px-3 py-2 text-[length:var(--text-body)] outline-none"
        style={{
          backgroundColor: 'var(--color-bg-surface)',
          color: 'var(--color-text-primary)',
          border: '1px solid var(--color-border-input)',
        }}
      >
        <RadixSelect.Value placeholder={placeholder}>{options.find((o) => o.value === value)?.label}</RadixSelect.Value>
        <RadixSelect.Icon>
          <ChevronDown size={16} aria-hidden="true" />
        </RadixSelect.Icon>
      </RadixSelect.Trigger>
      <RadixSelect.Portal>
        <RadixSelect.Content
          className="overflow-hidden rounded-[0.375rem] shadow-lg"
          style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}
        >
          <RadixSelect.Viewport className="p-1">
            {options.map((opt) => (
              <RadixSelect.Item
                key={opt.value}
                value={opt.value}
                className="flex cursor-pointer items-center justify-between rounded px-2 py-1.5 text-[length:var(--text-body)] outline-none data-[highlighted]:outline-none"
                style={{ color: 'var(--color-text-primary)' }}
                onPointerEnter={(e) => {
                  e.currentTarget.style.backgroundColor = 'var(--color-bg-hover)'
                }}
                onPointerLeave={(e) => {
                  e.currentTarget.style.backgroundColor = 'transparent'
                }}
              >
                <RadixSelect.ItemText>{opt.label}</RadixSelect.ItemText>
                <RadixSelect.ItemIndicator>
                  <Check size={14} aria-hidden="true" />
                </RadixSelect.ItemIndicator>
              </RadixSelect.Item>
            ))}
          </RadixSelect.Viewport>
        </RadixSelect.Content>
      </RadixSelect.Portal>
    </RadixSelect.Root>
  )
}
