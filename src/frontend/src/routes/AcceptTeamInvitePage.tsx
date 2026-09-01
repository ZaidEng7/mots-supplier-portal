import { useTranslation } from 'react-i18next'
import { AcceptInvitePageBase } from '../components/AcceptInvitePageBase'
import { acceptTeamInvite } from '../api/team'

export function AcceptTeamInvitePage() {
  const { t } = useTranslation()
  return (
    <AcceptInvitePageBase
      onSubmitToken={acceptTeamInvite}
      title={t('team.acceptInviteTitle')}
      hint={t('team.acceptInviteHint')}
      successMessage={t('team.acceptInviteSuccess')}
      invalidMessage={t('team.acceptInviteInvalid')}
      submitLabel={t('team.acceptInviteSubmit')}
      passwordFieldLabel={t('auth.newPassword')}
      mapPasswordError={(raw) => (raw ? t('team.errors.passwordTooShort') : undefined)}
      loginLinkLabel={t('auth.submit')}
    />
  )
}
