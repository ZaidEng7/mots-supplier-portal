// Setting a new password from a recovery token. The whole screen is AcceptInvitePageBase; this file is the copy and the
// endpoint - which is why the phase-12a finding that this page had "no loading, error or validation handling of any
// kind" was about the wrong file.

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
