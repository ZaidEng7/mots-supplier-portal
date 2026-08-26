import type { ReactNode } from 'react'

export function Table({ children, caption }: { children: ReactNode; caption?: string }) {
  return (
    <div className="w-full overflow-x-auto rounded-[0.5rem]" style={{ border: '1px solid var(--color-border)' }}>
      <table className="w-full border-collapse text-[length:var(--text-body-sm)]">
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        {children}
      </table>
    </div>
  )
}

export function TableHead({ children }: { children: ReactNode }) {
  return (
    <thead style={{ backgroundColor: 'var(--color-bg-sunken)' }}>
      <tr>{children}</tr>
    </thead>
  )
}

export function TableHeaderCell({ children, scope = 'col' }: { children: ReactNode; scope?: 'col' | 'row' }) {
  return (
    <th
      scope={scope}
      className="px-4 py-2.5 text-start font-[var(--fw-semibold)]"
      style={{ color: 'var(--color-text-secondary)' }}
    >
      {children}
    </th>
  )
}

export function TableBody({ children }: { children: ReactNode }) {
  return <tbody>{children}</tbody>
}

export function TableRow({ children }: { children: ReactNode }) {
  return (
    <tr className="border-t" style={{ borderColor: 'var(--color-border)' }}>
      {children}
    </tr>
  )
}

export function TableCell({ children }: { children: ReactNode }) {
  return (
    <td className="px-4 py-2.5" style={{ color: 'var(--color-text-primary)' }}>
      {children}
    </td>
  )
}
