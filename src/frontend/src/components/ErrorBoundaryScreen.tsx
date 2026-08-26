import { useTranslation } from 'react-i18next'

interface Props {
  code: '404' | '403' | '500'
}

export function ErrorBoundaryScreen({ code }: Props) {
  const { t } = useTranslation()
  const messageKey = code === '404' ? 'notFound' : code === '403' ? 'forbidden' : 'serverError'

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 py-24 text-center">
      <span className="num text-6xl font-bold" style={{ color: 'var(--color-text-muted)' }}>
        {code}
      </span>
      <p className="text-lg" style={{ color: 'var(--color-text-secondary)' }}>
        {t(`errors.${messageKey}`)}
      </p>
    </div>
  )
}
