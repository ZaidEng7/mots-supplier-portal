import { useTranslation } from 'react-i18next'
import { AcceptInvitePageBase } from '../components/AcceptInvitePageBase'
import { resetPassword } from '../api/auth'

export function ResetPasswordPage() {
  const { t } = useTranslation()
  return (
    <AcceptInvitePageBase
      onSubmitToken={resetPassword}
      title={t('auth.resetTitle')}
      successMessage={t('auth.resetSuccess')}
      invalidMessage={t('auth.resetInvalid')}
      submitLabel={t('auth.resetSubmit')}
      passwordFieldLabel={t('auth.newPassword')}
      mapPasswordError={(raw) => raw}
      loginLinkLabel={t('auth.submit')}
    />
  )
}
