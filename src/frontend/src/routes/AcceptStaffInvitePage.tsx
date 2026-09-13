// A staff account setting its password from an invitation token. The whole screen is AcceptInvitePageBase; this file is
// the copy and the endpoint.

import { useTranslation } from 'react-i18next'
import { AcceptInvitePageBase } from '../components/AcceptInvitePageBase'
import { acceptStaffInvite } from '../api/staff'

export function AcceptStaffInvitePage() {
  const { t } = useTranslation()
  return (
    <AcceptInvitePageBase
      onSubmitToken={acceptStaffInvite}
      title={t('staff.acceptInviteTitle')}
      hint={t('staff.acceptInviteHint')}
      successMessage={t('staff.acceptInviteSuccess')}
      invalidMessage={t('staff.acceptInviteInvalid')}
      submitLabel={t('staff.acceptInviteSubmit')}
      passwordFieldLabel={t('auth.newPassword')}
      mapPasswordError={(raw) => (raw ? t('staff.errors.passwordTooShort') : undefined)}
      loginLinkLabel={t('auth.submit')}
    />
  )
}
