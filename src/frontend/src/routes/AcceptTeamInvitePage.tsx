import { AcceptInvitePageBase } from '../components/AcceptInvitePageBase'
import { acceptTeamInvite } from '../api/team'

export function AcceptTeamInvitePage() {
  return <AcceptInvitePageBase acceptInvite={acceptTeamInvite} keyPrefix="team" />
}
