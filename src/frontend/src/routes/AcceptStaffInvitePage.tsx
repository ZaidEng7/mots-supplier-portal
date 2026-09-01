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
